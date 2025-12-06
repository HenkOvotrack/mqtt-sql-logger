using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MqttSqlLogger.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration from environment variables
builder.Configuration.AddEnvironmentVariables();

var config = builder.Configuration;
var mqttHost = config["MQTT:BROKER_HOST"] ?? "localhost";
var mqttPort = int.TryParse(config["MQTT:BROKER_PORT"], out var p) ? p : 1883;
var clientId = config["MQTT:CLIENT_ID"] ?? $"mqtt-sql-logger-{Environment.MachineName}";
var username = config["MQTT:USERNAME"];
var password = config["MQTT:PASSWORD"];
var sqlConnStr = config["SQL:CONNECTION_STRING"] ?? "Server=localhost;Database=MqttLogs;Trusted_Connection=True;TrustServerCertificate=True;";
var createTable = !string.Equals(config["SQL:CREATE_TABLE"], "false", StringComparison.OrdinalIgnoreCase);
var logLevel = config["LOG:LEVEL"] ?? "Information";
var startupDelayMs = int.TryParse(config["STARTUP:DELAY_MS"], out var delay) ? delay : 0;
var pathBase = config["PATH_BASE"] ?? "/";

// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    o.UseUtcTimestamp = true;
});
if (Enum.TryParse<LogLevel>(logLevel, true, out var parsed))
{
    builder.Logging.SetMinimumLevel(parsed);
}

// Register services
builder.Services.AddSingleton(new AppSettings(
    mqttHost, mqttPort, clientId, username, password,
    sqlConnStr, createTable, startupDelayMs));

builder.Services.AddSingleton<TopicFilterService>();
builder.Services.AddSingleton(_ => new LogQueryService(sqlConnStr));
builder.Services.AddSingleton<IMqttClient>(_ => new MqttFactory().CreateMqttClient());
builder.Services.AddHostedService<MqttLoggerService>();

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Initialize filter service
var filterService = app.Services.GetRequiredService<TopicFilterService>();
await filterService.InitializeAsync();

// Path base for reverse proxy (must be before other middleware)
if (!string.IsNullOrEmpty(pathBase) && pathBase != "/")
{
    app.UsePathBase(pathBase);
}

// Static files and Blazor
app.UseStaticFiles();
app.UseAntiforgery();

// API Endpoints
app.MapGet("/api/topics", (TopicFilterService svc) =>
    Results.Ok(new TopicListResponse(
        svc.GetAllTopics().ToList(),
        svc.GetStats())));

app.MapPost("/api/topics/toggle/{*topic}", async (string topic, TopicFilterService svc) =>
{
    var decodedTopic = Uri.UnescapeDataString(topic);
    var newState = await svc.ToggleTopicAsync(decodedTopic);
    return Results.Ok(new { topic = decodedTopic, isEnabled = newState });
});

app.MapPost("/api/topics/enable/{*topic}", async (string topic, EnableTopicRequest? request, TopicFilterService svc) =>
{
    var decodedTopic = Uri.UnescapeDataString(topic);
    await svc.EnableTopicAsync(decodedTopic, request?.IgnoreJsonPaths);
    return Results.Ok();
});

app.MapDelete("/api/topics/disable/{*topic}", async (string topic, TopicFilterService svc) =>
{
    var decodedTopic = Uri.UnescapeDataString(topic);
    await svc.DisableTopicAsync(decodedTopic);
    return Results.Ok();
});

app.MapPut("/api/topics/ignore-paths/{*topic}", async (string topic, string[] ignorePaths, TopicFilterService svc) =>
{
    var decodedTopic = Uri.UnescapeDataString(topic);
    await svc.UpdateIgnorePathsAsync(decodedTopic, ignorePaths);
    return Results.Ok();
});

app.MapGet("/api/stats", (TopicFilterService svc) =>
    Results.Ok(svc.GetStats()));

app.MapPost("/api/logging/toggle", (TopicFilterService svc) =>
{
    svc.SetGlobalLogging(!svc.GlobalLoggingEnabled);
    return Results.Ok(new { globalLoggingEnabled = svc.GlobalLoggingEnabled });
});

app.MapPost("/api/logging/enable", (TopicFilterService svc) =>
{
    svc.SetGlobalLogging(true);
    return Results.Ok();
});

app.MapPost("/api/logging/disable", (TopicFilterService svc) =>
{
    svc.SetGlobalLogging(false);
    return Results.Ok();
});

// Log query endpoints
app.MapGet("/api/logs", async (
    string? topic,
    string? search,
    DateTime? from,
    DateTime? to,
    int? page,
    int? pageSize,
    LogQueryService logSvc) =>
{
    var result = await logSvc.QueryAsync(new LogQueryRequest
    {
        Topic = topic,
        Search = search,
        FromUtc = from,
        ToUtc = to,
        Page = page ?? 1,
        PageSize = Math.Min(pageSize ?? 50, 200)
    });
    return Results.Ok(result);
});

app.MapGet("/api/logs/topics", async (LogQueryService logSvc) =>
    Results.Ok(await logSvc.GetDistinctTopicsAsync()));

app.MapGet("/api/logs/{id:int}", async (int id, LogQueryService logSvc) =>
{
    var msg = await logSvc.GetByIdAsync(id);
    return msg != null ? Results.Ok(msg) : Results.NotFound();
});

// Blazor components
app.MapRazorComponents<MqttSqlLogger.Components.App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

// Configuration record
record AppSettings(
    string BrokerHost,
    int BrokerPort,
    string ClientId,
    string? Username,
    string? Password,
    string SqlConnectionString,
    bool CreateTable,
    int StartupDelayMs
);

// Background service for MQTT
class MqttLoggerService : BackgroundService
{
    private readonly ILogger<MqttLoggerService> _logger;
    private readonly AppSettings _settings;
    private readonly IMqttClient _client;
    private readonly TopicFilterService _filterService;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    private long _msgCount;

    public MqttLoggerService(
        ILogger<MqttLoggerService> logger,
        AppSettings settings,
        IMqttClient client,
        TopicFilterService filterService)
    {
        _logger = logger;
        _settings = settings;
        _client = client;
        _filterService = filterService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AppDomain.CurrentDomain.ProcessExit += async (_, __) =>
        {
            await _filterService.PersistAllAsync();
            await _client.DisconnectAsync();
        };

        // Optional startup delay
        if (_settings.StartupDelayMs > 0)
        {
            _logger.LogInformation("Waiting {DelayMs} ms for dependent services...", _settings.StartupDelayMs);
            try { await Task.Delay(_settings.StartupDelayMs, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        if (_settings.CreateTable)
        {
            try { await EnsureTableAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to ensure SQL table"); }
        }

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            var topic = e.ApplicationMessage.Topic ?? string.Empty;
            var payload = e.ApplicationMessage.PayloadSegment;

            // Check filter and dedup
            if (!_filterService.ShouldLog(topic, payload.AsSpan(), out var hash))
            {
                return; // Filtered or duplicate
            }

            try
            {
                var text = TryGetUtf8(payload, out var s) ? s : null;
                var propsJson = UserPropsToJson(e.ApplicationMessage.UserProperties);

                await InsertRowAsync(
                    receivedAtUtc: DateTime.UtcNow,
                    topic: topic,
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
                    _logger.LogInformation("Logged {Count} messages (latest: {Topic})", c, topic);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Insert failed for topic {Topic}", topic);
            }
        };

        _client.DisconnectedAsync += async e =>
        {
            if (stoppingToken.IsCancellationRequested) return;

            if (!_reconnectLock.Wait(0))
            {
                _logger.LogDebug("Reconnection already in progress");
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

        await ConnectAndSubscribeWithRetryAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ConnectAndSubscribeWithRetryAsync(CancellationToken ct)
    {
        await _reconnectLock.WaitAsync(ct);
        try
        {
            var retryCount = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndSubscribeAsync(ct);
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    var logLvl = retryCount == 1 ? LogLevel.Information : LogLevel.Warning;
                    _logger.Log(logLvl, ex, "MQTT connection attempt {RetryCount} failed", retryCount);

                    var baseDelayMs = Math.Min(1000 * (int)Math.Pow(2, Math.Min(retryCount - 1, 6)), 30000);
                    var delayMs = baseDelayMs + Random.Shared.Next(0, 2000);
                    _logger.LogInformation("Waiting {DelayMs} ms before retry {Next}...", delayMs, retryCount + 1);

                    try { await Task.Delay(delayMs, ct); }
                    catch (OperationCanceledException) { return; }
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
        if (_client.IsConnected)
        {
            _logger.LogDebug("MQTT already connected");
            return;
        }

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(_settings.ClientId)
            .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            optionsBuilder = optionsBuilder.WithCredentials(_settings.Username, _settings.Password);
        }

        _logger.LogInformation("Connecting to MQTT {Host}:{Port} as {ClientId}...",
            _settings.BrokerHost, _settings.BrokerPort, _settings.ClientId);

        await _client.ConnectAsync(optionsBuilder.Build(), ct);
        _logger.LogInformation("Connected. Subscribing to # (all topics)...");

        // Subscribe to all topics for discovery
        var filter = new MqttTopicFilterBuilder()
            .WithTopic("#")
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.SubscribeAsync(filter, ct);
        _logger.LogInformation("Subscribed to # - topic discovery active");
    }

    private static int RandomJitteredBackoffMs(MqttClientDisconnectReason reason)
    {
        var baseMs = reason switch
        {
            MqttClientDisconnectReason.NormalDisconnection => 2000,
            MqttClientDisconnectReason.UnspecifiedError => 3000,
            MqttClientDisconnectReason.ServerBusy => 1500,
            MqttClientDisconnectReason.ProtocolError => 4000,
            MqttClientDisconnectReason.AdministrativeAction => 4000,
            _ => 5000
        };
        return baseMs + Random.Shared.Next(0, 2000);
    }

    private static bool TryGetUtf8(ReadOnlyMemory<byte> bytes, out string? text)
    {
        try
        {
            if (bytes.IsEmpty) { text = string.Empty; return true; }
            text = Encoding.UTF8.GetString(bytes.Span);
            return true;
        }
        catch
        {
            text = null;
            return false;
        }
    }

    private static string UserPropsToJson(List<MqttUserProperty>? props)
    {
        if (props == null || props.Count == 0) return "{}";
        var sb = new StringBuilder();
        sb.Append('{');
        for (int i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            sb.Append('\"').Append(Escape(prop.Name)).Append("\":\"").Append(Escape(prop.Value)).Append('\"');
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

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblMqttMessageLog' AND schema_id = SCHEMA_ID('dbo'))
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
        cmd.CommandText = @"
            INSERT INTO [dbo].[tblMqttMessageLog]
                ([ReceivedAt],[Topic],[QoS],[Retained],[ClientId],[PayloadText],[PayloadBytes],[UserPropertiesJson])
            VALUES
                (@ReceivedAt,@Topic,@QoS,@Retained,@ClientId,@PayloadText,@PayloadBytes,@UserPropertiesJson)";

        cmd.Parameters.Add(new SqlParameter("@ReceivedAt", SqlDbType.DateTime2) { Value = receivedAtUtc });
        cmd.Parameters.Add(new SqlParameter("@Topic", SqlDbType.NVarChar, 256) { Value = topic });
        cmd.Parameters.Add(new SqlParameter("@QoS", SqlDbType.TinyInt) { Value = qos });
        cmd.Parameters.Add(new SqlParameter("@Retained", SqlDbType.Bit) { Value = retained });
        cmd.Parameters.Add(new SqlParameter("@ClientId", SqlDbType.NVarChar, 128) { Value = (object?)clientId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PayloadText", SqlDbType.NVarChar, -1) { Value = (object?)payloadText ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@PayloadBytes", SqlDbType.VarBinary, -1) { Value = payloadBytes.ToArray() });
        cmd.Parameters.Add(new SqlParameter("@UserPropertiesJson", SqlDbType.NVarChar, -1) { Value = userPropsJson });

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
