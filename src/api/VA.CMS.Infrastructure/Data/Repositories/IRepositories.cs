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
    /// <summary>List all menus. Issue #46 — FR-NAV-01.</summary>
    Task<IReadOnlyList<NavigationMenu>> ListAllAsync();
    /// <summary>Create a menu. Returns new Id. Issue #46 — FR-NAV-01.</summary>
    Task<long> CreateAsync(string name, string handle);
    /// <summary>Rename a menu. Issue #46 — FR-NAV-01.</summary>
    Task UpdateAsync(long id, string name);
    /// <summary>Delete a menu and cascade-delete its items. Issue #46 — FR-NAV-01.</summary>
    Task DeleteAsync(long id);
}

public interface IAuditLogRepository
{
    /// <summary>
    /// Writes one audit row. Source IP, user agent and correlation id come from the
    /// scope's <see cref="IAuditContext"/> (#165); <paramref name="outcome"/> is
    /// <see cref="AuditOutcome.Success"/> unless the event records a denial or failure.
    /// </summary>
    Task WriteAsync(long? actorId, string entityType, long entityId, string action, string? diffJson = null,
        string outcome = AuditOutcome.Success);
    Task<IEnumerable<AuditLog>> ListAsync(long? actorId = null, string? entityType = null, string? action = null,
        DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 50);

    /// <summary>
    /// Paged audit log with total count, filtered by any combination of actor, action, entity type,
    /// date range, outcome and source IP. Returns newest first. Issue #57 — BRD FR-USERS-06.
    /// </summary>
    Task<AuditLogPage> ListPagedAsync(
        long? actorId = null, string? action = null, string? entityType = null,
        DateTime? fromDate = null, DateTime? toDate = null,
        int page = 1, int pageSize = 50,
        string? outcome = null, string? ipAddress = null);

    /// <summary>
    /// Same filters as ListPagedAsync but returns up to 1000 rows without paging for CSV export.
    /// Issue #57 — BRD FR-USERS-06.
    /// </summary>
    Task<IReadOnlyList<AuditLogRow>> ExportAsync(
        long? actorId = null, string? action = null, string? entityType = null,
        DateTime? fromDate = null, DateTime? toDate = null,
        string? outcome = null, string? ipAddress = null);
}

/// <summary>
/// Persistent refresh tokens (#163, BRD FR-SECURITY-02). Only the SHA-256 hash of the
/// opaque cookie value ever reaches the database; every method maps to one usp_RefreshToken_* call.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Starts a new token family at login. Returns the row id.</summary>
    Task<long> IssueAsync(long userId, byte[] tokenHash, DateTime expiresAt, DateTime absoluteExpiresAt,
        string? createdByIp, string? userAgent, string? groupsJson);

    /// <summary>
    /// Resolves a presented token: null when unknown, otherwise the row with its
    /// <see cref="RefreshTokenLookup.Status"/>. A replayed token revokes its whole family here.
    /// </summary>
    Task<RefreshTokenLookup?> ValidateAsync(byte[] tokenHash, int idleMinutes, int rotationGraceSeconds,
        string? sourceIp, string? userAgent);

    /// <summary>Revokes <paramref name="oldId"/> and issues its replacement in one transaction. Returns the new row id.</summary>
    Task<long> RotateAsync(long oldId, byte[] newTokenHash, DateTime expiresAt, string? createdByIp, string? userAgent);

    /// <summary>Revokes a single token (logout, disabled account).</summary>
    Task RevokeAsync(byte[] tokenHash, string reason);

    /// <summary>Revokes every live token of a user and bumps User.SessionVersion ("sign out everywhere").</summary>
    Task RevokeAllForUserAsync(long userId, long? actorId, string reason);
}
