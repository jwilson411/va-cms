using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Full-text search endpoint.
/// Issue #49 — Epic #8 E-Search — BRD FR-SEARCH-02.
/// Issue #50 — Epic #8 E-Search — BRD FR-SEARCH-01 (public site integration).
///
///   GET /api/v1/search?q=&amp;type=&amp;from=&amp;to=&amp;tag=&amp;page=&amp;pageSize=
///
///   - Public endpoint: no JWT required (search is available to the public site).
///   - Delegates to usp_Search_FullText via ISearchRepository.
///   - Logs every query (result count + optional userId) via usp_Search_LogQuery.
///   - Returns results ordered by FTS rank descending.
///   - Each result includes: title, content type, slug, summary excerpt, published date.
///
/// Filter parameters (all optional, AND-combined):
///   type  — ContentTypeId (long)
///   from  — published on or after this date (ISO 8601, UTC)
///   to    — published on or before this date (ISO 8601, UTC)
///   tag   — taxonomy term ID (long)
/// </summary>
[ApiController]
[Route("api/v1/search")]
[AllowAnonymous]
public class SearchController : ControllerBase
{
    private readonly ISearchRepository _search;

    public SearchController(ISearchRepository search)
    {
        _search = search;
    }

    /// <summary>
    /// Full-text search over published content.
    ///
    /// Parameters:
    ///   q          — required; the search query string.
    ///   type       — optional; filter by ContentTypeId (exact match).
    ///   from       — optional; ISO 8601 UTC date — only entries published on/after.
    ///   to         — optional; ISO 8601 UTC date — only entries published on/before.
    ///   tag        — optional; taxonomy term ID — only entries tagged with this term.
    ///   page       — optional; 1-based page number (default 1).
    ///   pageSize   — optional; results per page, 1–100 (default 25).
    ///
    /// Response body:
    ///   {
    ///     "query":      "...",
    ///     "page":       1,
    ///     "pageSize":   25,
    ///     "totalItems": N,
    ///     "items": [
    ///       {
    ///         "id":              123,
    ///         "title":           "...",
    ///         "slug":            "...",
    ///         "contentTypeId":   1,
    ///         "contentTypeName": "Standard Page",
    ///         "excerpt":         "...",
    ///         "publishedAt":     "2026-01-01T00:00:00Z",
    ///         "rank":            N
    ///       },
    ///       ...
    ///     ]
    ///   }
    ///
    /// When Full-Text Search is not installed on the SQL Server instance,
    /// the SP returns an empty result set — this endpoint returns 200 with 0 items.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] long? type = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] long? tag = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var results = await _search.FullTextSearchAsync(
            q.Trim(),
            contentTypeId: type,
            fromDate: from,
            toDate: to,
            tagTermId: tag,
            page: page,
            pageSize: pageSize);

        // Log every search query for admin analytics (issue #49 / FR-SEARCH-02).
        // Fire-and-forget: we do not await to avoid delaying the HTTP response.
        // A failure here does not affect the search result.
        _ = _search.LogQueryAsync(q.Trim(), (int)results.TotalItems);

        return Ok(new SearchResponse
        {
            Query      = q.Trim(),
            Page       = page,
            PageSize   = pageSize,
            TotalItems = (int)results.TotalItems,
            Items      = results.Items.Select(r => new SearchResultDto
            {
                Id              = r.Id,
                Title           = r.Title,
                Slug            = r.Slug,
                ContentTypeId   = r.ContentTypeId,
                ContentTypeName = r.ContentTypeName,
                Excerpt         = r.Excerpt,
                PublishedAt     = r.PublishedAt,
                Rank            = r.Rank,
            }).ToList(),
        });
    }
}

/// <summary>Response envelope for GET /api/v1/search.</summary>
public sealed class SearchResponse
{
    public string Query { get; init; } = string.Empty;
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public IReadOnlyList<SearchResultDto> Items { get; init; } = [];
}

/// <summary>
/// Single result item returned by GET /api/v1/search.
/// Fields mapped from usp_Search_FullText via SearchResult POCO.
/// </summary>
public sealed class SearchResultDto
{
    public long Id { get; init; }

    /// <summary>
    /// Title extracted from FieldsJson 'title' field.
    /// Null when the content type has no title field.
    /// </summary>
    public string? Title { get; init; }

    public string Slug { get; init; } = string.Empty;
    public long ContentTypeId { get; init; }

    /// <summary>Human-readable content type name (e.g. "News Article", "Standard Page").</summary>
    public string ContentTypeName { get; init; } = string.Empty;

    /// <summary>
    /// Summary excerpt — 'summary' field from FieldsJson when present,
    /// otherwise first 300 chars of extracted plain text from all fields.
    /// </summary>
    public string? Excerpt { get; init; }

    /// <summary>When the entry was last published (ContentEntry.UpdatedAt at publish time).</summary>
    public DateTime PublishedAt { get; init; }

    /// <summary>Full-text search rank score (higher = more relevant).</summary>
    public int Rank { get; init; }
}
