using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.API.Navigation;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Admin redirect management API.
/// Issue #48 — BRD FR-NAV-06.
///
/// GET    /api/v1/redirects           — list all redirects (paginated, filterable by isActive)
/// POST   /api/v1/redirects           — create a new redirect
/// GET    /api/v1/redirects/{id}      — get a single redirect
/// PATCH  /api/v1/redirects/{id}      — edit a redirect (FromPath / ToPath / StatusCode)
/// DELETE /api/v1/redirects/{id}      — soft-deactivate a redirect
///
/// #155: reads need any CMS role; mutations need CanManageSite (SiteAdmin /
/// SystemAdmin). FromPath must be site-relative and must not shadow a published
/// slug; ToPath must be site-relative or an https URL on an allow-listed host.
/// </summary>
[ApiController]
[Route("api/v1/redirects")]
[Authorize(Policy = CmsRoles.Policies.CanRead)]
public class RedirectAdminController : ControllerBase
{
    private readonly INavigationRepository   _nav;
    private readonly IContentEntryRepository _entries;
    private readonly ISiteSettingsService    _settings;

    public RedirectAdminController(
        INavigationRepository   nav,
        IContentEntryRepository entries,
        ISiteSettingsService    settings)
    {
        _nav      = nav;
        _entries  = entries;
        _settings = settings;
    }

    // ── GET /api/v1/redirects ─────────────────────────────────────────────────

    /// <summary>
    /// List redirects with pagination.
    /// Optional query string: isActive=true|false (omit for all).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RedirectListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRedirects(
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        pageSize = _settings.ClampPageSize(pageSize);

        var (rows, total) = await _nav.ListRedirectsAsync(isActive, page, pageSize);
        return Ok(new RedirectListResponse(
            Items:      rows.Select(MapToDto).ToList(),
            TotalRows:  total,
            Page:       page,
            PageSize:   pageSize));
    }

    // ── GET /api/v1/redirects/{id} ────────────────────────────────────────────

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(RedirectAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRedirect(long id)
    {
        var row = await _nav.GetRedirectByIdAsync(id);
        if (row is null) return NotFound();
        return Ok(MapToDto(row));
    }

    // ── POST /api/v1/redirects ────────────────────────────────────────────────

    /// <summary>Create a new redirect rule.</summary>
    [HttpPost]
    [Authorize(Policy = CmsRoles.Policies.CanManageSite)]
    [ProducesResponseType(typeof(RedirectAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRedirect([FromBody] CreateRedirectRequest req)
    {
        if (await ValidateAsync(req.FromPath, req.ToPath, req.StatusCode) is { } error)
            return BadRequest(new { error });

        var actorId = GetCurrentUserId();
        var redirect = new Redirect
        {
            FromPath    = req.FromPath.Trim(),
            ToPath      = req.ToPath.Trim(),
            StatusCode  = req.StatusCode,
            IsActive    = true,
            CreatedById = actorId,
        };

        var newId = await _nav.CreateRedirectAsync(redirect);
        var row   = await _nav.GetRedirectByIdAsync(newId);

        return Created($"/api/v1/redirects/{newId}", MapToDto(row!));
    }

    // ── PATCH /api/v1/redirects/{id} ──────────────────────────────────────────

    /// <summary>Edit an existing redirect's FromPath, ToPath, and/or StatusCode.</summary>
    [HttpPatch("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanManageSite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRedirect(long id, [FromBody] UpdateRedirectRequest req)
    {
        if (await ValidateAsync(req.FromPath, req.ToPath, req.StatusCode) is { } error)
            return BadRequest(new { error });

        var existing = await _nav.GetRedirectByIdAsync(id);
        if (existing is null) return NotFound();

        await _nav.UpdateRedirectAsync(id, req.FromPath.Trim(), req.ToPath.Trim(), req.StatusCode);
        return NoContent();
    }

    // ── DELETE /api/v1/redirects/{id} ─────────────────────────────────────────

    /// <summary>Soft-deactivate a redirect (sets IsActive = false).</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanManageSite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateRedirect(long id)
    {
        var existing = await _nav.GetRedirectByIdAsync(id);
        if (existing is null) return NotFound();

        await _nav.DeactivateRedirectAsync(id);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Shared create/update validation; returns the first error or null.</summary>
    private async Task<string?> ValidateAsync(string fromPath, string toPath, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(fromPath))
            return "FromPath is required.";
        if (string.IsNullOrWhiteSpace(toPath))
            return "ToPath is required.";
        if (statusCode is not (301 or 302))
            return "StatusCode must be 301 or 302.";

        if (RedirectPathValidator.ValidateFromPath(fromPath) is { } fromError)
            return fromError;

        var allowedHosts = _settings.GetStringList(SiteSettingKeys.RedirectsAllowedExternalHosts);
        if (RedirectPathValidator.ValidateToPath(toPath, allowedHosts) is { } toError)
            return toError;

        if (string.Equals(fromPath.Trim().TrimEnd('/'), toPath.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return "FromPath and ToPath must differ.";

        foreach (var slug in RedirectPathValidator.SlugCandidates(fromPath))
        {
            if (await _entries.GetPublishedBySlugAsync(slug) is not null)
                return $"FromPath collides with published content at slug '{slug}'.";
        }

        return null;
    }

    private long GetCurrentUserId()
    {
        var claim = User?.FindFirst("sub") ?? User?.FindFirst("userId");
        return claim is not null && long.TryParse(claim.Value, out var id) ? id : 0;
    }

    private static RedirectAdminDto MapToDto(RedirectAdminRow row) => new(
        Id:                   row.Id,
        FromPath:             row.FromPath,
        ToPath:               row.ToPath,
        StatusCode:           row.StatusCode,
        IsActive:             row.IsActive,
        CreatedById:          row.CreatedById,
        CreatedByEmail:       row.CreatedByEmail,
        CreatedByDisplayName: row.CreatedByDisplayName,
        CreatedAt:            row.CreatedAt);
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Single redirect row returned by the API.</summary>
public sealed record RedirectAdminDto(
    long      Id,
    string    FromPath,
    string    ToPath,
    int       StatusCode,
    bool      IsActive,
    long      CreatedById,
    string?   CreatedByEmail,
    string?   CreatedByDisplayName,
    DateTime  CreatedAt);

/// <summary>Paginated list response for GET /api/v1/redirects.</summary>
public sealed record RedirectListResponse(
    IReadOnlyList<RedirectAdminDto> Items,
    int TotalRows,
    int Page,
    int PageSize);

/// <summary>Request body for POST /api/v1/redirects.</summary>
public sealed class CreateRedirectRequest
{
    public string FromPath   { get; init; } = string.Empty;
    public string ToPath     { get; init; } = string.Empty;
    public int    StatusCode { get; init; } = 301;
}

/// <summary>Request body for PATCH /api/v1/redirects/{id}.</summary>
public sealed class UpdateRedirectRequest
{
    public string FromPath   { get; init; } = string.Empty;
    public string ToPath     { get; init; } = string.Empty;
    public int    StatusCode { get; init; } = 301;
}
