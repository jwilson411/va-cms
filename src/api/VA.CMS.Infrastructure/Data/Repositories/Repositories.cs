using PetaPoco;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// ContentVersion repository. All access via EXEC usp_ContentVersion_* stored procedures.
/// </summary>
public class ContentVersionRepository : IContentVersionRepository
{
    private readonly CmsDatabase _db;

    public ContentVersionRepository(CmsDatabase db) => _db = db;

    public async Task<ContentVersion?> GetByIdAsync(long id)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentVersion_GetById @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var ordId = reader.GetOrdinal("Id");
        var ordEntryId = reader.GetOrdinal("ContentEntryId");
        var ordVersionNum = reader.GetOrdinal("VersionNumber");
        var ordFields = reader.GetOrdinal("FieldsJson");
        var ordStatus = reader.GetOrdinal("Status");
        var ordAuthorId = reader.GetOrdinal("AuthorId");
        var ordNote = reader.GetOrdinal("ChangeNote");
        var ordCreated = reader.GetOrdinal("CreatedAt");
        return new ContentVersion
        {
            Id = reader.GetInt64(ordId),
            ContentEntryId = reader.GetInt64(ordEntryId),
            VersionNumber = reader.GetInt32(ordVersionNum),
            FieldsJson = reader.GetString(ordFields),
            Status = reader.GetString(ordStatus),
            AuthorId = reader.GetInt64(ordAuthorId),
            ChangeNote = reader.IsDBNull(ordNote) ? null : reader.GetString(ordNote),
            CreatedAt = reader.GetDateTime(ordCreated),
        };
    }

    public async Task<Page<ContentVersion>> ListAsync(long contentEntryId, int page, int pageSize)
    {
        var items = await _db.FetchAsync<ContentVersion>(
            "EXEC usp_ContentVersion_List @0, @1, @2", contentEntryId, page, pageSize);

        return new Page<ContentVersion>
        {
            CurrentPage = page,
            ItemsPerPage = pageSize,
            Items = items,
            TotalItems = items.Count,
        };
    }

    public async Task<long> CreateAsync(ContentVersion version)
    {
        // Use ADO.NET directly for OUTPUT params — PetaPoco's @N scanner
        // conflicts with named OUTPUT parameters.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentVersion_Create @ContentEntryId, @FieldsJson, @RenderedFieldsJson, @Status, @AuthorId, @ChangeNote, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", version.ContentEntryId);
        cmd.Parameters.AddWithValue("@FieldsJson", version.FieldsJson);
        cmd.Parameters.AddWithValue("@RenderedFieldsJson", (object?)version.RenderedFieldsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", version.Status);
        cmd.Parameters.AddWithValue("@AuthorId", version.AuthorId);
        cmd.Parameters.AddWithValue("@ChangeNote", (object?)version.ChangeNote ?? DBNull.Value);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    // ── Issue #32: author-joined list / get / restore ─────────────────────────

    public async Task<IReadOnlyList<ContentVersionWithAuthor>> ListWithAuthorAsync(
        long contentEntryId, int page = 1, int pageSize = 25)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentVersion_List @ContentEntryId, @Page, @PageSize";
        cmd.Parameters.AddWithValue("@ContentEntryId", contentEntryId);
        cmd.Parameters.AddWithValue("@Page", page);
        cmd.Parameters.AddWithValue("@PageSize", pageSize);
        var results = new List<ContentVersionWithAuthor>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapVersionWithAuthor(reader));
        return results;
    }

    public async Task<ContentVersionWithAuthor?> GetByIdWithAuthorAsync(long versionId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentVersion_GetById @Id";
        cmd.Parameters.AddWithValue("@Id", versionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapVersionWithAuthor(reader);
    }

    public async Task<long> RestoreAsync(long contentEntryId, long targetVersionId, long actorId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentVersion_Restore @ContentEntryId, @TargetVersionId, @ActorId, NULL, @NewVersionId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", contentEntryId);
        cmd.Parameters.AddWithValue("@TargetVersionId", targetVersionId);
        cmd.Parameters.AddWithValue("@ActorId", actorId);
        var outParam = cmd.Parameters.Add("@NewVersionId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    private static ContentVersionWithAuthor MapVersionWithAuthor(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id             = r.GetInt64(r.GetOrdinal("Id")),
        ContentEntryId = r.GetInt64(r.GetOrdinal("ContentEntryId")),
        VersionNumber  = r.GetInt32(r.GetOrdinal("VersionNumber")),
        FieldsJson     = r.GetString(r.GetOrdinal("FieldsJson")),
        RenderedFieldsJson = r.IsDBNull(r.GetOrdinal("RenderedFieldsJson")) ? null : r.GetString(r.GetOrdinal("RenderedFieldsJson")),
        Status         = r.GetString(r.GetOrdinal("Status")),
        AuthorId       = r.GetInt64(r.GetOrdinal("AuthorId")),
        AuthorName     = r.GetString(r.GetOrdinal("AuthorName")),
        ChangeNote     = r.IsDBNull(r.GetOrdinal("ChangeNote")) ? null : r.GetString(r.GetOrdinal("ChangeNote")),
        CreatedAt      = r.GetDateTime(r.GetOrdinal("CreatedAt")),
    };
}

/// <summary>
/// MediaAsset repository. All access via EXEC usp_MediaAsset_* stored procedures.
/// </summary>
public class MediaAssetRepository : IMediaAssetRepository
{
    private readonly CmsDatabase _db;

    public MediaAssetRepository(CmsDatabase db) => _db = db;

    public async Task<MediaAsset?> GetByIdAsync(long id)
    {
        // All DB access via EXEC usp_* — no raw DML (NFR-DB-01)
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaAsset_GetById @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new MediaAsset
        {
            Id             = reader.GetInt64(reader.GetOrdinal("Id")),
            FileName       = reader.GetString(reader.GetOrdinal("FileName")),
            StoragePath    = reader.GetString(reader.GetOrdinal("StoragePath")),
            StorageBackend = reader.GetString(reader.GetOrdinal("StorageBackend")),
            MimeType       = reader.GetString(reader.GetOrdinal("MimeType")),
            FileSizeBytes  = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
            Width          = reader.IsDBNull(reader.GetOrdinal("Width"))  ? null : reader.GetInt32(reader.GetOrdinal("Width")),
            Height         = reader.IsDBNull(reader.GetOrdinal("Height")) ? null : reader.GetInt32(reader.GetOrdinal("Height")),
            UploadedById   = reader.GetInt64(reader.GetOrdinal("UploadedById")),
            IsVirusScanPassed = reader.IsDBNull(reader.GetOrdinal("IsVirusScanPassed")) ? null : reader.GetBoolean(reader.GetOrdinal("IsVirusScanPassed")),
            AltText        = reader.IsDBNull(reader.GetOrdinal("AltText"))        ? null : reader.GetString(reader.GetOrdinal("AltText")),
            Title          = reader.IsDBNull(reader.GetOrdinal("Title"))          ? null : reader.GetString(reader.GetOrdinal("Title")),
            Description    = reader.IsDBNull(reader.GetOrdinal("Description"))    ? null : reader.GetString(reader.GetOrdinal("Description")),
            Tags           = reader.IsDBNull(reader.GetOrdinal("Tags"))           ? null : reader.GetString(reader.GetOrdinal("Tags")),
            WebPStoragePath = reader.IsDBNull(reader.GetOrdinal("WebPStoragePath")) ? null : reader.GetString(reader.GetOrdinal("WebPStoragePath")),
            CreatedAt      = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt      = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
        };
    }

    public async Task<Page<MediaAsset>> ListAsync(int page, int pageSize,
        string? mimeTypePrefix = null, string? searchTerm = null)
    {
        var items = await _db.FetchAsync<MediaAsset>(
            "EXEC usp_MediaAsset_List @0, @1, @2, @3, NULL",
            (object?)mimeTypePrefix, (object?)searchTerm, page, pageSize);

        return new Page<MediaAsset>
        {
            CurrentPage = page,
            ItemsPerPage = pageSize,
            Items = items,
            TotalItems = items.Count,
        };
    }

    public async Task<long> CreateAsync(MediaAsset asset)
    {
        // Use ADO.NET directly for OUTPUT params — PetaPoco's @N scanner
        // conflicts with named OUTPUT parameters.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaAsset_Create @FileName, @StoragePath, @StorageBackend, @MimeType, @FileSizeBytes, @Width, @Height, @UploadedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@FileName", asset.FileName);
        cmd.Parameters.AddWithValue("@StoragePath", asset.StoragePath);
        cmd.Parameters.AddWithValue("@StorageBackend", asset.StorageBackend);
        cmd.Parameters.AddWithValue("@MimeType", asset.MimeType);
        cmd.Parameters.AddWithValue("@FileSizeBytes", asset.FileSizeBytes);
        cmd.Parameters.AddWithValue("@Width", (object?)asset.Width ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Height", (object?)asset.Height ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UploadedById", asset.UploadedById);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    public async Task UpdateAsync(MediaAsset asset)
    {
        await _db.ExecuteAsync(
            "EXEC usp_MediaAsset_UpdateMetadata @0, @1, @2, @3, @4",
            asset.Id, asset.AltText, asset.Title, asset.Description, asset.Tags);
    }

    /// <inheritdoc />
    public async Task UpdateWebPPathAsync(long id, string webPStoragePath)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaAsset_UpdateWebPPath @Id, @WebPStoragePath";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@WebPStoragePath", webPStoragePath);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// User repository. All access via EXEC usp_User_* stored procedures.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly CmsDatabase _db;

    public UserRepository(CmsDatabase db) => _db = db;

    public async Task<User?> GetByIdAsync(long id)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_User_GetById @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new User
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            ExternalId = reader.GetString(reader.GetOrdinal("ExternalId")),
            Email = reader.GetString(reader.GetOrdinal("Email")),
            DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastLoginAt")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
        };
    }

    public async Task<User?> GetByExternalIdAsync(string externalId)
    {
        // All DB access via EXEC usp_* — no raw DML (NFR-DB-01)
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_User_GetByExternalId @ExternalId";
        cmd.Parameters.AddWithValue("@ExternalId", externalId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new User
        {
            Id          = reader.GetInt64(reader.GetOrdinal("Id")),
            ExternalId  = reader.GetString(reader.GetOrdinal("ExternalId")),
            Email       = reader.GetString(reader.GetOrdinal("Email")),
            DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
            IsActive    = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastLoginAt")),
            CreatedAt   = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt   = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
        };
    }

    public async Task<long> UpsertAsync(string externalId, string email, string displayName)
    {
        // Use ADO.NET directly for OUTPUT params — PetaPoco's @N scanner
        // conflicts with named OUTPUT parameters like @UserId in DECLARE blocks.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_User_Upsert @ExternalId, @Email, @DisplayName, @UserId OUTPUT";
        cmd.Parameters.AddWithValue("@ExternalId", externalId);
        cmd.Parameters.AddWithValue("@Email", email);
        cmd.Parameters.AddWithValue("@DisplayName", displayName);
        var outParam = cmd.Parameters.Add("@UserId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    public async Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_User_GetRoles @UserId";
        cmd.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<UserRoleAssignment>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserRoleAssignment
            {
                RoleId           = reader.GetInt64(reader.GetOrdinal("RoleId")),
                RoleName         = reader.GetString(reader.GetOrdinal("RoleName")),
                SectionId        = reader.IsDBNull(reader.GetOrdinal("SectionId")) ? null : reader.GetInt64(reader.GetOrdinal("SectionId")),
                SectionSlugPrefix = reader.IsDBNull(reader.GetOrdinal("SectionSlugPrefix")) ? null : reader.GetString(reader.GetOrdinal("SectionSlugPrefix")),
            });
        }
        return results;
    }
}


/// <summary>
/// NavigationMenu repository. All access via EXEC usp_Navigation_* stored procedures.
/// </summary>
public class NavigationMenuRepository : INavigationMenuRepository
{
    private readonly CmsDatabase _db;

    public NavigationMenuRepository(CmsDatabase db) => _db = db;

    public async Task<NavigationMenu?> GetByHandleAsync(string handle)
    {
        // All DB access via EXEC usp_* — no raw DML (NFR-DB-01)
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_NavigationMenu_GetByHandle @Handle";
        cmd.Parameters.AddWithValue("@Handle", handle);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new NavigationMenu
        {
            Id     = reader.GetInt64(reader.GetOrdinal("Id")),
            Handle = reader.GetString(reader.GetOrdinal("Handle")),
            Name   = reader.GetString(reader.GetOrdinal("Name")),
        };
    }
}

/// <summary>
/// AuditLog repository. Write-only — no UPDATE or DELETE permitted by the app service account.
/// Reads go through EXEC usp_AuditLog_List.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly CmsDatabase _db;

    public AuditLogRepository(CmsDatabase db) => _db = db;

    public async Task WriteAsync(long? actorId, string entityType, long entityId,
        string action, string? diffJson = null)
    {
        await _db.ExecuteAsync(
            "EXEC usp_AuditLog_Write @0, @1, @2, @3, @4",
            actorId, entityType, entityId, action, diffJson);
    }

    public async Task<IEnumerable<AuditLog>> ListAsync(long? actorId = null,
        string? entityType = null, string? action = null,
        DateTime? fromDate = null, DateTime? toDate = null,
        int page = 1, int pageSize = 50)
    {
        return await _db.FetchAsync<AuditLog>(
            "EXEC usp_AuditLog_List @0, @1, @2, @3, @4, @5, @6",
            (object?)actorId, (object?)entityType, (object?)action,
            (object?)fromDate, (object?)toDate, page, pageSize);
    }
}
