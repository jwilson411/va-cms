using System.Data;
using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data;

namespace VA.CMS.Infrastructure.Outbox;

/// <summary>
/// [OutboundEvent] data access, all through usp_OutboundEvent_* (NFR-DB-01). Issue #171.
/// Registered as a singleton on the connection string (like <c>SiteSettingRepository</c>)
/// so the hosted worker and scoped request services share one implementation.
/// </summary>
public interface IOutboxRepository
{
    /// <summary>
    /// Write the row(s) for one event. For <see cref="OutboundEventTypes.Webhook"/> the stored
    /// procedure fans out to every active subscriber of <paramref name="eventName"/>; returns
    /// how many rows were written (0 when nothing subscribes).
    /// </summary>
    Task<int> EnqueueAsync(string type, string? eventName, string payloadJson, CancellationToken ct = default);

    /// <summary>Claim up to <paramref name="batchSize"/> due rows for <paramref name="lockedBy"/>.</summary>
    Task<IReadOnlyList<OutboundEvent>> ClaimAsync(string lockedBy, int batchSize, int leaseSeconds, CancellationToken ct = default);

    Task CompleteAsync(long id, string lockedBy, CancellationToken ct = default);
    Task RescheduleAsync(long id, string lockedBy, DateTime nextAttemptAtUtc, string? lastError, CancellationToken ct = default);
    Task FailAsync(long id, string lockedBy, string? lastError, CancellationToken ct = default);

    /// <summary>Delete completed rows older than <paramref name="olderThanDays"/>; returns the count.</summary>
    Task<int> PurgeAsync(int olderThanDays, CancellationToken ct = default);

    Task<OutboxStats> GetStatsAsync(CancellationToken ct = default);
}

public sealed class OutboxRepository : IOutboxRepository
{
    private readonly string _connectionString;

    public OutboxRepository(CmsDatabase db) : this(db.ConnectionString) { }

    public OutboxRepository(string connectionString) => _connectionString = connectionString;

    public async Task<int> EnqueueAsync(string type, string? eventName, string payloadJson, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Enqueue @Type, @EventName, @PayloadJson, @Count OUTPUT";
        cmd.Parameters.AddWithValue("@Type",        type);
        cmd.Parameters.AddWithValue("@EventName",   (object?)eventName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PayloadJson", payloadJson);
        var count = cmd.Parameters.Add("@Count", SqlDbType.Int);
        count.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return count.Value is int n ? n : 0;
    }

    public async Task<IReadOnlyList<OutboundEvent>> ClaimAsync(string lockedBy, int batchSize, int leaseSeconds, CancellationToken ct = default)
    {
        var rows = new List<OutboundEvent>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Claim @LockedBy, @BatchSize, @LeaseSeconds";
        cmd.Parameters.AddWithValue("@LockedBy",     lockedBy);
        cmd.Parameters.AddWithValue("@BatchSize",    batchSize);
        cmd.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(Map(reader));
        return rows;
    }

    public async Task CompleteAsync(long id, string lockedBy, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Complete @Id, @LockedBy";
        cmd.Parameters.AddWithValue("@Id",       id);
        cmd.Parameters.AddWithValue("@LockedBy", lockedBy);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RescheduleAsync(long id, string lockedBy, DateTime nextAttemptAtUtc, string? lastError, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Reschedule @Id, @LockedBy, @NextAttemptAt, @LastError";
        cmd.Parameters.AddWithValue("@Id",            id);
        cmd.Parameters.AddWithValue("@LockedBy",      lockedBy);
        cmd.Parameters.AddWithValue("@NextAttemptAt", nextAttemptAtUtc);
        cmd.Parameters.AddWithValue("@LastError",     (object?)Truncate(lastError) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task FailAsync(long id, string lockedBy, string? lastError, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Fail @Id, @LockedBy, @LastError";
        cmd.Parameters.AddWithValue("@Id",        id);
        cmd.Parameters.AddWithValue("@LockedBy",  lockedBy);
        cmd.Parameters.AddWithValue("@LastError", (object?)Truncate(lastError) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PurgeAsync(int olderThanDays, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Purge @OlderThanDays, @Deleted OUTPUT";
        cmd.Parameters.AddWithValue("@OlderThanDays", olderThanDays);
        var deleted = cmd.Parameters.Add("@Deleted", SqlDbType.Int);
        deleted.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return deleted.Value is int n ? n : 0;
    }

    public async Task<OutboxStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_OutboundEvent_Stats";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new OutboxStats(0, 0, 0, null);
        return new OutboxStats(
            Pending:         reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader.GetValue(0)),
            Succeeded:       reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1)),
            Failed:          reader.IsDBNull(2) ? 0 : Convert.ToInt64(reader.GetValue(2)),
            OldestPendingAt: reader.IsDBNull(3) ? null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc));
    }

    private static string? Truncate(string? s) => s is { Length: > 2000 } ? s[..2000] : s;

    private static OutboundEvent Map(SqlDataReader r) => new()
    {
        Id            = r.GetInt64(r.GetOrdinal("Id")),
        Type          = r.GetString(r.GetOrdinal("Type")),
        EventName     = r.IsDBNull(r.GetOrdinal("EventName"))   ? null : r.GetString(r.GetOrdinal("EventName")),
        WebhookId     = r.IsDBNull(r.GetOrdinal("WebhookId"))   ? null : r.GetInt64(r.GetOrdinal("WebhookId")),
        PayloadJson   = r.GetString(r.GetOrdinal("PayloadJson")),
        Status        = r.GetString(r.GetOrdinal("Status")),
        Attempts      = r.GetInt32(r.GetOrdinal("Attempts")),
        NextAttemptAt = Utc(r.GetDateTime(r.GetOrdinal("NextAttemptAt"))),
        LockedBy      = r.IsDBNull(r.GetOrdinal("LockedBy"))    ? null : r.GetString(r.GetOrdinal("LockedBy")),
        LockedAt      = r.IsDBNull(r.GetOrdinal("LockedAt"))    ? null : Utc(r.GetDateTime(r.GetOrdinal("LockedAt"))),
        LastError     = r.IsDBNull(r.GetOrdinal("LastError"))   ? null : r.GetString(r.GetOrdinal("LastError")),
        CreatedAt     = Utc(r.GetDateTime(r.GetOrdinal("CreatedAt"))),
        CompletedAt   = r.IsDBNull(r.GetOrdinal("CompletedAt")) ? null : Utc(r.GetDateTime(r.GetOrdinal("CompletedAt"))),
    };

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
