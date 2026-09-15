using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Public navigation endpoint.
/// Issue #47 — BRD FR-NAV-01.
///
///   GET /api/v1/navigation/{handle}   — anonymous, returns nav tree as nested JSON
///
/// The 'primary' handle drives the USWDS header on the public site.
/// The response is suitable for Next.js ISR caching with revalidation on menu save.
/// </summary>
[ApiController]
[Route("api/v1/navigation")]
[AllowAnonymous]
public class NavigationController : ControllerBase
{
    private readonly INavigationRepository _nav;

    public NavigationController(INavigationRepository nav) => _nav = nav;

    /// <summary>
    /// Return the navigation tree for the given handle as nested JSON.
    /// Flat rows from the SP are assembled into a parent→children hierarchy here
    /// so the public site only needs one HTTP call and zero tree-walking.
    ///
    /// Returns 404 when the menu handle is not found in the database.
    /// Returns an empty items array [] when the menu exists but has no visible items.
    /// </summary>
    [HttpGet("{handle}")]
    [ProducesResponseType(typeof(NavigationMenuResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMenuTree(string handle)
    {
        // The SP raises an error / returns nothing when the menu doesn't exist.
        // We return an empty result and 404 to differentiate "menu not found" from
        // "menu exists but has no items".
        var flat = (await _nav.GetMenuTreeAsync(handle)).ToList();

        // Build the nested tree from the flat sorted result.
        // SP orders by ParentItemId, SortOrder — root items have ParentItemId == null.
        var itemMap = flat.ToDictionary(
            i => i.Id,
            i => new NavItemDto
            {
                Id       = i.Id,
                Label    = i.Label,
                Url      = i.Url ?? string.Empty,
                Target   = i.Target,
                Children = new List<NavItemDto>(),
            });

        var roots = new List<NavItemDto>();
        foreach (var item in flat)
        {
            var dto = itemMap[item.Id];
            if (item.ParentItemId is null)
            {
                roots.Add(dto);
            }
            else if (itemMap.TryGetValue(item.ParentItemId.Value, out var parent))
            {
                parent.Children.Add(dto);
            }
        }

        return Ok(new NavigationMenuResponse(handle, roots));
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Response envelope for GET /api/v1/navigation/{handle}.</summary>
public sealed record NavigationMenuResponse(
    string Handle,
    IReadOnlyList<NavItemDto> Items);

/// <summary>A single navigation item, potentially nested via Children.</summary>
public sealed class NavItemDto
{
    public long   Id       { get; init; }
    public string Label    { get; init; } = string.Empty;
    public string Url      { get; init; } = string.Empty;
    public string Target   { get; init; } = "_self";
    public List<NavItemDto> Children { get; init; } = new();
}
