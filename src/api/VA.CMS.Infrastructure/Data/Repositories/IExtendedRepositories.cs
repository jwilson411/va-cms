using VA.CMS.Infrastructure.Data.Pocos;
using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Repositories;

// ── Workflow ──────────────────────────────────────────────────────────────────

public interface IWorkflowRepository
{
    /// <summary>
    /// Attempt a workflow transition. Returns (true, null) on success,
    /// (false, errorMessage) on validation failure.
    /// </summary>
    Task<(bool Success, string? Error)> TransitionAsync(
        long contentEntryId,
        long contentVersionId,
        string fromStatus,
        string toStatus,
        long actorId,
        string? comment = null);
}

// ── Navigation ────────────────────────────────────────────────────────────────

public interface INavigationRepository
{
    Task<IEnumerable<NavigationItem>> GetMenuTreeAsync(string handle);
    Task<long> UpsertItemAsync(NavigationItem item);
    Task<Redirect?> GetRedirectByPathAsync(string fromPath);
    Task<long> CreateRedirectAsync(Redirect redirect);
}

// ── Search ────────────────────────────────────────────────────────────────────

public interface ISearchRepository
{
    Task<Page<SearchResult>> FullTextSearchAsync(
        string query,
        long? contentTypeId = null,
        int page = 1,
        int pageSize = 25);
    Task LogQueryAsync(string query, int resultCount, long? userId = null);
}

// ── Taxonomy ──────────────────────────────────────────────────────────────────

public interface ITaxonomyRepository
{
    Task<IEnumerable<TaxonomyTerm>> GetTermTreeAsync(string taxonomyHandle);
    Task<IEnumerable<ContentEntry>> GetEntriesForTermAsync(
        long termId, int page = 1, int pageSize = 25);
}

// ── Webhooks ──────────────────────────────────────────────────────────────────

public interface IWebhookRepository
{
    Task<IEnumerable<Webhook>> GetActiveForEventAsync(string eventName);
    Task<long> CreateDeliveryAsync(WebhookDelivery delivery);
}

// ── Extended User ─────────────────────────────────────────────────────────────

public interface IUserRoleRepository
{
    Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId);
    Task AssignRoleAsync(long userId, long roleId, long grantedById, long? sectionId = null);
    Task RevokeRoleAsync(long userId, long roleId, long? sectionId = null);
    Task DeactivateAsync(long userId, long actorId);
    Task<IEnumerable<User>> ListAsync(
        string? searchTerm = null,
        bool isActive = true,
        int page = 1,
        int pageSize = 50);
}

// ── Alt Text Guard ────────────────────────────────────────────────────────────

/// <summary>
/// Checks whether all image assets referenced by a content entry have alt text set.
/// Issue #43 — FR-MEDIA-05: alt text enforcement before publish.
/// </summary>
public interface IMediaAltTextGuardRepository
{
    /// <summary>
    /// Returns the list of image asset IDs (and filenames) that are referenced by
    /// the given content entry and are missing alt text.
    /// An empty list means all image references are satisfied and publish is allowed.
    /// </summary>
    Task<IReadOnlyList<MissingAltTextAsset>> GetMissingAltTextAsync(long contentEntryId);
}

/// <summary>
/// Lightweight DTO for an asset that is blocking publish due to missing alt text.
/// </summary>
public class MissingAltTextAsset
{
    public long   Id       { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
}

// ── Extended Media ────────────────────────────────────────────────────────────

public interface IMediaExtendedRepository
{
    Task SetVirusScanResultAsync(long assetId, bool passed);
    /// <summary>
    /// Returns usage rows joined with ContentEntry columns (Slug, Status, ContentTypeId).
    /// Issue #42 — detail panel usage list.
    /// </summary>
    Task<IEnumerable<MediaUsageDetail>> GetUsageAsync(long mediaAssetId);
    Task<int> SafeDeleteAsync(long assetId);   // 0 = deleted, 1 = blocked
    Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName);
    Task DeleteUsageForEntryAsync(long contentEntryId);
}
