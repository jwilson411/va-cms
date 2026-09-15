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
}

public interface IMediaAssetRepository
{
    Task<MediaAsset?> GetByIdAsync(long id);
    Task<Page<MediaAsset>> ListAsync(int page, int pageSize, string? mimeTypePrefix = null, string? searchTerm = null);
    Task<long> CreateAsync(MediaAsset asset);
    Task UpdateAsync(MediaAsset asset);
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
}
