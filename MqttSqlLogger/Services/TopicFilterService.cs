using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MqttSqlLogger.Services;

public class TopicFilterService
{
    private readonly ConcurrentDictionary<string, TopicState> _topics = new();
    private readonly string _connectionString;
    private readonly ILogger<TopicFilterService> _logger;
    private readonly string[] _defaultIgnorePaths = ["$.Timestamp"];
    private volatile bool _globalLoggingEnabled = true;

    public bool GlobalLoggingEnabled
    {
        get => _globalLoggingEnabled;
        set
        {
            _globalLoggingEnabled = value;
            _logger.LogInformation("Global logging {State}", value ? "enabled" : "disabled");
        }
    }

    public TopicFilterService(IConfiguration configuration, ILogger<TopicFilterService> logger)
    {
        _connectionString = configuration["SQL:CONNECTION_STRING"]
            ?? "Server=localhost;Database=MqttLogs;Trusted_Connection=True;TrustServerCertificate=True;";
        _logger = logger;
    }

    public class TopicState
    {
        public bool IsEnabled { get; set; }
        public string[] IgnoreJsonPaths { get; set; } = ["$.Timestamp"];
        public byte[]? LastPayloadHash { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public long MessagesReceived;
        public long MessagesLogged;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureTableAsync(ct);
            await LoadFiltersFromDatabaseAsync(ct);
            _logger.LogInformation("TopicFilterService initialized with {Count} filters",
                _topics.Count(t => t.Value.IsEnabled));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TopicFilterService");
        }
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblTopicFilter')
            BEGIN
                CREATE TABLE [dbo].[tblTopicFilter](
                    [Topic] NVARCHAR(256) NOT NULL PRIMARY KEY,
                    [IsEnabled] BIT NOT NULL DEFAULT 1,
                    [IgnoreJsonPaths] NVARCHAR(1000) NULL,
                    [LastPayloadHash] VARBINARY(32) NULL,
                    [LastSeenAt] DATETIME2 NULL,
                    [MessagesReceived] BIGINT NOT NULL DEFAULT 0,
                    [MessagesLogged] BIGINT NOT NULL DEFAULT 0,
                    [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task LoadFiltersFromDatabaseAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Topic, IsEnabled, IgnoreJsonPaths, LastPayloadHash, LastSeenAt, MessagesReceived, MessagesLogged FROM tblTopicFilter";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var topic = reader.GetString(0);
            var ignorePaths = reader.IsDBNull(2) ? _defaultIgnorePaths :
                JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? _defaultIgnorePaths;

            _topics[topic] = new TopicState
            {
                IsEnabled = reader.GetBoolean(1),
                IgnoreJsonPaths = ignorePaths,
                LastPayloadHash = reader.IsDBNull(3) ? null : (byte[])reader[3],
                LastSeenAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                MessagesReceived = reader.GetInt64(5),
                MessagesLogged = reader.GetInt64(6)
            };
        }
    }

    /// <summary>
    /// Called on every MQTT message. Returns true if message should be logged.
    /// </summary>
    public bool ShouldLog(string topic, ReadOnlySpan<byte> payload, out byte[] newHash)
    {
        var state = _topics.GetOrAdd(topic, _ => new TopicState { IsEnabled = false });

        Interlocked.Increment(ref state.MessagesReceived);
        state.LastSeenAt = DateTime.UtcNow;

        // Check global switch first
        if (!_globalLoggingEnabled)
        {
            newHash = [];
            return false;
        }

        if (!state.IsEnabled)
        {
            newHash = [];
            return false;
        }

        newHash = ComputeNormalizedHash(payload, state.IgnoreJsonPaths);

        if (state.LastPayloadHash != null && newHash.SequenceEqual(state.LastPayloadHash))
        {
            return false; // Duplicate payload
        }

        state.LastPayloadHash = newHash;
        Interlocked.Increment(ref state.MessagesLogged);
        return true;
    }

    public async Task<bool> ToggleTopicAsync(string topic, CancellationToken ct = default)
    {
        var state = _topics.GetOrAdd(topic, _ => new TopicState());
        state.IsEnabled = !state.IsEnabled;

        if (state.IsEnabled)
        {
            state.IgnoreJsonPaths = _defaultIgnorePaths;
        }

        await PersistTopicAsync(topic, state, ct);
        _logger.LogInformation("Topic {Topic} logging {State}", topic, state.IsEnabled ? "enabled" : "disabled");

        return state.IsEnabled;
    }

    public async Task EnableTopicAsync(string topic, string[]? ignorePaths = null, CancellationToken ct = default)
    {
        var state = _topics.GetOrAdd(topic, _ => new TopicState());
        state.IsEnabled = true;
        state.IgnoreJsonPaths = ignorePaths ?? _defaultIgnorePaths;

        await PersistTopicAsync(topic, state, ct);
        _logger.LogInformation("Topic {Topic} enabled with ignore paths: {Paths}", topic, state.IgnoreJsonPaths);
    }

    public async Task DisableTopicAsync(string topic, CancellationToken ct = default)
    {
        if (_topics.TryGetValue(topic, out var state))
        {
            state.IsEnabled = false;
            await PersistTopicAsync(topic, state, ct);
            _logger.LogInformation("Topic {Topic} disabled", topic);
        }
    }

    public async Task UpdateIgnorePathsAsync(string topic, string[] ignorePaths, CancellationToken ct = default)
    {
        if (_topics.TryGetValue(topic, out var state))
        {
            state.IgnoreJsonPaths = ignorePaths;
            state.LastPayloadHash = null; // Reset hash to log next message
            await PersistTopicAsync(topic, state, ct);
        }
    }

    private async Task PersistTopicAsync(string topic, TopicState state, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                MERGE [dbo].[tblTopicFilter] AS target
                USING (SELECT @Topic AS Topic) AS source ON target.Topic = source.Topic
                WHEN MATCHED THEN
                    UPDATE SET IsEnabled = @IsEnabled, IgnoreJsonPaths = @IgnoreJsonPaths,
                               LastPayloadHash = @LastPayloadHash, LastSeenAt = @LastSeenAt,
                               MessagesReceived = @MessagesReceived, MessagesLogged = @MessagesLogged
                WHEN NOT MATCHED THEN
                    INSERT (Topic, IsEnabled, IgnoreJsonPaths, LastPayloadHash, LastSeenAt, MessagesReceived, MessagesLogged)
                    VALUES (@Topic, @IsEnabled, @IgnoreJsonPaths, @LastPayloadHash, @LastSeenAt, @MessagesReceived, @MessagesLogged);";

            cmd.Parameters.AddWithValue("@Topic", topic);
            cmd.Parameters.AddWithValue("@IsEnabled", state.IsEnabled);
            cmd.Parameters.AddWithValue("@IgnoreJsonPaths", JsonSerializer.Serialize(state.IgnoreJsonPaths));
            cmd.Parameters.AddWithValue("@LastPayloadHash", (object?)state.LastPayloadHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LastSeenAt", (object?)state.LastSeenAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MessagesReceived", Interlocked.Read(ref state.MessagesReceived));
            cmd.Parameters.AddWithValue("@MessagesLogged", Interlocked.Read(ref state.MessagesLogged));

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist topic {Topic}", topic);
        }
    }

    public IEnumerable<TopicViewModel> GetAllTopics()
    {
        return _topics.Select(kvp => new TopicViewModel
        {
            Topic = kvp.Key,
            IsEnabled = kvp.Value.IsEnabled,
            IgnoreJsonPaths = kvp.Value.IgnoreJsonPaths,
            LastSeen = kvp.Value.LastSeenAt,
            MessagesReceived = Interlocked.Read(ref kvp.Value.MessagesReceived),
            MessagesLogged = Interlocked.Read(ref kvp.Value.MessagesLogged)
        }).OrderByDescending(t => t.LastSeen);
    }

    public StatsViewModel GetStats()
    {
        var topics = _topics.Values.ToList();
        return new StatsViewModel(
            TotalReceived: topics.Sum(t => Interlocked.Read(ref t.MessagesReceived)),
            TotalLogged: topics.Sum(t => Interlocked.Read(ref t.MessagesLogged)),
            TopicsDiscovered: topics.Count,
            TopicsEnabled: topics.Count(t => t.IsEnabled),
            LastMessageAt: topics.Max(t => t.LastSeenAt),
            GlobalLoggingEnabled: _globalLoggingEnabled
        );
    }

    public void SetGlobalLogging(bool enabled)
    {
        GlobalLoggingEnabled = enabled;
    }

    public async Task PersistAllAsync(CancellationToken ct = default)
    {
        foreach (var (topic, state) in _topics)
        {
            await PersistTopicAsync(topic, state, ct);
        }
        _logger.LogInformation("Persisted {Count} topic states", _topics.Count);
    }

    private static byte[] ComputeNormalizedHash(ReadOnlySpan<byte> payload, string[] ignorePaths)
    {
        if (payload.IsEmpty)
            return SHA256.HashData(payload);

        try
        {
            using var doc = JsonDocument.Parse(payload.ToArray());
            var normalized = RemoveIgnoredPaths(doc.RootElement, ignorePaths);
            var normalizedBytes = JsonSerializer.SerializeToUtf8Bytes(normalized,
                new JsonSerializerOptions { WriteIndented = false });
            return SHA256.HashData(normalizedBytes);
        }
        catch (JsonException)
        {
            // Not valid JSON - hash raw bytes
            return SHA256.HashData(payload);
        }
    }

    private static object? RemoveIgnoredPaths(JsonElement element, string[] ignorePaths)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => RemoveIgnoredPathsFromObject(element, ignorePaths),
            JsonValueKind.Array => element.EnumerateArray().Select(e => RemoveIgnoredPaths(e, ignorePaths)).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?> RemoveIgnoredPathsFromObject(JsonElement element, string[] ignorePaths)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            var path = $"$.{prop.Name}";
            if (ignorePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                continue;

            dict[prop.Name] = RemoveIgnoredPaths(prop.Value, ignorePaths);
        }
        return dict;
    }
}

public record TopicViewModel
{
    public string Topic { get; init; } = "";
    public bool IsEnabled { get; init; }
    public string[] IgnoreJsonPaths { get; init; } = ["$.Timestamp"];
    public DateTime? LastSeen { get; init; }
    public long MessagesReceived { get; init; }
    public long MessagesLogged { get; init; }

    public long MessagesFiltered => MessagesReceived - MessagesLogged;
    public double LogPercentage => MessagesReceived > 0
        ? Math.Round(100.0 * MessagesLogged / MessagesReceived, 1)
        : 0;
}

public record StatsViewModel(
    long TotalReceived,
    long TotalLogged,
    int TopicsDiscovered,
    int TopicsEnabled,
    DateTime? LastMessageAt,
    bool GlobalLoggingEnabled);

public record TopicListResponse(List<TopicViewModel> Topics, StatsViewModel Stats);

public record EnableTopicRequest(string[]? IgnoreJsonPaths = null);
