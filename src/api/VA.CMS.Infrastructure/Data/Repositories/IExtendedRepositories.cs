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

// ── Extended Media ────────────────────────────────────────────────────────────

public interface IMediaExtendedRepository
{
    Task SetVirusScanResultAsync(long assetId, bool passed);
    Task<IEnumerable<MediaUsage>> GetUsageAsync(long mediaAssetId);
    Task<int> SafeDeleteAsync(long assetId);   // 0 = deleted, 1 = blocked
    Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName);
    Task DeleteUsageForEntryAsync(long contentEntryId);
}
