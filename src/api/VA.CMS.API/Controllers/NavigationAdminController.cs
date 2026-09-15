using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Admin navigation menu CRUD API.
/// Issue #46 — BRD FR-NAV-01, FR-NAV-03.
///
/// GET    /api/v1/navigation/{handle}/items      — list all items (admin, includes hidden)
/// POST   /api/v1/navigation/{handle}/items      — create item
/// PATCH  /api/v1/navigation/{handle}/items/{id} — update item
/// DELETE /api/v1/navigation/{handle}/items/{id} — delete item
///
/// GET    /api/v1/navigation                     — list all menus
/// POST   /api/v1/navigation                     — create menu
/// PATCH  /api/v1/navigation/{handle}            — rename menu
/// DELETE /api/v1/navigation/{handle}            — delete menu
///
/// POST   /api/v1/navigation/{handle}/reorder    — bulk reorder (drag-and-drop)
/// GET    /api/v1/navigation/{handle}/preview    — render menu tree for preview
/// </summary>
[ApiController]
[Route("api/v1/navigation")]
[Authorize]
public class NavigationAdminController : ControllerBase
{
    private readonly INavigationMenuRepository _menus;
    private readonly INavigationRepository _nav;

    public NavigationAdminController(
        INavigationMenuRepository menus,
        INavigationRepository nav)
    {
        _menus = menus;
        _nav   = nav;
    }

    // ── Menu CRUD ─────────────────────────────────────────────────────────────

    /// <summary>List all navigation menus.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NavigationMenu>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMenus()
    {
        var menus = await _menus.ListAllAsync();
        return Ok(menus);
    }

    /// <summary>Create a new navigation menu.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(NavigationMenu), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateMenu([FromBody] CreateMenuRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });
        if (string.IsNullOrWhiteSpace(req.Handle))
            return BadRequest(new { error = "Handle is required." });

        var id = await _menus.CreateAsync(req.Name.Trim(), req.Handle.Trim());
        var menu = await _menus.GetByHandleAsync(req.Handle.Trim());
        return CreatedAtAction(nameof(GetMenuItems),
            new { handle = req.Handle.Trim() },
            menu);
    }

    /// <summary>Rename a navigation menu.</summary>
    [HttpPatch("{handle}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMenu(string handle, [FromBody] UpdateMenuRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });

        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        await _menus.UpdateAsync(menu.Id, req.Name.Trim());
        return NoContent();
    }

    /// <summary>Delete a navigation menu and all its items.</summary>
    [HttpDelete("{handle}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMenu(string handle)
    {
        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        await _menus.DeleteAsync(menu.Id);
        return NoContent();
    }

    // ── Item CRUD ─────────────────────────────────────────────────────────────

    /// <summary>
    /// List all items in a menu — includes hidden items.
    /// Returns items as a flat list ordered by ParentItemId, SortOrder.
    /// The admin UI assembles the tree from this flat list.
    /// </summary>
    [HttpGet("{handle}/items")]
    [ProducesResponseType(typeof(NavItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMenuItems(string handle)
    {
        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        var items = (await _nav.GetMenuTreeAdminAsync(handle)).ToList();
        return Ok(new NavItemsResponse(menu.Id, handle, items.Select(MapItem)));
    }

    /// <summary>Create a new navigation item in a menu.</summary>
    [HttpPost("{handle}/items")]
    [ProducesResponseType(typeof(NavItemAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateItem(string handle, [FromBody] UpsertItemRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label))
            return BadRequest(new { error = "Label is required." });

        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        // Depth guard: verify parent chain does not exceed 3 levels
        if (req.ParentItemId.HasValue)
        {
            var depthError = await ValidateDepthAsync(req.ParentItemId.Value, parentDepth: 0);
            if (depthError is not null)
                return UnprocessableEntity(new { error = depthError });
        }

        var item = new NavigationItem
        {
            MenuId         = menu.Id,
            ParentItemId   = req.ParentItemId,
            Label          = req.Label.Trim(),
            Url            = req.Url?.Trim(),
            ContentEntryId = req.ContentEntryId,
            Target         = req.Target ?? "_self",
            SortOrder      = req.SortOrder,
            IsVisible      = req.IsVisible ?? true,
        };

        var id = await _nav.UpsertItemAsync(item);
        item.Id = id;

        return CreatedAtAction(nameof(GetMenuItems),
            new { handle },
            MapItem(item));
    }

    /// <summary>Update a navigation item (label, URL, visibility, target).</summary>
    [HttpPatch("{handle}/items/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateItem(
        string handle, long id, [FromBody] UpsertItemRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label))
            return BadRequest(new { error = "Label is required." });

        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        var existing = await _nav.GetItemAsync(id);
        if (existing is null || existing.MenuId != menu.Id) return NotFound();

        // Depth guard on reparent
        if (req.ParentItemId.HasValue && req.ParentItemId != existing.ParentItemId)
        {
            var depthError = await ValidateDepthAsync(req.ParentItemId.Value, parentDepth: 0);
            if (depthError is not null)
                return UnprocessableEntity(new { error = depthError });
        }

        existing.Label          = req.Label.Trim();
        existing.Url            = req.Url?.Trim();
        existing.ContentEntryId = req.ContentEntryId;
        existing.Target         = req.Target ?? "_self";
        existing.SortOrder      = req.SortOrder;
        existing.IsVisible      = req.IsVisible ?? existing.IsVisible;
        if (req.ParentItemId is not null)
            existing.ParentItemId = req.ParentItemId;

        await _nav.UpsertItemAsync(existing);
        return NoContent();
    }

    /// <summary>Delete a navigation item and all its children.</summary>
    [HttpDelete("{handle}/items/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteItem(string handle, long id)
    {
        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        var existing = await _nav.GetItemAsync(id);
        if (existing is null || existing.MenuId != menu.Id) return NotFound();

        await _nav.DeleteItemAsync(id);
        return NoContent();
    }

    // ── Bulk reorder (drag-and-drop save) ────────────────────────────────────

    /// <summary>
    /// Apply drag-and-drop reorder: update SortOrder and ParentItemId for all items.
    /// Validates that no item exceeds 3 levels deep.
    /// Request body: array of { id, parentItemId, sortOrder }.
    /// </summary>
    [HttpPost("{handle}/reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BulkReorder(
        string handle, [FromBody] List<ReorderItemRequest> items)
    {
        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        if (items is null || items.Count == 0)
            return BadRequest(new { error = "At least one item is required." });

        // Validate depth: build parent→depth map from the request
        var depthError = ValidateReorderDepth(items);
        if (depthError is not null)
            return UnprocessableEntity(new { error = depthError });

        var json = JsonSerializer.Serialize(
            items.Select(i => new { id = i.Id, parentItemId = i.ParentItemId, sortOrder = i.SortOrder }),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await _nav.BulkReorderAsync(menu.Id, json);
        return NoContent();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the menu tree as nested JSON — same shape as the public GET /navigation/{handle},
    /// but includes hidden items so admins can preview the layout before saving.
    /// </summary>
    [HttpGet("{handle}/preview")]
    [ProducesResponseType(typeof(NavigationMenuPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewMenu(string handle)
    {
        var menu = await _menus.GetByHandleAsync(handle);
        if (menu is null) return NotFound();

        var flat = (await _nav.GetMenuTreeAdminAsync(handle)).ToList();

        // Assemble nested tree — same logic as NavigationController
        var itemMap = flat.ToDictionary(
            i => i.Id,
            i => new NavPreviewItemDto
            {
                Id        = i.Id,
                Label     = i.Label,
                Url       = i.Url ?? string.Empty,
                Target    = i.Target,
                IsVisible = i.IsVisible,
                Depth     = i.Depth,
                Children  = new List<NavPreviewItemDto>(),
            });

        var roots = new List<NavPreviewItemDto>();
        foreach (var item in flat)
        {
            var dto = itemMap[item.Id];
            if (item.ParentItemId is null)
                roots.Add(dto);
            else if (itemMap.TryGetValue(item.ParentItemId.Value, out var parent))
                parent.Children.Add(dto);
        }

        return Ok(new NavigationMenuPreviewResponse(handle, menu.Name, roots));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up the parent chain and returns an error message if depth would exceed 3 levels
    /// (root = 0, max visible depth = 2, max parent depth = 2 meaning item would be depth 3).
    /// Returns null when depth is valid.
    /// </summary>
    private async Task<string?> ValidateDepthAsync(long parentItemId, int parentDepth)
    {
        if (parentDepth >= 2)
            return "Navigation items cannot be nested more than 3 levels deep.";

        var parent = await _nav.GetItemAsync(parentItemId);
        if (parent is null) return null; // parent not found — let DB enforce
        if (parent.ParentItemId is null) return null; // parent is root (depth=0), item will be depth=1
        return await ValidateDepthAsync(parent.ParentItemId.Value, parentDepth + 1);
    }

    private static string? ValidateReorderDepth(List<ReorderItemRequest> items)
    {
        // Build id→parentItemId map from the request
        var parentMap = items.ToDictionary(i => i.Id, i => i.ParentItemId);

        foreach (var item in items)
        {
            int depth = 0;
            var current = item.ParentItemId;
            while (current.HasValue)
            {
                depth++;
                if (depth > 2)
                    return $"Item {item.Id} would be more than 3 levels deep after reorder.";
                if (!parentMap.TryGetValue(current.Value, out var next)) break;
                current = next;
            }
        }
        return null;
    }

    private static NavItemAdminDto MapItem(NavigationItem i) => new()
    {
        Id             = i.Id,
        MenuId         = i.MenuId,
        ParentItemId   = i.ParentItemId,
        Label          = i.Label,
        Url            = i.Url,
        ContentEntryId = i.ContentEntryId,
        Target         = i.Target,
        SortOrder      = i.SortOrder,
        IsVisible      = i.IsVisible,
        Depth          = i.Depth,
    };
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Request body for creating a navigation menu.</summary>
public sealed record CreateMenuRequest(string Name, string Handle);

/// <summary>Request body for renaming a navigation menu.</summary>
public sealed record UpdateMenuRequest(string Name);

/// <summary>Request body for creating or updating a navigation item.</summary>
public sealed class UpsertItemRequest
{
    public string  Label          { get; init; } = string.Empty;
    public string? Url            { get; init; }
    public long?   ContentEntryId { get; init; }
    public string? Target         { get; init; }
    public int     SortOrder      { get; init; }
    public bool?   IsVisible      { get; init; }
    public long?   ParentItemId   { get; init; }
}

/// <summary>Single item in a bulk-reorder request.</summary>
public sealed class ReorderItemRequest
{
    public long  Id           { get; init; }
    public long? ParentItemId { get; init; }
    public int   SortOrder    { get; init; }
}

/// <summary>Admin view of a navigation item — includes visibility and depth.</summary>
public sealed class NavItemAdminDto
{
    public long    Id             { get; init; }
    public long    MenuId         { get; init; }
    public long?   ParentItemId   { get; init; }
    public string  Label          { get; init; } = string.Empty;
    public string? Url            { get; init; }
    public long?   ContentEntryId { get; init; }
    public string  Target         { get; init; } = "_self";
    public int     SortOrder      { get; init; }
    public bool    IsVisible      { get; init; }
    public int     Depth          { get; init; }
}

/// <summary>Response envelope for GET /api/v1/navigation/{handle}/items.</summary>
public sealed record NavItemsResponse(
    long MenuId,
    string Handle,
    IEnumerable<NavItemAdminDto> Items);

/// <summary>Preview response — nested tree with visibility info.</summary>
public sealed record NavigationMenuPreviewResponse(
    string Handle,
    string Name,
    IReadOnlyList<NavPreviewItemDto> Items);

/// <summary>Preview tree node — includes IsVisible so admin preview can dim hidden items.</summary>
public sealed class NavPreviewItemDto
{
    public long   Id        { get; init; }
    public string Label     { get; init; } = string.Empty;
    public string Url       { get; init; } = string.Empty;
    public string Target    { get; init; } = "_self";
    public bool   IsVisible { get; init; }
    public int    Depth     { get; init; }
    public List<NavPreviewItemDto> Children { get; init; } = new();
}
