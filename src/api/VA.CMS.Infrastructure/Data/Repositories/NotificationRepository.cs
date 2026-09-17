using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// In-app notification repository.
/// All DB access via EXEC usp_Notification_* stored procedures (NFR-DB-01).
/// Issue #38 — BRD FR-WORKFLOW-02 / FR-WORKFLOW-03.
/// </summary>
public class NotificationRepository : INotificationRepository
{
    private readonly CmsDatabase _db;

    public NotificationRepository(CmsDatabase db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationRecipient>> CreateForWorkflowEventAsync(long contentEntryId, string eventType, long actorId, string? comment = null)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_Notification_CreateForWorkflowEvent @ContentEntryId, @EventType, @ActorId, @Comment, @Count OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", contentEntryId);
        cmd.Parameters.AddWithValue("@EventType",      eventType);
        cmd.Parameters.AddWithValue("@ActorId",        actorId);
        cmd.Parameters.AddWithValue("@Comment",        (object?)comment ?? DBNull.Value);

        var count = cmd.Parameters.Add("@Count", SqlDbType.Int);
        count.Direction = ParameterDirection.Output;

        var results = new List<NotificationRecipient>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapRecipient(reader));

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Notification>> ListForUserAsync(long userId, bool unreadOnly = false, int limit = 50)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Notification_ListForUser @UserId, @UnreadOnly, @Limit";
        cmd.Parameters.AddWithValue("@UserId",     userId);
        cmd.Parameters.AddWithValue("@UnreadOnly", unreadOnly);
        cmd.Parameters.AddWithValue("@Limit",      limit);

        var results = new List<Notification>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapNotification(reader));

        return results;
    }

    /// <inheritdoc />
    public async Task<int> UnreadCountAsync(long userId)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Notification_UnreadCount @UserId";
        cmd.Parameters.AddWithValue("@UserId", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is int n ? n : 0;
    }

    /// <inheritdoc />
    public async Task<int> MarkReadAsync(long userId, IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return 0;

        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Notification_MarkRead @UserId, @IdsJson";
        cmd.Parameters.AddWithValue("@UserId",  userId);
        cmd.Parameters.AddWithValue("@IdsJson", JsonSerializer.Serialize(ids));
        var result = await cmd.ExecuteScalarAsync();
        return result is int n ? n : 0;
    }

    /// <inheritdoc />
    public async Task<int> MarkAllReadAsync(long userId)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Notification_MarkAllRead @UserId";
        cmd.Parameters.AddWithValue("@UserId", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is int n ? n : 0;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static NotificationRecipient MapRecipient(SqlDataReader r)
    {
        var ordActorName = r.GetOrdinal("ActorDisplayName");
        var ordComment   = r.GetOrdinal("Comment");

        return new NotificationRecipient
        {
            Id                   = r.GetInt64(r.GetOrdinal("Id")),
            RecipientUserId      = r.GetInt64(r.GetOrdinal("RecipientUserId")),
            RecipientEmail       = r.GetString(r.GetOrdinal("RecipientEmail")),
            RecipientDisplayName = r.GetString(r.GetOrdinal("RecipientDisplayName")),
            EventType            = r.GetString(r.GetOrdinal("EventType")),
            ContentEntryId       = r.GetInt64(r.GetOrdinal("ContentEntryId")),
            ContentTitle         = r.GetString(r.GetOrdinal("ContentTitle")),
            Message              = r.GetString(r.GetOrdinal("Message")),
            ActorDisplayName     = r.IsDBNull(ordActorName) ? null : r.GetString(ordActorName),
            Comment              = r.IsDBNull(ordComment)   ? null : r.GetString(ordComment),
        };
    }

    private static Notification MapNotification(SqlDataReader r)
    {
        var ordActorId   = r.GetOrdinal("ActorId");
        var ordActorName = r.GetOrdinal("ActorDisplayName");
        var ordComment   = r.GetOrdinal("Comment");
        var ordReadAt    = r.GetOrdinal("ReadAt");
        var ordSlug      = r.GetOrdinal("EntrySlug");
        var ordStatus    = r.GetOrdinal("EntryStatus");

        return new Notification
        {
            Id               = r.GetInt64(r.GetOrdinal("Id")),
            RecipientUserId  = r.GetInt64(r.GetOrdinal("RecipientUserId")),
            EventType        = r.GetString(r.GetOrdinal("EventType")),
            ContentEntryId   = r.GetInt64(r.GetOrdinal("ContentEntryId")),
            ContentTitle     = r.GetString(r.GetOrdinal("ContentTitle")),
            Message          = r.GetString(r.GetOrdinal("Message")),
            ActorId          = r.IsDBNull(ordActorId)   ? null : r.GetInt64(ordActorId),
            ActorDisplayName = r.IsDBNull(ordActorName) ? null : r.GetString(ordActorName),
            Comment          = r.IsDBNull(ordComment)   ? null : r.GetString(ordComment),
            IsRead           = r.GetBoolean(r.GetOrdinal("IsRead")),
            ReadAt           = r.IsDBNull(ordReadAt)    ? null : r.GetDateTime(ordReadAt),
            CreatedAt        = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            EntrySlug        = r.IsDBNull(ordSlug)      ? null : r.GetString(ordSlug),
            EntryStatus      = r.IsDBNull(ordStatus)    ? null : r.GetString(ordStatus),
        };
    }
}
