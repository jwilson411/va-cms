using Microsoft.Data.SqlClient;
using PetaPoco;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

// ── Workflow ──────────────────────────────────────────────────────────────────

/// <summary>
/// Workflow repository. All DB access via EXEC usp_Workflow_* stored procedures.
/// </summary>
public class WorkflowRepository : IWorkflowRepository
{
    private readonly CmsDatabase _db;

    public WorkflowRepository(CmsDatabase db) => _db = db;

    public async Task<(bool Success, string? Error)> TransitionAsync(
        long contentEntryId,
        long contentVersionId,
        string fromStatus,
        string toStatus,
        long actorId,
        string? comment = null)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_Workflow_Transition " +
            "@ContentEntryId, @ContentVersionId, @FromStatus, @ToStatus, " +
            "@ActorId, @Comment, @Success OUTPUT, @ErrorMessage OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", contentEntryId);
        cmd.Parameters.AddWithValue("@ContentVersionId", contentVersionId);
        cmd.Parameters.AddWithValue("@FromStatus", fromStatus);
        cmd.Parameters.AddWithValue("@ToStatus", toStatus);
        cmd.Parameters.AddWithValue("@ActorId", actorId);
        cmd.Parameters.AddWithValue("@Comment", (object?)comment ?? DBNull.Value);

        var successParam = cmd.Parameters.Add("@Success", System.Data.SqlDbType.Bit);
        successParam.Direction = System.Data.ParameterDirection.Output;

        var errorParam = cmd.Parameters.Add("@ErrorMessage", System.Data.SqlDbType.NVarChar, 500);
        errorParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var success = successParam.Value != DBNull.Value && (bool)successParam.Value;
        var error = errorParam.Value == DBNull.Value ? null : (string?)errorParam.Value;
        return (success, error);
    }
}

// ── Navigation ────────────────────────────────────────────────────────────────

/// <summary>
/// Navigation repository. All DB access via EXEC usp_Navigation_* / usp_Redirect_* stored procedures.
/// </summary>
public class NavigationRepository : INavigationRepository
{
    private readonly CmsDatabase _db;

    public NavigationRepository(CmsDatabase db) => _db = db;

    public async Task<IEnumerable<NavigationItem>> GetMenuTreeAsync(string handle)
    {
        // Use ADO.NET reader directly — the SP returns a Depth computed column
        // that is not in the NavigationItem table; PetaPoco would try to SELECT
        // the table definition. Map manually to avoid that.
        var results = new List<NavigationItem>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Navigation_GetMenuTree @Handle";
        cmd.Parameters.AddWithValue("@Handle", handle);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new NavigationItem
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                ParentItemId = reader.IsDBNull(reader.GetOrdinal("ParentItemId"))
                    ? null : reader.GetInt64(reader.GetOrdinal("ParentItemId")),
                Label = reader.GetString(reader.GetOrdinal("Label")),
                Url = reader.IsDBNull(reader.GetOrdinal("Url"))
                    ? null : reader.GetString(reader.GetOrdinal("Url")),
                ContentEntryId = reader.IsDBNull(reader.GetOrdinal("ContentEntryId"))
                    ? null : reader.GetInt64(reader.GetOrdinal("ContentEntryId")),
                Target = reader.GetString(reader.GetOrdinal("Target")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                IsVisible = reader.GetBoolean(reader.GetOrdinal("IsVisible")),
                Depth = reader.GetInt32(reader.GetOrdinal("Depth")),
            });
        }
        return results;
    }

    public async Task<long> UpsertItemAsync(NavigationItem item)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_Navigation_UpsertItem " +
            "@Id, @MenuId, @ParentItemId, @Label, @Url, @ContentEntryId, " +
            "@Target, @SortOrder, @IsVisible, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@Id", item.Id == 0 ? (object)DBNull.Value : item.Id);
        cmd.Parameters.AddWithValue("@MenuId", item.MenuId);
        cmd.Parameters.AddWithValue("@ParentItemId", (object?)item.ParentItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Label", item.Label);
        cmd.Parameters.AddWithValue("@Url", (object?)item.Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ContentEntryId", (object?)item.ContentEntryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Target", item.Target);
        cmd.Parameters.AddWithValue("@SortOrder", item.SortOrder);
        cmd.Parameters.AddWithValue("@IsVisible", item.IsVisible);

        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    public async Task<Redirect?> GetRedirectByPathAsync(string fromPath)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Redirect_GetByPath @FromPath";
        cmd.Parameters.AddWithValue("@FromPath", fromPath);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Redirect
        {
            FromPath = fromPath,
            ToPath = reader.GetString(reader.GetOrdinal("ToPath")),
            StatusCode = reader.GetInt32(reader.GetOrdinal("StatusCode")),
        };
    }

    public async Task<long> CreateRedirectAsync(Redirect redirect)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_Redirect_Create @FromPath, @ToPath, @StatusCode, @CreatedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@FromPath", redirect.FromPath);
        cmd.Parameters.AddWithValue("@ToPath", redirect.ToPath);
        cmd.Parameters.AddWithValue("@StatusCode", redirect.StatusCode);
        cmd.Parameters.AddWithValue("@CreatedById", redirect.CreatedById);

        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }
}

// ── Search ────────────────────────────────────────────────────────────────────

/// <summary>
/// Search repository. All DB access via EXEC usp_Search_* stored procedures.
/// </summary>
public class SearchRepository : ISearchRepository
{
    private readonly CmsDatabase _db;

    public SearchRepository(CmsDatabase db) => _db = db;

    public async Task<Page<SearchResult>> FullTextSearchAsync(
        string query,
        long? contentTypeId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        long? tagTermId = null,
        int page = 1,
        int pageSize = 25)
    {
        // SP uses OUTPUT param for TotalRows; use ADO.NET to capture it.
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_Search_FullText @Query, @ContentTypeId, @FromDate, @ToDate, @TagTermId, @Page, @PageSize, @TotalRows OUTPUT";
        cmd.Parameters.AddWithValue("@Query", query);
        cmd.Parameters.AddWithValue("@ContentTypeId", (object?)contentTypeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FromDate", (object?)fromDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ToDate", (object?)toDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TagTermId", (object?)tagTermId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Page", page);
        cmd.Parameters.AddWithValue("@PageSize", pageSize);

        var totalRowsParam = cmd.Parameters.Add("@TotalRows", System.Data.SqlDbType.Int);
        totalRowsParam.Direction = System.Data.ParameterDirection.Output;

        var results = new List<SearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // V024: SP now returns Title, ContentTypeName, Excerpt (summary|plain), PublishedAt.
            var ordTitle           = reader.GetOrdinal("Title");
            var ordContentTypeName = reader.GetOrdinal("ContentTypeName");
            var ordExcerpt         = reader.GetOrdinal("Excerpt");
            var ordPublishedAt     = reader.GetOrdinal("PublishedAt");

            results.Add(new SearchResult
            {
                Id              = reader.GetInt64(reader.GetOrdinal("Id")),
                Title           = reader.IsDBNull(ordTitle)           ? null : reader.GetString(ordTitle),
                Slug            = reader.GetString(reader.GetOrdinal("Slug")),
                ContentTypeId   = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
                ContentTypeName = reader.IsDBNull(ordContentTypeName) ? string.Empty : reader.GetString(ordContentTypeName),
                Locale          = reader.GetString(reader.GetOrdinal("Locale")),
                Excerpt         = reader.IsDBNull(ordExcerpt)         ? null : reader.GetString(ordExcerpt),
                PublishedAt     = reader.GetDateTime(ordPublishedAt),
                Rank            = reader.GetInt32(reader.GetOrdinal("RANK")),
            });
        }
        await reader.CloseAsync();

        var totalRows = totalRowsParam.Value == DBNull.Value ? 0 : (int)totalRowsParam.Value;

        return new Page<SearchResult>
        {
            CurrentPage = page,
            ItemsPerPage = pageSize,
            Items = results,
            TotalItems = totalRows,
        };
    }

    public async Task LogQueryAsync(string query, int resultCount, long? userId = null)
    {
        await _db.ExecuteAsync(
            "EXEC usp_Search_LogQuery @0, @1, @2",
            query, resultCount, (object?)userId);
    }
}

// ── Taxonomy ──────────────────────────────────────────────────────────────────

/// <summary>
/// Taxonomy repository. All DB access via EXEC usp_Taxonomy_* stored procedures.
/// </summary>
public class TaxonomyRepository : ITaxonomyRepository
{
    private readonly CmsDatabase _db;

    public TaxonomyRepository(CmsDatabase db) => _db = db;

    public async Task<IEnumerable<TaxonomyTerm>> GetTermTreeAsync(string taxonomyHandle)
    {
        // Use ADO.NET — SP returns computed Depth column; TaxonomyTerm table
        // has no CreatedAt/UpdatedAt. Map manually from SP result.
        var results = new List<TaxonomyTerm>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Taxonomy_GetTermTree @TaxonomyHandle";
        cmd.Parameters.AddWithValue("@TaxonomyHandle", taxonomyHandle);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TaxonomyTerm
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                ParentTermId = reader.IsDBNull(reader.GetOrdinal("ParentTermId"))
                    ? null : reader.GetInt64(reader.GetOrdinal("ParentTermId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Slug = reader.GetString(reader.GetOrdinal("Slug")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                Depth = reader.GetInt32(reader.GetOrdinal("Depth")),
            });
        }
        return results;
    }

    public async Task<IEnumerable<ContentEntry>> GetEntriesForTermAsync(
        long termId, int page = 1, int pageSize = 25)
    {
        // Use ADO.NET — PetaPoco wraps SP calls with SELECT, bypassing the SP's WHERE clause.
        var results = new List<ContentEntry>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Taxonomy_GetEntriesForTerm @TermId, @Page, @PageSize";
        cmd.Parameters.AddWithValue("@TermId", termId);
        cmd.Parameters.AddWithValue("@Page", page);
        cmd.Parameters.AddWithValue("@PageSize", pageSize);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ContentEntry
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                Slug = reader.GetString(reader.GetOrdinal("Slug")),
                ContentTypeId = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
                Locale = reader.GetString(reader.GetOrdinal("Locale")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            });
        }
        return results;
    }
}

// ── Webhooks ──────────────────────────────────────────────────────────────────

/// <summary>
/// Webhook repository. All DB access via EXEC usp_Webhook_* / usp_WebhookDelivery_* stored procedures.
/// </summary>
public class WebhookRepository : IWebhookRepository
{
    private readonly CmsDatabase _db;

    public WebhookRepository(CmsDatabase db) => _db = db;

    public async Task<IEnumerable<Webhook>> GetActiveForEventAsync(string eventName)
    {
        // Use ADO.NET — SP returns only Id, Url, Secret columns.
        // PetaPoco with [TableName] would try to map the full table.
        var results = new List<Webhook>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Webhook_GetActiveForEvent @EventName";
        cmd.Parameters.AddWithValue("@EventName", eventName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new Webhook
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                Url = reader.GetString(reader.GetOrdinal("Url")),
                Secret = reader.IsDBNull(reader.GetOrdinal("Secret"))
                    ? null : reader.GetString(reader.GetOrdinal("Secret")),
                IsActive = true,
            });
        }
        return results;
    }

    public async Task<long> CreateDeliveryAsync(WebhookDelivery delivery)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_WebhookDelivery_Create " +
            "@WebhookId, @EventName, @PayloadJson, @ResponseStatusCode, " +
            "@AttemptNumber, @ErrorMessage, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@WebhookId", delivery.WebhookId);
        cmd.Parameters.AddWithValue("@EventName", delivery.EventName);
        cmd.Parameters.AddWithValue("@PayloadJson", delivery.PayloadJson);
        cmd.Parameters.AddWithValue("@ResponseStatusCode",
            (object?)delivery.ResponseStatusCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AttemptNumber", delivery.AttemptNumber);
        cmd.Parameters.AddWithValue("@ErrorMessage",
            (object?)delivery.ErrorMessage ?? DBNull.Value);

        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }
}

// ── Extended User ─────────────────────────────────────────────────────────────

/// <summary>
/// User role and lifecycle repository. All DB access via EXEC usp_User_* stored procedures.
/// </summary>
public class UserRoleRepository : IUserRoleRepository
{
    private readonly CmsDatabase _db;

    public UserRoleRepository(CmsDatabase db) => _db = db;

    public async Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId)
    {
        // Use ADO.NET — PetaPoco uses the class name as table name for undecorated POCOs,
        // causing it to try SELECT from 'UserRoleAssignment' table which doesn't exist.
        var results = new List<UserRoleAssignment>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_User_GetRoles @UserId";
        cmd.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new UserRoleAssignment
            {
                RoleId = reader.GetInt64(reader.GetOrdinal("RoleId")),
                RoleName = reader.GetString(reader.GetOrdinal("RoleName")),
                SectionId = reader.IsDBNull(reader.GetOrdinal("SectionId"))
                    ? null : reader.GetInt64(reader.GetOrdinal("SectionId")),
                SectionSlugPrefix = reader.IsDBNull(reader.GetOrdinal("SectionSlugPrefix"))
                    ? null : reader.GetString(reader.GetOrdinal("SectionSlugPrefix")),
            });
        }
        return results;
    }

    public async Task AssignRoleAsync(long userId, long roleId, long grantedById, long? sectionId = null)
    {
        await _db.ExecuteAsync(
            "EXEC usp_User_AssignRole @0, @1, @2, @3",
            userId, roleId, (object?)sectionId, grantedById);
    }

    public async Task RevokeRoleAsync(long userId, long roleId, long? sectionId = null)
    {
        await _db.ExecuteAsync(
            "EXEC usp_User_RevokeRole @0, @1, @2",
            userId, roleId, (object?)sectionId);
    }

    public async Task DeactivateAsync(long userId, long actorId)
    {
        await _db.ExecuteAsync(
            "EXEC usp_User_Deactivate @0, @1",
            userId, actorId);
    }

    public async Task<IEnumerable<User>> ListAsync(
        string? searchTerm = null,
        bool isActive = true,
        int page = 1,
        int pageSize = 50)
    {
        return await _db.FetchAsync<User>(
            "EXEC usp_User_List @0, @1, @2, @3",
            (object?)searchTerm, isActive, page, pageSize);
    }
}

// ── Extended Media ────────────────────────────────────────────────────────────

/// <summary>
/// Extended media repository for virus scan, usage tracking, and safe-delete.
/// All DB access via EXEC usp_MediaAsset_* / usp_MediaUsage_* stored procedures.
/// </summary>
public class MediaExtendedRepository : IMediaExtendedRepository
{
    private readonly CmsDatabase _db;

    public MediaExtendedRepository(CmsDatabase db) => _db = db;

    public async Task SetVirusScanResultAsync(long assetId, bool passed)
    {
        await _db.ExecuteAsync(
            "EXEC usp_MediaAsset_SetVirusScanResult @0, @1",
            assetId, passed);
    }

    public async Task<IEnumerable<MediaUsageDetail>> GetUsageAsync(long mediaAssetId)
    {
        // Use ADO.NET — the SP joins ContentEntry and returns Slug, Status, ContentTypeId
        // which are not columns on the MediaUsage table; PetaPoco's table-name mapping
        // would fail. Map manually.
        var results = new List<MediaUsageDetail>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaAsset_GetUsage @MediaAssetId";
        cmd.Parameters.AddWithValue("@MediaAssetId", mediaAssetId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new MediaUsageDetail
            {
                ContentEntryId = reader.GetInt64(reader.GetOrdinal("ContentEntryId")),
                FieldName      = reader.GetString(reader.GetOrdinal("FieldName")),
                Slug           = reader.GetString(reader.GetOrdinal("Slug")),
                Status         = reader.GetString(reader.GetOrdinal("Status")),
                ContentTypeId  = reader.GetInt64(reader.GetOrdinal("ContentTypeId")),
            });
        }
        return results;
    }

    public async Task<int> SafeDeleteAsync(long assetId)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaAsset_SafeDelete @Id, @Result OUTPUT";
        cmd.Parameters.AddWithValue("@Id", assetId);
        var outParam = cmd.Parameters.Add("@Result", System.Data.SqlDbType.Int);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (int)outParam.Value;
    }

    public async Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName)
    {
        await _db.ExecuteAsync(
            "EXEC usp_MediaUsage_Upsert @0, @1, @2",
            mediaAssetId, contentEntryId, fieldName);
    }

    public async Task DeleteUsageForEntryAsync(long contentEntryId)
    {
        await _db.ExecuteAsync(
            "EXEC usp_MediaUsage_DeleteForEntry @0",
            contentEntryId);
    }
}
