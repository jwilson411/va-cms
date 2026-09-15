using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Pinned search results admin endpoints — issue #52 (BRD FR-SEARCH-04).
///
/// Endpoints:
///   GET    /api/v1/admin/search/pins
///       Returns all pins (optionally filtered by query string).
///       Used by /admin/search/pins admin table.
///
///   POST   /api/v1/admin/search/pins
///       Creates or updates a pin: links a query string to a content entry.
///       Body: { queryString, contentEntryId }
///
///   DELETE /api/v1/admin/search/pins/{id}
///       Removes a pin by its database Id.
///
/// Auth: all three endpoints require at least SiteAdmin rights (cms:manage_site).
/// </summary>
[ApiController]
[Authorize(Policy = CmsRoles.Policies.CanManageSite)]
public class SearchPinsController : ControllerBase
{
    private readonly ISearchPinRepository _pins;

    public SearchPinsController(ISearchPinRepository pins)
    {
        _pins = pins;
    }

    // ── GET /api/v1/admin/search/pins ─────────────────────────────────────────

    /// <summary>
    /// Returns all pinned results, ordered by query string.
    /// Optionally filter to a single query by passing ?q=…
    ///
    /// Response body:
    ///   {
    ///     "items": [
    ///       {
    ///         "id":                 1,
    ///         "queryString":        "va benefits",
    ///         "contentEntryId":     42,
    ///         "createdAt":          "2026-09-15T12:00:00Z",
    ///         "entrySlug":          "benefits/overview",
    ///         "entryStatus":        "Published",
    ///         "entryContentTypeId": 1,
    ///         "entryTitle":         "VA Benefits Overview"
    ///       },
    ///       …
    ///     ]
    ///   }
    /// </summary>
    [HttpGet("api/v1/admin/search/pins")]
    [ProducesResponseType(typeof(SearchPinsListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery(Name = "q")] string? queryString = null)
    {
        var pins = await _pins.ListAsync(queryString);

        return Ok(new SearchPinsListDto
        {
            Items = pins.Select(p => new SearchPinDto
            {
                Id                 = p.Id,
                QueryString        = p.QueryString,
                ContentEntryId     = p.ContentEntryId,
                CreatedAt          = p.CreatedAt,
                EntrySlug          = p.EntrySlug,
                EntryStatus        = p.EntryStatus,
                EntryContentTypeId = p.EntryContentTypeId,
                EntryTitle         = p.EntryTitle,
            }).ToList(),
        });
    }

    // ── POST /api/v1/admin/search/pins ────────────────────────────────────────

    /// <summary>
    /// Creates or replaces a pin: one query string → one content entry.
    /// If a pin for this query string already exists it is overwritten.
    ///
    /// Request body:
    ///   { "queryString": "va benefits", "contentEntryId": 42 }
    ///
    /// Response: 201 Created with { "id": N }
    /// </summary>
    [HttpPost("api/v1/admin/search/pins")]
    [ProducesResponseType(typeof(CreatePinResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreatePinRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QueryString))
            return BadRequest(new { error = "queryString is required." });

        if (request.ContentEntryId <= 0)
            return BadRequest(new { error = "contentEntryId must be a positive integer." });

        // Extract caller's user id from JWT claims (null in DevBypass with no claim)
        long? actorId = null;
        var actorClaim = User.FindFirst("cms_user_id");
        if (actorClaim != null && long.TryParse(actorClaim.Value, out var parsed))
            actorId = parsed;

        var newId = await _pins.CreateAsync(
            request.QueryString.Trim(),
            request.ContentEntryId,
            createdById: actorId);

        return StatusCode(StatusCodes.Status201Created, new CreatePinResponseDto { Id = newId });
    }

    // ── DELETE /api/v1/admin/search/pins/{id} ─────────────────────────────────

    /// <summary>
    /// Removes a pin by Id.
    /// Returns 204 No Content on success (even if the pin did not exist).
    /// </summary>
    [HttpDelete("api/v1/admin/search/pins/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id)
    {
        await _pins.DeleteAsync(id);
        return NoContent();
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Envelope for GET /api/v1/admin/search/pins.</summary>
public sealed class SearchPinsListDto
{
    public IReadOnlyList<SearchPinDto> Items { get; init; } = [];
}

/// <summary>Single pin row returned by GET /api/v1/admin/search/pins.</summary>
public sealed class SearchPinDto
{
    public long     Id                 { get; init; }
    public string   QueryString        { get; init; } = string.Empty;
    public long     ContentEntryId     { get; init; }
    public DateTime CreatedAt          { get; init; }
    public string?  EntrySlug          { get; init; }
    public string?  EntryStatus        { get; init; }
    public long?    EntryContentTypeId { get; init; }
    public string?  EntryTitle         { get; init; }
}

/// <summary>Request body for POST /api/v1/admin/search/pins.</summary>
public sealed class CreatePinRequest
{
    public string QueryString    { get; init; } = string.Empty;
    public long   ContentEntryId { get; init; }
}

/// <summary>Response body for a successful POST /api/v1/admin/search/pins.</summary>
public sealed class CreatePinResponseDto
{
    public long Id { get; init; }
}
