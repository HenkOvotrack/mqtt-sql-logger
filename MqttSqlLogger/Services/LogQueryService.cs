using System.Data;
using Microsoft.Data.SqlClient;

namespace MqttSqlLogger.Services;

public class LogQueryService
{
    private readonly string _connectionString;

    public LogQueryService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<LogQueryResult> QueryAsync(LogQueryRequest request, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Build WHERE clause
        var conditions = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(request.Topic))
        {
            if (request.Topic.Contains('*') || request.Topic.Contains('%'))
            {
                conditions.Add("[Topic] LIKE @Topic");
                parameters.Add(new SqlParameter("@Topic", SqlDbType.NVarChar, 256)
                    { Value = request.Topic.Replace('*', '%') });
            }
            else
            {
                conditions.Add("[Topic] = @Topic");
                parameters.Add(new SqlParameter("@Topic", SqlDbType.NVarChar, 256) { Value = request.Topic });
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            conditions.Add("[PayloadText] LIKE @Search");
            parameters.Add(new SqlParameter("@Search", SqlDbType.NVarChar, 256)
                { Value = $"%{request.Search}%" });
        }

        if (request.FromUtc.HasValue)
        {
            conditions.Add("[ReceivedAt] >= @FromUtc");
            parameters.Add(new SqlParameter("@FromUtc", SqlDbType.DateTime2) { Value = request.FromUtc.Value });
        }

        if (request.ToUtc.HasValue)
        {
            conditions.Add("[ReceivedAt] <= @ToUtc");
            parameters.Add(new SqlParameter("@ToUtc", SqlDbType.DateTime2) { Value = request.ToUtc.Value });
        }

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        // Get total count
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM [dbo].[tblMqttMessageLog] {whereClause}";
        countCmd.Parameters.AddRange(parameters.ToArray());
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        // Get paged results
        var offset = (request.Page - 1) * request.PageSize;
        await using var queryCmd = conn.CreateCommand();
        queryCmd.CommandText = $@"
            SELECT [ID], [ReceivedAt], [Topic], [QoS], [Retained], [ClientId], [PayloadText]
            FROM [dbo].[tblMqttMessageLog]
            {whereClause}
            ORDER BY [ReceivedAt] DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        // Clone parameters for second query
        foreach (var p in parameters)
        {
            queryCmd.Parameters.Add(new SqlParameter(p.ParameterName, p.SqlDbType, p.Size) { Value = p.Value });
        }
        queryCmd.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
        queryCmd.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = request.PageSize });

        var messages = new List<LogMessageViewModel>();
        await using var reader = await queryCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(new LogMessageViewModel
            {
                Id = reader.GetInt32(0),
                ReceivedAt = reader.GetDateTime(1),
                Topic = reader.GetString(2),
                QoS = reader.GetByte(3),
                Retained = reader.GetBoolean(4),
                ClientId = reader.IsDBNull(5) ? null : reader.GetString(5),
                PayloadText = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return new LogQueryResult
        {
            Messages = messages,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize)
        };
    }

    public async Task<List<string>> GetDistinctTopicsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT [Topic]
            FROM [dbo].[tblMqttMessageLog]
            ORDER BY [Topic]";

        var topics = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            topics.Add(reader.GetString(0));
        }
        return topics;
    }

    public async Task<LogMessageViewModel?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [ID], [ReceivedAt], [Topic], [QoS], [Retained], [ClientId], [PayloadText], [UserPropertiesJson]
            FROM [dbo].[tblMqttMessageLog]
            WHERE [ID] = @Id";
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new LogMessageViewModel
            {
                Id = reader.GetInt32(0),
                ReceivedAt = reader.GetDateTime(1),
                Topic = reader.GetString(2),
                QoS = reader.GetByte(3),
                Retained = reader.GetBoolean(4),
                ClientId = reader.IsDBNull(5) ? null : reader.GetString(5),
                PayloadText = reader.IsDBNull(6) ? null : reader.GetString(6),
                UserPropertiesJson = reader.IsDBNull(7) ? null : reader.GetString(7)
            };
        }
        return null;
    }
}

public class LogQueryRequest
{
    public string? Topic { get; set; }
    public string? Search { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class LogQueryResult
{
    public List<LogMessageViewModel> Messages { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class LogMessageViewModel
{
    public int Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string Topic { get; set; } = string.Empty;
    public byte QoS { get; set; }
    public bool Retained { get; set; }
    public string? ClientId { get; set; }
    public string? PayloadText { get; set; }
    public string? UserPropertiesJson { get; set; }
}
