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
        var results = await _db.FetchAsync<MediaAsset>(
            Sql.Builder.Append("SELECT * FROM [MediaAsset] WHERE [Id] = @0", id));
        return results.FirstOrDefault();
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
        return await _db.FirstOrDefaultAsync<User>(
            Sql.Builder
                .Append("SELECT * FROM [User] WHERE [ExternalId] = @0", externalId));
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
        return await _db.FirstOrDefaultAsync<NavigationMenu>(
            Sql.Builder
                .Append("SELECT * FROM [NavigationMenu] WHERE [Handle] = @0", handle));
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
