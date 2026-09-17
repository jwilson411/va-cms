using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using VA.CMS.API.Auth;
using VA.CMS.API.GraphQL.DataLoaders;
using VA.CMS.API.GraphQL.Types;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.GraphQL;

/// <summary>
/// Hot Chocolate root Query type.
///
/// Exposes:
///   - contentEntry(id)      — single ContentEntry by ID (via DataLoader)
///   - contentEntries(...)   — paginated list with status/type filters
///   - mediaAsset(id)        — single MediaAsset by ID (via DataLoader)
///   - mediaAssets(...)      — paginated list (CanRead only)
///   - navigationMenu(handle)— NavigationMenu by handle
///   - taxonomyTerms(handle) — TaxonomyTerm tree for a taxonomy handle
///
/// Audience (#156): anonymous callers are limited to Published content — the
/// status filter is forced at the stored-procedure level — and may read a media
/// asset only when published content references it. Callers with CanRead see
/// everything. See <see cref="IGraphQLAudience"/>.
/// </summary>
public class Query
{
    // ─── ContentEntry ─────────────────────────────────────────────────────────

    /// <summary>Fetch a single content entry by its internal ID. Anonymous callers only see Published entries.</summary>
    public async Task<ContentEntryType?> GetContentEntryAsync(
        long id,
        ContentEntryByIdDataLoader dataLoader,
        IResolverContext context,
        [Service] IGraphQLAudience audience,
        CancellationToken ct)
    {
        var entry = await dataLoader.LoadAsync(id, ct);
        if (entry is null)
            return null;

        if (!IsPublished(entry.Status) && !await audience.CanReadAllAsync(context))
            return null;

        return entry;
    }

    /// <summary>
    /// List content entries with optional status/type filters.
    /// Returns up to <paramref name="first"/> rows (default 25, max api.maxPageSize).
    /// Anonymous callers always get <c>status: "Published"</c> regardless of the argument.
    /// </summary>
    public async Task<IReadOnlyList<ContentEntryType>> GetContentEntriesAsync(
        [Service] IContentEntryRepository repo,
        [Service] ISiteSettingsService settings,
        [Service] IGraphQLAudience audience,
        IResolverContext context,
        string? status = null,
        long?   contentTypeId = null,
        int     first  = 25,
        int     page   = 1,
        CancellationToken ct = default)
    {
        // The public audience is pinned to Published inside usp_ContentEntry_List —
        // never filtered after the fact in C#.
        if (!await audience.CanReadAllAsync(context))
            status = PublishedStatus;

        var pageSize = settings.ClampPageSize(first);
        var result   = await repo.ListAsync(page, pageSize, status, contentTypeId);

        return result.Items
            .Select(ContentEntryByIdDataLoader.MapToType)
            .ToList();
    }

    // ─── MediaAsset ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch a single media asset by its internal ID. Anonymous callers only get
    /// assets that published content references (MediaUsage rows with a Published entry).
    /// </summary>
    public async Task<MediaAssetType?> GetMediaAssetAsync(
        long id,
        MediaAssetByIdDataLoader dataLoader,
        IResolverContext context,
        [Service] IGraphQLAudience audience,
        [Service] IMediaExtendedRepository usageRepo,
        CancellationToken ct)
    {
        var asset = await dataLoader.LoadAsync(id, ct);
        if (asset is null)
            return null;

        if (await audience.CanReadAllAsync(context))
            return asset;

        var usage = await usageRepo.GetUsageAsync(id);
        return usage.Any(u => IsPublished(u.Status)) ? asset : null;
    }

    /// <summary>
    /// List media assets (CanRead only — the public site never needs to enumerate uploads).
    /// Optionally filter by MIME type prefix (e.g. "image/") or a search term.
    /// </summary>
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    public async Task<IReadOnlyList<MediaAssetType>> GetMediaAssetsAsync(
        [Service] IMediaAssetRepository repo,
        [Service] ISiteSettingsService settings,
        string? mimeTypePrefix = null,
        string? searchTerm     = null,
        int     first  = 25,
        int     page   = 1,
        CancellationToken ct = default)
    {
        var pageSize = settings.ClampPageSize(first);
        var result   = await repo.ListAsync(page, pageSize, mimeTypePrefix, searchTerm);

        return result.Items
            .Select(MediaAssetByIdDataLoader.MapToType)
            .ToList();
    }

    // ─── NavigationMenu ───────────────────────────────────────────────────────

    /// <summary>Fetch a navigation menu and its items by handle (e.g. "primary").</summary>
    public async Task<NavigationMenuType?> GetNavigationMenuAsync(
        string handle,
        [Service] INavigationMenuRepository repo,
        CancellationToken ct)
    {
        var menu = await repo.GetByHandleAsync(handle);
        if (menu is null) return null;

        return new NavigationMenuType
        {
            Id        = menu.Id,
            Name      = menu.Name,
            Handle    = menu.Handle,
            CreatedAt = menu.CreatedAt,
            UpdatedAt = menu.UpdatedAt,
        };
    }

    // ─── TaxonomyTerm ─────────────────────────────────────────────────────────

    /// <summary>
    /// Return the full term tree for a taxonomy identified by <paramref name="handle"/>
    /// (e.g. "topics").
    /// </summary>
    public async Task<IReadOnlyList<TaxonomyTermType>> GetTaxonomyTermsAsync(
        string handle,
        [Service] ITaxonomyRepository repo,
        CancellationToken ct)
    {
        var terms = await repo.GetTermTreeAsync(handle);

        return terms
            .Select(t => new TaxonomyTermType
            {
                Id           = t.Id,
                TaxonomyId   = t.TaxonomyId,
                ParentTermId = t.ParentTermId,
                Name         = t.Name,
                Slug         = t.Slug,
                SortOrder    = t.SortOrder,
                Depth        = t.Depth,
            })
            .ToList();
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private const string PublishedStatus = "Published";

    private static bool IsPublished(string status)
        => string.Equals(status, PublishedStatus, StringComparison.OrdinalIgnoreCase);
}
