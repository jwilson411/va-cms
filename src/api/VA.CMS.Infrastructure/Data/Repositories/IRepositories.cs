using VA.CMS.Infrastructure.Data.Pocos;
using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Repositories;

public interface IContentVersionRepository
{
    Task<ContentVersion?> GetByIdAsync(long id);
    Task<Page<ContentVersion>> ListAsync(long contentEntryId, int page, int pageSize);
    Task<long> CreateAsync(ContentVersion version);

    /// <summary>
    /// List versions with joined author display name (issue #32).
    /// Returns newest first, paginated.
    /// </summary>
    Task<IReadOnlyList<ContentVersionWithAuthor>> ListWithAuthorAsync(long contentEntryId, int page = 1, int pageSize = 25);

    /// <summary>Get a version with joined author display name (issue #32).</summary>
    Task<ContentVersionWithAuthor?> GetByIdWithAuthorAsync(long versionId);

    /// <summary>
    /// Restore a prior version by copying its FieldsJson into a new version row.
    /// History is never modified — restore always appends a new row.
    /// Returns the new version id (issue #32).
    /// </summary>
    Task<long> RestoreAsync(long contentEntryId, long targetVersionId, long actorId);

    /// <summary>
    /// Update the RenderedFieldsJson on an existing ContentVersion.
    /// Called on publish and version restore to cache rendered HTML.
    /// Issue #66: BRD FR-AUTH-02a/02b.
    /// </summary>
    Task UpdateRenderedFieldsAsync(long versionId, string renderedFieldsJson);
}

public interface IMediaAssetRepository
{
    Task<MediaAsset?> GetByIdAsync(long id);
    Task<Page<MediaAsset>> ListAsync(int page, int pageSize, string? mimeTypePrefix = null, string? searchTerm = null);
    Task<long> CreateAsync(MediaAsset asset);
    Task UpdateAsync(MediaAsset asset);
    /// <summary>
    /// Records the WebP storage path for an asset after image processing.
    /// Calls usp_MediaAsset_UpdateWebPPath. Issue #41 — FR-MEDIA-02.
    /// </summary>
    Task UpdateWebPPathAsync(long id, string webPStoragePath);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id);
    Task<User?> GetByExternalIdAsync(string externalId);
    Task<long> UpsertAsync(string externalId, string email, string displayName);
    Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId);
}

public interface INavigationMenuRepository
{
    Task<NavigationMenu?> GetByHandleAsync(string handle);
}

public interface IAuditLogRepository
{
    Task WriteAsync(long? actorId, string entityType, long entityId, string action, string? diffJson = null);
    Task<IEnumerable<AuditLog>> ListAsync(long? actorId = null, string? entityType = null, string? action = null,
        DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 50);

    /// <summary>
    /// Paged audit log with total count, filtered by any combination of actor, action, entity type,
    /// and date range. Returns newest first. Issue #57 — BRD FR-USERS-06.
    /// </summary>
    Task<AuditLogPage> ListPagedAsync(
        long? actorId = null, string? action = null, string? entityType = null,
        DateTime? fromDate = null, DateTime? toDate = null,
        int page = 1, int pageSize = 50);

    /// <summary>
    /// Same filters as ListPagedAsync but returns up to 1000 rows without paging for CSV export.
    /// Issue #57 — BRD FR-USERS-06.
    /// </summary>
    Task<IReadOnlyList<AuditLogRow>> ExportAsync(
        long? actorId = null, string? action = null, string? entityType = null,
        DateTime? fromDate = null, DateTime? toDate = null);
}
