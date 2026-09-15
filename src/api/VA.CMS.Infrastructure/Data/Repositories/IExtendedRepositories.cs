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
    /// <summary>
    /// Full-text search over published content.
    /// All filter parameters are optional and AND-combined when provided.
    /// </summary>
    /// <param name="query">FTS query string (required).</param>
    /// <param name="contentTypeId">Filter by content type ID.</param>
    /// <param name="fromDate">Include only entries published on or after this UTC date.</param>
    /// <param name="toDate">Include only entries published on or before this UTC date.</param>
    /// <param name="tagTermId">Filter by taxonomy term ID (tag).</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Results per page (1–100).</param>
    Task<Page<SearchResult>> FullTextSearchAsync(
        string query,
        long? contentTypeId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        long? tagTermId = null,
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
    /// <summary>Get all active webhook targets subscribed to the given event (for fanout delivery).</summary>
    Task<IEnumerable<Webhook>> GetActiveForEventAsync(string eventName);

    /// <summary>Log a delivery attempt. Returns the new delivery row Id.</summary>
    Task<long> CreateDeliveryAsync(WebhookDelivery delivery);

    // ── Issue #54: registration CRUD ──────────────────────────────────────────

    /// <summary>Register a new webhook. Returns the new row Id.</summary>
    Task<long> CreateAsync(string name, string url, string secret, string eventsJson, long createdById);

    /// <summary>List all webhooks (active and inactive). Secret is not returned in list rows.</summary>
    Task<IReadOnlyList<Webhook>> ListAllAsync();

    /// <summary>Get a single webhook including its secret (for admin / signing).</summary>
    Task<Webhook?> GetByIdAsync(long id);

    /// <summary>Soft-delete a webhook (sets IsActive = 0).</summary>
    Task DeleteAsync(long id);
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

    /// <summary>
    /// Get a single user with embedded role assignments (issue #56).
    /// Returns null when not found.
    /// </summary>
    Task<UserDetail?> GetDetailAsync(long userId);
}

// ── Role / Section lookup ─────────────────────────────────────────────────────

public interface IRoleRepository
{
    Task<IEnumerable<RoleRow>> ListAllAsync();
    Task<IEnumerable<ContentSectionRow>> ListSectionsAsync();
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

    /// <summary>
    /// Returns enriched usage rows: includes EntryTitle (from FieldsJson), ContentTypeName.
    /// Issue #44 — 409 conflict body and admin detail links.
    /// </summary>
    Task<IEnumerable<MediaUsageWithTitle>> GetUsageWithTitleAsync(long mediaAssetId);

    Task<int> SafeDeleteAsync(long assetId);   // 0 = deleted, 1 = blocked
    Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName);
    Task DeleteUsageForEntryAsync(long contentEntryId);
}
