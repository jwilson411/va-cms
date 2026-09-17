using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Search analytics admin endpoints — issue #51 (BRD FR-SEARCH-06).
///
/// Endpoints:
///   GET /api/v1/admin/search/analytics/summary
///       Returns top 10 queries + top 10 zero-result queries (last 30 days).
///       Used by the admin dashboard widget.
///
///   GET /api/v1/admin/search/analytics
///       Full paginated analytics table (query, count, zero-result, CTR).
///       Used by /admin/search/analytics admin page.
///
///   POST /api/v1/search/click
///       Records a user click on a search result for CTR tracking.
///       Public (no JWT) — called by the public-site search component.
/// </summary>
[ApiController]
public class SearchAnalyticsController : ControllerBase
{
    private readonly ISearchAnalyticsRepository _analytics;
    private readonly ISiteSettingsService       _settings;

    public SearchAnalyticsController(ISearchAnalyticsRepository analytics, ISiteSettingsService settings)
    {
        _analytics = analytics;
        _settings  = settings;
    }

    // ── Dashboard summary widget ──────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/admin/search/analytics/summary
    /// Returns top 10 queries and top 10 zero-result queries over the last 30 days.
    /// Requires any CMS role (admin users only).
    /// </summary>
    [HttpGet("api/v1/admin/search/analytics/summary")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(SearchAnalyticsSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var topQueries = await _analytics.GetTopQueriesAsync(topN: 10, daysBack: 30);
        var zeroResult = await _analytics.GetZeroResultQueriesAsync(topN: 10, daysBack: 30);

        return Ok(new SearchAnalyticsSummaryDto
        {
            TopQueries         = topQueries.Select(r => new TopQueryDto
            {
                Query           = r.Query,
                SearchCount     = r.SearchCount,
                ZeroResultCount = r.ZeroResultCount,
                AvgResultCount  = r.AvgResultCount,
                LastSearchedAt  = r.LastSearchedAt,
            }).ToList(),
            ZeroResultQueries  = zeroResult.Select(r => new ZeroResultQueryDto
            {
                Query           = r.Query,
                ZeroResultCount = r.ZeroResultCount,
                LastSearchedAt  = r.LastSearchedAt,
            }).ToList(),
        });
    }

    // ── Full analytics table ──────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/admin/search/analytics
    /// Full paginated analytics table with CTR, used by /admin/search/analytics page.
    /// Requires any CMS role.
    /// </summary>
    [HttpGet("api/v1/admin/search/analytics")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(SearchAnalyticsPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFull(
        [FromQuery] int daysBack  = 30,
        [FromQuery] int page      = 1,
        [FromQuery] int pageSize  = 50)
    {
        pageSize = _settings.ClampPageSize(pageSize);
        page     = Math.Max(1, page);
        daysBack = Math.Clamp(daysBack, 1, 365);

        var (items, totalRows) = await _analytics.GetFullAnalyticsAsync(daysBack, page, pageSize);

        return Ok(new SearchAnalyticsPageDto
        {
            TotalRows  = totalRows,
            TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalRows / (double)pageSize) : 0,
            Page       = page,
            PageSize   = pageSize,
            DaysBack   = daysBack,
            Items      = items.Select(r => new SearchAnalyticsRowDto
            {
                Query            = r.Query,
                SearchCount      = r.SearchCount,
                ZeroResultCount  = r.ZeroResultCount,
                AvgResultCount   = r.AvgResultCount,
                LastSearchedAt   = r.LastSearchedAt,
                ClickCount       = r.ClickCount,
                ClickThroughRate = r.ClickThroughRate,
            }).ToList(),
        });
    }

    // ── Click-through tracking ────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/search/click
    /// Records a user click on a search result for CTR analytics.
    /// Public endpoint — no JWT required (called from public site search).
    /// Body: { query, clickedSlug, resultRank }
    /// </summary>
    [HttpPost("api/v1/search/click")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LogClick([FromBody] LogClickRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || string.IsNullOrWhiteSpace(request.ClickedSlug))
            return BadRequest(new { error = "query and clickedSlug are required." });

        await _analytics.LogClickAsync(
            query:       request.Query.Trim(),
            clickedSlug: request.ClickedSlug.Trim(),
            resultRank:  request.ResultRank,
            userId:      null); // public endpoint — no authenticated user

        return NoContent();
    }
}

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>Request body for POST /api/v1/search/click.</summary>
public sealed class LogClickRequest
{
    public string Query       { get; init; } = string.Empty;
    public string ClickedSlug { get; init; } = string.Empty;
    public int    ResultRank  { get; init; }
}

/// <summary>Response for GET /api/v1/admin/search/analytics/summary (dashboard widget).</summary>
public sealed class SearchAnalyticsSummaryDto
{
    public IReadOnlyList<TopQueryDto>       TopQueries         { get; init; } = [];
    public IReadOnlyList<ZeroResultQueryDto> ZeroResultQueries { get; init; } = [];
}

/// <summary>Single row in the top-queries widget.</summary>
public sealed class TopQueryDto
{
    public string   Query           { get; init; } = string.Empty;
    public int      SearchCount     { get; init; }
    public int      ZeroResultCount { get; init; }
    public decimal  AvgResultCount  { get; init; }
    public DateTime LastSearchedAt  { get; init; }
}

/// <summary>Single row in the zero-result queries widget.</summary>
public sealed class ZeroResultQueryDto
{
    public string   Query           { get; init; } = string.Empty;
    public int      ZeroResultCount { get; init; }
    public DateTime LastSearchedAt  { get; init; }
}

/// <summary>Paginated response for GET /api/v1/admin/search/analytics (full table).</summary>
public sealed class SearchAnalyticsPageDto
{
    public int TotalRows  { get; init; }
    public int TotalPages { get; init; }
    public int Page       { get; init; }
    public int PageSize   { get; init; }
    public int DaysBack   { get; init; }
    public IReadOnlyList<SearchAnalyticsRowDto> Items { get; init; } = [];
}

/// <summary>Single row in the full analytics table.</summary>
public sealed class SearchAnalyticsRowDto
{
    public string   Query            { get; init; } = string.Empty;
    public int      SearchCount      { get; init; }
    public int      ZeroResultCount  { get; init; }
    public decimal  AvgResultCount   { get; init; }
    public DateTime LastSearchedAt   { get; init; }
    public int      ClickCount       { get; init; }
    public decimal  ClickThroughRate { get; init; }
}
