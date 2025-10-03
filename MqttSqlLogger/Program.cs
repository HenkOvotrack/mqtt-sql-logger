// MqttSqlLogger - minimal, production-ready-ish single-file app
// .NET 8 Console + Generic Host + MQTTnet + Microsoft.Data.SqlClient
// Reads config from environment variables, subscribes to topics, and logs into SQL Server.
//
// Build:
//   dotnet new console -n MqttSqlLogger
//   cd MqttSqlLogger
//   dotnet add package MQTTnet --version 4.3.2.952
//   dotnet add package Microsoft.Data.SqlClient --version 5.2.1
//   replace Program.cs with this file
//   dotnet run
//
// Required environment variables (with sensible defaults shown):
//   MQTT__BROKER_HOST=localhost
//   MQTT__BROKER_PORT=1883
//   MQTT__CLIENT_ID=mqtt-sql-logger
//   MQTT__USERNAME= (optional)
//   MQTT__PASSWORD= (optional)
//   MQTT__TOPICS=tele/#,stat/#,shellies/#  (comma-separated list; default "#")
//   MQTT__QOS=1
//   SQL__CONNECTION_STRING=Server=localhost;Database=MqttLogs;User Id=sa;Password=Your_password123;TrustServerCertificate=True;
//   SQL__CREATE_TABLE=true   (set false to skip DDL)
//   LOG__LEVEL=Information   (Trace|Debug|Information|Warning|Error|Critical|None)
//   STARTUP__DELAY_MS=0      (milliseconds to wait before initial connection attempt; useful for docker-compose)
//
// Table created (if SQL__CREATE_TABLE=true):
//   [dbo].[tblMqttMessageLog]
// Columns:
//   [ID] INT IDENTITY(1,1) PRIMARY KEY
//   [ReceivedAt] DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
//   [Topic] NVARCHAR(256) NOT NULL
//   [QoS] TINYINT NOT NULL
//   [Retained] BIT NOT NULL
//   [ClientId] NVARCHAR(128) NULL
//   [PayloadText] NVARCHAR(MAX) NULL
//   [PayloadBytes] VARBINARY(MAX) NULL
//   [UserPropertiesJson] NVARCHAR(MAX) NULL
// Indexes:
//   IX_tblMqttMessageLog_ReceivedAt (descending) INCLUDE(Topic)
//   IX_tblMqttMessageLog_Topic_ReceivedAt
//
// Notes:
// - Logs both text and bytes. If payload is valid UTF-8, it's stored in PayloadText as-is.
// - Backpressure: single-row inserts for simplicity; upgrade to TVP/bulk insert for very high throughput.
// - Resilience: automatic reconnect with jittered exponential backoff; startup connection failures won't crash the service.
// - Observability: structured logging with message counters.

using System.Buffers;
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;

var builder = Host.CreateApplicationBuilder(args);

// Explicitly add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

var config = builder.Configuration;

var mqttHost = config["MQTT:BROKER_HOST"] ?? "localhost";
var mqttPort = int.TryParse(config["MQTT:BROKER_PORT"], out var p) ? p : 1883;
var clientId = config["MQTT:CLIENT_ID"] ?? $"mqtt-sql-logger-{Environment.MachineName}";
var username = config["MQTT:USERNAME"];
var password = config["MQTT:PASSWORD"];
var topicsCsv = config["MQTT:TOPICS"] ?? "#";
var qosLevel = byte.TryParse(config["MQTT:QOS"], out var qos) ? qos : (byte)1;
var sqlConnStr = config["SQL:CONNECTION_STRING"] ?? "Server=localhost;Database=MqttLogs;Trusted_Connection=True;TrustServerCertificate=True;";
var createTable = !string.Equals(config["SQL:CREATE_TABLE"], "false", StringComparison.OrdinalIgnoreCase);
var logLevel = config["LOG:LEVEL"] ?? "Information";
var startupDelayMs = int.TryParse(config["STARTUP:DELAY_MS"], out var delay) ? delay : 0;

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ "; o.UseUtcTimestamp = true;
});
if (Enum.TryParse<LogLevel>(logLevel, true, out var parsed))
{
    builder.Logging.SetMinimumLevel(parsed);
}

builder.Services.AddSingleton(new AppSettings(
    mqttHost, mqttPort, clientId, username, password,
    topicsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    qosLevel, sqlConnStr, createTable, startupDelayMs));

builder.Services.AddSingleton<IMqttClient>(_ => new MqttFactory().CreateMqttClient());

builder.Services.AddHostedService<MqttSqlLoggerService>();

var app = builder.Build();
await app.RunAsync();

record AppSettings(
    string BrokerHost,
    int BrokerPort,
    string ClientId,
    string? Username,
    string? Password,
    string[] Topics,
    byte Qos,
    string SqlConnectionString,
    bool CreateTable,
    int StartupDelayMs
);

class MqttSqlLoggerService : BackgroundService
{
    private readonly ILogger<MqttSqlLoggerService> _logger;
    private readonly AppSettings _settings;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _semaphore = new(1000, 1000);
    private readonly SemaphoreSlim _reconnectLock = new(1, 1); // Prevent multiple simultaneous reconnections

    private long _msgCount;

    public MqttSqlLoggerService(ILogger<MqttSqlLoggerService> logger, AppSettings settings, IMqttClient client)
    {
        _logger = logger; _settings = settings; _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, __) => _client.DisconnectAsync();

        // Optional startup delay to allow dependent services to initialize
        if (_settings.StartupDelayMs > 0)
        {
            _logger.LogInformation("Waiting {DelayMs} ms for dependent services to start...", _settings.StartupDelayMs);
            try
            {
                await Task.Delay(_settings.StartupDelayMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return; // Service is shutting down
            }
        }

        if (_settings.CreateTable)
        {
            try { await EnsureTableAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to ensure SQL table"); }
        }

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var payload = e.ApplicationMessage.PayloadSegment;
                var text = TryGetUtf8(payload, out var s) ? s : null;
                var propsJson = UserPropsToJson(e.ApplicationMessage.UserProperties);

                await InsertRowAsync(
                    receivedAtUtc: DateTime.UtcNow,
                    topic: e.ApplicationMessage.Topic ?? string.Empty,
                    qos: (byte)e.ApplicationMessage.QualityOfServiceLevel,
                    retained: e.ApplicationMessage.Retain,
                    clientId: _settings.ClientId,
                    payloadText: text,
                    payloadBytes: payload,
                    userPropsJson: propsJson,
                    stoppingToken);

                var c = Interlocked.Increment(ref _msgCount);
                if (c % 100 == 0)
                {
                    _logger.LogInformation("Inserted {Count} messages (latest topic: {Topic})", c, e.ApplicationMessage.Topic);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Insert failed for topic {Topic}", e.ApplicationMessage.Topic);
            }
        };

        _client.DisconnectedAsync += async e =>
        {
            if (stoppingToken.IsCancellationRequested) return;
            
            // Prevent multiple simultaneous reconnection attempts
            if (!_reconnectLock.Wait(0))
            {
                _logger.LogDebug("Another reconnection attempt is already in progress, skipping");
                return;
            }
            
            try
            {
                var delayMs = RandomJitteredBackoffMs(e.Reason);
                _logger.LogWarning("MQTT disconnected: {Reason}. Reconnecting in {DelayMs} ms...", e.Reason, delayMs);
                await Task.Delay(delayMs, stoppingToken);
                await ConnectAndSubscribeWithRetryAsync(stoppingToken);
            }
            finally
            {
                _reconnectLock.Release();
            }
        };

        // Initial connection with retry loop
        await ConnectAndSubscribeWithRetryAsync(stoppingToken);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ConnectAndSubscribeWithRetryAsync(CancellationToken ct)
    {
        // Use the reconnection lock to prevent multiple simultaneous retry attempts
        await _reconnectLock.WaitAsync(ct);
        
        try
        {
            var retryCount = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndSubscribeAsync(ct);
                    return; // Success - exit retry loop
                }
                catch (Exception ex)
                {
                    retryCount++;
                    var isFirstAttempt = retryCount == 1;
                    var logLevel = isFirstAttempt ? LogLevel.Information : LogLevel.Warning;

                    _logger.Log(logLevel, ex,
                        "MQTT connection attempt {RetryCount} failed. {Message}. Will retry...",
                        retryCount,
                        ex.Message);

                    // Use exponential backoff with jitter for startup retries
                    var baseDelayMs = Math.Min(1000 * (int)Math.Pow(2, Math.Min(retryCount - 1, 6)), 30000); // Cap at 30 seconds
                    var jitter = Random.Shared.Next(0, 2000);
                    var delayMs = baseDelayMs + jitter;

                    _logger.LogInformation("Waiting {DelayMs} ms before retry attempt {NextAttempt}...", delayMs, retryCount + 1);

                    try
                    {
                        await Task.Delay(delayMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // Service is shutting down
                    }
                }
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken ct)
    {
        // Check if already connected
        if (_client.IsConnected)
        {
            _logger.LogDebug("MQTT client is already connected, skipping connection attempt");
            return;
        }

        // Ensure client is disconnected before attempting to connect
        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync(cancellationToken: ct);
                _logger.LogDebug("Disconnected existing MQTT connection before reconnecting");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disconnect existing MQTT connection, proceeding with new connection");
            }
        }

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(_settings.ClientId)
            .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            optionsBuilder = optionsBuilder.WithCredentials(_settings.Username, _settings.Password);
        }

        var options = optionsBuilder.Build();

        _logger.LogInformation("Connecting to MQTT {Host}:{Port} as {ClientId}...", _settings.BrokerHost, _settings.BrokerPort, _settings.ClientId);
        await _client.ConnectAsync(options, ct);
        _logger.LogInformation("Connected. Subscribing to {Count} topic(s)...", _settings.Topics.Length);

        foreach (var topic in _settings.Topics)
        {
            var filter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel((MQTTnet.Protocol.MqttQualityOfServiceLevel)_settings.Qos)
                .Build();
            await _client.SubscribeAsync(filter, ct);
            _logger.LogInformation("Subscribed to {Topic} (QoS {Qos})", topic, _settings.Qos);
        }
    }

    private static int RandomJitteredBackoffMs(MqttClientDisconnectReason reason)
    {
        // Basic backoff: faster retry on broker unavailable; slower for auth
        var baseMs = reason switch
        {
            MqttClientDisconnectReason.NormalDisconnection => 2000,
            MqttClientDisconnectReason.UnspecifiedError => 3000,
            MqttClientDisconnectReason.ServerBusy => 1500,
            MqttClientDisconnectReason.ProtocolError => 4000,
            MqttClientDisconnectReason.AdministrativeAction => 4000,
            _ => 5000
        };
        var jitter = Random.Shared.Next(0, 2000);
        return baseMs + jitter;
    }

    private static bool TryGetUtf8(ReadOnlyMemory<byte> bytes, out string? text)
    {
        try
        {
            if (bytes.IsEmpty) { text = string.Empty; return true; }
            var span = bytes.Span;
            // Validate by attempting to decode; if invalid, catch and return false
            text = Encoding.UTF8.GetString(span);
            return true;
        }
        catch
        {
            text = null; return false;
        }
    }

    private static string UserPropsToJson(List<MqttUserProperty>? props)
    {
        if (props == null || props.Count == 0) return "{}";
        // Very small manual JSON to avoid extra dependencies
        var sb = new StringBuilder();
        sb.Append('{');
        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            sb.Append('\"').Append(Escape(p.Name)).Append('\"').Append(':')
              .Append('\"').Append(Escape(p.Value)).Append('\"');
            if (i < props.Count - 1) sb.Append(',');
        }
        sb.Append('}');
        return sb.ToString();

        static string Escape(string? s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_settings.SqlConnectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblMqttMessageLog' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[tblMqttMessageLog](
        [ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblMqttMessageLog PRIMARY KEY,
        [ReceivedAt] DATETIME2(3) NOT NULL CONSTRAINT DF_tblMqttMessageLog_ReceivedAt DEFAULT SYSUTCDATETIME(),
        [Topic] NVARCHAR(256) NOT NULL,
        [QoS] TINYINT NOT NULL,
        [Retained] BIT NOT NULL,
        [ClientId] NVARCHAR(128) NULL,
        [PayloadText] NVARCHAR(MAX) NULL,
        [PayloadBytes] VARBINARY(MAX) NULL,
        [UserPropertiesJson] NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_tblMqttMessageLog_ReceivedAt ON [dbo].[tblMqttMessageLog]([ReceivedAt] DESC) INCLUDE([Topic]);
    CREATE INDEX IX_tblMqttMessageLog_Topic_ReceivedAt ON [dbo].[tblMqttMessageLog]([Topic], [ReceivedAt] DESC);
END";
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Ensured table [dbo].[tblMqttMessageLog] exists.");
    }

    private async Task InsertRowAsync(
        DateTime receivedAtUtc,
        string topic,
        byte qos,
        bool retained,
        string clientId,
        string? payloadText,
        ReadOnlyMemory<byte> payloadBytes,
        string userPropsJson,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_settings.SqlConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO [dbo].[tblMqttMessageLog]
            ([ReceivedAt],[Topic],[QoS],[Retained],[ClientId],[PayloadText],[PayloadBytes],[UserPropertiesJson])
            VALUES (@ReceivedAt,@Topic,@QoS,@Retained,@ClientId,@PayloadText,@PayloadBytes,@UserPropertiesJson)";

        cmd.Parameters.Add(new SqlParameter("@ReceivedAt", SqlDbType.DateTime2) { Value = receivedAtUtc });
        cmd.Parameters.Add(new SqlParameter("@Topic", SqlDbType.NVarChar, 256) { Value = topic });
        cmd.Parameters.Add(new SqlParameter("@QoS", SqlDbType.TinyInt) { Value = qos });
        cmd.Parameters.Add(new SqlParameter("@Retained", SqlDbType.Bit) { Value = retained });
        cmd.Parameters.Add(new SqlParameter("@ClientId", SqlDbType.NVarChar, 128) { Value = (object?)clientId ?? DBNull.Value });

        cmd.Parameters.Add(new SqlParameter("@PayloadText", SqlDbType.NVarChar, -1) { Value = (object?)payloadText ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PayloadBytes", SqlDbType.VarBinary, -1) { Value = (object?)payloadBytes.ToArray() ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@UserPropertiesJson", SqlDbType.NVarChar, -1) { Value = (object?)userPropsJson ?? DBNull.Value });

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
