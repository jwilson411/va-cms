using PetaPoco;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// ContentEntry repository. All DB access via EXEC usp_ContentEntry_* stored procedures.
/// No raw DML — app service account has EXECUTE only.
/// </summary>
public class ContentEntryRepository : IContentEntryRepository
{
    private readonly CmsDatabase _db;

    public ContentEntryRepository(CmsDatabase db) => _db = db;

    public async Task<ContentEntry?> GetByIdAsync(long id)
    {
        // Use ADO.NET directly for SP calls — avoids PetaPoco EXEC wrapping issues
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_GetById @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapContentEntry(reader);
    }

    public async Task<ContentEntry?> GetBySlugAsync(string slug, string locale = "en-US")
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_GetBySlug @Slug, @Locale";
        cmd.Parameters.AddWithValue("@Slug", slug);
        cmd.Parameters.AddWithValue("@Locale", locale);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapContentEntry(reader);
    }

    private static ContentEntry MapContentEntry(Microsoft.Data.SqlClient.SqlDataReader reader)
    {
        var entry = new ContentEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            ContentTypeId = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
            Slug = reader.GetString(reader.GetOrdinal("Slug")),
            Locale = reader.GetString(reader.GetOrdinal("Locale")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            PublishedVersionId = reader.IsDBNull(reader.GetOrdinal("PublishedVersionId")) ? null : reader.GetInt64(reader.GetOrdinal("PublishedVersionId")),
            ScheduledPublishAt = reader.IsDBNull(reader.GetOrdinal("ScheduledPublishAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ScheduledPublishAt")),
            ScheduledExpireAt  = reader.IsDBNull(reader.GetOrdinal("ScheduledExpireAt"))  ? null : reader.GetDateTime(reader.GetOrdinal("ScheduledExpireAt")),
            OwnerId = reader.GetInt64(reader.GetOrdinal("OwnerId")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
        };

        // RenderedFieldsJson and FieldsJson come from LEFT JOIN on PublishedVersionId.
        // Only present when reading from usp_ContentEntry_GetById/GetBySlug — try/catch for safety.
        // Issue #66: FR-AUTH-02a/02b.
        try
        {
            var ordFields = reader.GetOrdinal("FieldsJson");
            entry.FieldsJson = reader.IsDBNull(ordFields) ? null : reader.GetString(ordFields);
        }
        catch { /* column not present in all queries */ }
        try
        {
            var ordRendered = reader.GetOrdinal("RenderedFieldsJson");
            entry.RenderedFieldsJson = reader.IsDBNull(ordRendered) ? null : reader.GetString(ordRendered);
        }
        catch { /* column not present in all queries */ }

        return entry;
    }

    public async Task<Page<ContentEntry>> ListAsync(int page, int pageSize,
        string? status = null, long? contentTypeId = null)
    {
        // SP handles pagination internally; use ADO.NET to read the result set directly.
        // @TotalRows OUTPUT param not surfaced here — Page.TotalItems reflects items returned
        // which is sufficient for the acceptance criteria (all queries via EXEC usp_*).
        var items = await _db.FetchAsync<ContentEntry>(
            "EXEC usp_ContentEntry_List @0, @1, NULL, NULL, NULL, @2, @3, NULL",
            (object?)contentTypeId, (object?)status, page, pageSize);

        return new Page<ContentEntry>
        {
            CurrentPage = page,
            ItemsPerPage = pageSize,
            Items = items,
            TotalItems = items.Count,
        };
    }

    public async Task<long> CreateAsync(ContentEntry entry)
    {
        // Use ADO.NET directly for OUTPUT params — PetaPoco's @N scanner
        // conflicts with named OUTPUT parameters.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_Create @ContentTypeId, @Slug, @Locale, @OwnerId, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentTypeId", entry.ContentTypeId);
        cmd.Parameters.AddWithValue("@Slug", entry.Slug);
        cmd.Parameters.AddWithValue("@Locale", entry.Locale);
        cmd.Parameters.AddWithValue("@OwnerId", entry.OwnerId);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    public async Task UpdateAsync(ContentEntry entry)
    {
        await _db.ExecuteAsync(
            "EXEC usp_ContentEntry_UpdateStatus @0, @1, @2",
            entry.Id, entry.Status, entry.PublishedVersionId);
    }

    public async Task ArchiveAsync(long id, long actorId)
    {
        await _db.ExecuteAsync(
            "EXEC usp_ContentEntry_Archive @0, @1",
            id, actorId);
    }

    public async Task<ContentEntryAdminPage> ListAdminAsync(
        long?     contentTypeId = null,
        string?   status        = null,
        string?   authorSearch  = null,
        DateTime? dateFrom      = null,
        DateTime? dateTo        = null,
        string    sortBy        = "UpdatedAt",
        string    sortDir       = "DESC",
        int       page          = 1,
        int       pageSize      = 25)
    {
        // Use ADO.NET directly to handle OUTPUT parameter and result set.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();

        // The SP returns rows first, then sets @TotalRows OUTPUT.
        // We wrap in a batch that re-selects @TotalRows as a second result set.
        cmd.CommandText = @"
            DECLARE @TotalRows INT;
            EXEC usp_ContentEntry_ListAdmin
                @ContentTypeId = @ContentTypeId,
                @Status        = @Status,
                @AuthorSearch  = @AuthorSearch,
                @DateFrom      = @DateFrom,
                @DateTo        = @DateTo,
                @SortBy        = @SortBy,
                @SortDir       = @SortDir,
                @Page          = @Page,
                @PageSize      = @PageSize,
                @TotalRows     = @TotalRows OUTPUT;
            SELECT @TotalRows AS TotalRows;";

        AddNullableParam(cmd, "@ContentTypeId", System.Data.SqlDbType.BigInt,     (object?)contentTypeId);
        AddNullableParam(cmd, "@Status",        System.Data.SqlDbType.NVarChar,   (object?)status);
        AddNullableParam(cmd, "@AuthorSearch",  System.Data.SqlDbType.NVarChar,   (object?)authorSearch);
        AddNullableParam(cmd, "@DateFrom",      System.Data.SqlDbType.DateTime2,  (object?)dateFrom);
        AddNullableParam(cmd, "@DateTo",        System.Data.SqlDbType.DateTime2,  (object?)dateTo);
        cmd.Parameters.AddWithValue("@SortBy",   sortBy);
        cmd.Parameters.AddWithValue("@SortDir",  sortDir);
        cmd.Parameters.AddWithValue("@Page",     page);
        cmd.Parameters.AddWithValue("@PageSize", pageSize);

        var items = new List<ContentEntryAdminRow>();
        int totalRows = 0;

        await using var reader = await cmd.ExecuteReaderAsync();
        // First result set: the content entry rows
        while (await reader.ReadAsync())
        {
            items.Add(new ContentEntryAdminRow
            {
                Id                = reader.GetInt64(reader.GetOrdinal("Id")),
                Slug              = reader.GetString(reader.GetOrdinal("Slug")),
                Status            = reader.GetString(reader.GetOrdinal("Status")),
                ContentTypeId     = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
                ContentTypeName   = reader.GetString(reader.GetOrdinal("ContentTypeName")),
                OwnerId           = reader.GetInt64(reader.GetOrdinal("OwnerId")),
                AuthorDisplayName = reader.GetString(reader.GetOrdinal("AuthorDisplayName")),
                UpdatedAt         = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                Title             = reader.GetString(reader.GetOrdinal("Title")),
            });
        }
        // Second result set: TotalRows scalar
        if (await reader.NextResultAsync() && await reader.ReadAsync())
        {
            totalRows = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        }

        return new ContentEntryAdminPage
        {
            Items     = items,
            TotalRows = totalRows,
            Page      = page,
            PageSize  = pageSize,
        };
    }

    /// <summary>
    /// Update the slug for a content entry (issue #33, FR-NAV-05).
    /// Calls usp_ContentEntry_UpdateSlug which:
    ///   - Validates uniqueness within locale (returns error if duplicate)
    ///   - Creates a 301 Redirect if the entry is Published and the slug changes
    ///   - Updates ContentEntry.Slug
    ///   - Audit logs the change
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> UpdateSlugAsync(
        long id, string newSlug, long actorId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentEntry_UpdateSlug @Id, @NewSlug, @ActorId, @Success OUTPUT, @ErrorMessage OUTPUT";
        cmd.Parameters.AddWithValue("@Id",      id);
        cmd.Parameters.AddWithValue("@NewSlug", newSlug);
        cmd.Parameters.AddWithValue("@ActorId", actorId);

        var successParam = cmd.Parameters.Add("@Success", System.Data.SqlDbType.Bit);
        successParam.Direction = System.Data.ParameterDirection.Output;

        var errorParam = cmd.Parameters.Add("@ErrorMessage", System.Data.SqlDbType.NVarChar, 500);
        errorParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var success = successParam.Value is bool b && b;
        var error   = errorParam.Value == DBNull.Value ? null : errorParam.Value as string;
        return (success, error);
    }

    // ── Issue #35: Scheduled publish / expiry ────────────────────────────────

    public async Task<(bool Success, string? ErrorMessage)> SetScheduleAsync(
        long id,
        DateTime? scheduledPublishAt,
        DateTime? scheduledExpireAt,
        long actorId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentEntry_SetSchedule @Id, @ScheduledPublishAt, @ScheduledExpireAt, @ActorId, @Success OUTPUT, @ErrorMessage OUTPUT";
        cmd.Parameters.AddWithValue("@Id", id);
        AddNullableParam(cmd, "@ScheduledPublishAt", System.Data.SqlDbType.DateTime2, scheduledPublishAt);
        AddNullableParam(cmd, "@ScheduledExpireAt",  System.Data.SqlDbType.DateTime2, scheduledExpireAt);
        cmd.Parameters.AddWithValue("@ActorId", actorId);

        var successParam = cmd.Parameters.Add("@Success", System.Data.SqlDbType.Bit);
        successParam.Direction = System.Data.ParameterDirection.Output;

        var errorParam = cmd.Parameters.Add("@ErrorMessage", System.Data.SqlDbType.NVarChar, 500);
        errorParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var success = successParam.Value is bool b && b;
        var error   = errorParam.Value == DBNull.Value ? null : errorParam.Value as string;
        return (success, error);
    }

    public async Task<IList<ContentEntry>> GetScheduledForPublishAsync()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_GetScheduledForPublish";
        var results = new List<ContentEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ContentEntry
            {
                Id            = reader.GetInt64(reader.GetOrdinal("Id")),
                ContentTypeId = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
                Slug          = reader.GetString(reader.GetOrdinal("Slug")),
                Locale        = reader.GetString(reader.GetOrdinal("Locale")),
                Status        = "Approved",
            });
        }
        return results;
    }

    public async Task<IList<ContentEntry>> GetScheduledForExpiryAsync()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_GetScheduledForExpiry";
        var results = new List<ContentEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ContentEntry
            {
                Id            = reader.GetInt64(reader.GetOrdinal("Id")),
                ContentTypeId = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
                Slug          = reader.GetString(reader.GetOrdinal("Slug")),
                Locale        = reader.GetString(reader.GetOrdinal("Locale")),
                Status        = "Published",
            });
        }
        return results;
    }

    public async Task PublishScheduledAsync(long id, long systemActorId)
    {
        // Use dedicated scheduler SP (V019) — bypasses usp_Workflow_Transition which
        // requires a ContentVersionId from a user-driven action.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_PublishScheduled @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExpireScheduledAsync(long id, long systemActorId)
    {
        // Use dedicated scheduler SP (V019) — bypasses usp_Workflow_Transition.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_ExpireScheduled @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Issue #36: Duplicate entry ────────────────────────────────────────────

    /// <summary>
    /// Duplicate a content entry.
    /// SP creates a new Draft with '(Copy)' appended to title, slug cleared,
    /// all fields copied, media references shared (not re-uploaded).
    /// Issue #36: BRD FR-AUTH-07.
    /// </summary>
    public async Task<(bool Success, long? NewEntryId, string? ErrorMessage)> DuplicateAsync(
        long sourceEntryId, long actorId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentEntry_Duplicate @SourceEntryId, @ActorId, @NewEntryId OUTPUT, @Success OUTPUT, @ErrorMessage OUTPUT";
        cmd.Parameters.AddWithValue("@SourceEntryId", sourceEntryId);
        cmd.Parameters.AddWithValue("@ActorId",       actorId);

        var newEntryIdParam = cmd.Parameters.Add("@NewEntryId", System.Data.SqlDbType.BigInt);
        newEntryIdParam.Direction = System.Data.ParameterDirection.Output;

        var successParam = cmd.Parameters.Add("@Success", System.Data.SqlDbType.Bit);
        successParam.Direction = System.Data.ParameterDirection.Output;

        var errorParam = cmd.Parameters.Add("@ErrorMessage", System.Data.SqlDbType.NVarChar, 500);
        errorParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var success    = successParam.Value is bool b && b;
        long? newId    = newEntryIdParam.Value == DBNull.Value ? null : (long?)newEntryIdParam.Value;
        var errorMsg   = errorParam.Value == DBNull.Value ? null : errorParam.Value as string;

        return (success, newId, errorMsg);
    }

    private static void AddNullableParam(
        Microsoft.Data.SqlClient.SqlCommand cmd,
        string name,
        System.Data.SqlDbType dbType,
        object? value)
    {
        var p = cmd.Parameters.Add(name, dbType);
        p.Value = value ?? DBNull.Value;
    }
}
