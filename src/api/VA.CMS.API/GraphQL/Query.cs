using VA.CMS.API.GraphQL.DataLoaders;
using VA.CMS.API.GraphQL.Types;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.GraphQL;

/// <summary>
/// Hot Chocolate root Query type.
///
/// Exposes:
///   - contentEntry(id)      — single ContentEntry by ID (via DataLoader)
///   - contentEntries(...)   — paginated list with status/type filters
///   - mediaAsset(id)        — single MediaAsset by ID (via DataLoader)
///   - mediaAssets(...)      — paginated list
///   - navigationMenu(handle)— NavigationMenu by handle
///   - taxonomyTerms(handle) — TaxonomyTerm tree for a taxonomy handle
/// </summary>
public class Query
{
    // ─── ContentEntry ─────────────────────────────────────────────────────────

    /// <summary>Fetch a single content entry by its internal ID.</summary>
    public async Task<ContentEntryType?> GetContentEntryAsync(
        long id,
        ContentEntryByIdDataLoader dataLoader,
        CancellationToken ct)
        => await dataLoader.LoadAsync(id, ct);

    /// <summary>
    /// List content entries with optional status/type filters.
    /// Returns up to <paramref name="first"/> rows (default 25, max 100).
    /// </summary>
    public async Task<IReadOnlyList<ContentEntryType>> GetContentEntriesAsync(
        [Service] IContentEntryRepository repo,
        string? status = null,
        long?   contentTypeId = null,
        int     first  = 25,
        int     page   = 1,
        CancellationToken ct = default)
    {
        var pageSize = Math.Min(Math.Max(first, 1), 100);
        var result   = await repo.ListAsync(page, pageSize, status, contentTypeId);

        return result.Items
            .Select(ContentEntryByIdDataLoader.MapToType)
            .ToList();
    }

    // ─── MediaAsset ───────────────────────────────────────────────────────────

    /// <summary>Fetch a single media asset by its internal ID.</summary>
    public async Task<MediaAssetType?> GetMediaAssetAsync(
        long id,
        MediaAssetByIdDataLoader dataLoader,
        CancellationToken ct)
        => await dataLoader.LoadAsync(id, ct);

    /// <summary>
    /// List media assets.
    /// Optionally filter by MIME type prefix (e.g. "image/") or a search term.
    /// </summary>
    public async Task<IReadOnlyList<MediaAssetType>> GetMediaAssetsAsync(
        [Service] IMediaAssetRepository repo,
        string? mimeTypePrefix = null,
        string? searchTerm     = null,
        int     first  = 25,
        int     page   = 1,
        CancellationToken ct = default)
    {
        var pageSize = Math.Min(Math.Max(first, 1), 100);
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
}
