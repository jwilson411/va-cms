using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #50 — Integrate USWDS Search component on public site.
///
/// BRD FR-SEARCH-01.
///
/// Acceptance criteria (API layer — public site integration):
///   AC1: GET /api/v1/search supports ?from= and ?to= date filter parameters without error.
///   AC2: GET /api/v1/search supports ?tag= taxonomy term filter parameter without error.
///   AC3: SearchController.Search signature includes from, to, and tag parameters.
///   AC4: Existing AC1–AC8 from issue #49 continue to pass (non-regression).
///   AC5: pageSize and page are echoed in response when using new filter parameters.
///
/// Test strategy:
///   - SearchController unit tests against a real DB (via DatabaseFixture, TestContainers SQL Server).
///   - V025 migration adds date/tag filter support to usp_Search_FullText.
///   - When FTS is not installed (typical TestContainers), SP returns 0 rows;
///     tests verify no exception and correct response shape — not that results exist.
/// </summary>
[Collection("Database")]
public class Issue50AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private ISearchRepository Repo() => new SearchRepository(fixture.CreateDb());

    private SearchController Controller() => new SearchController(Repo(), StaticSiteSettings.Defaults, new InlineSearchLogQueue(search: Repo()));

    // ── AC1: date range filters ────────────────────────────────────────────────

    /// <summary>
    /// AC1: GET /api/v1/search?q=&amp;from= returns 200 OK without error.
    /// </summary>
    [Fact]
    public async Task Search_WithFromDate_Returns200()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = await Controller().Search(q: "content", from: from);
        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// AC1: GET /api/v1/search?q=&amp;to= returns 200 OK without error.
    /// </summary>
    [Fact]
    public async Task Search_WithToDate_Returns200()
    {
        var to = DateTime.UtcNow;
        var result = await Controller().Search(q: "content", to: to);
        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// AC1: GET /api/v1/search?q=&amp;from=&amp;to= returns 200 OK without error.
    /// </summary>
    [Fact]
    public async Task Search_WithFromAndToDate_Returns200()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = DateTime.UtcNow;
        var result = await Controller().Search(q: "content", from: from, to: to);
        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// AC1: Response with date filter echoes query and has non-negative TotalItems.
    /// </summary>
    [Fact]
    public async Task Search_WithDateRange_EchoesQueryAndNonNegativeCount()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = await Controller().Search(q: "veterans", from: from);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal("veterans", response.Query);
        Assert.True(response.TotalItems >= 0);
        Assert.NotNull(response.Items);
    }

    // ── AC2: tag filter ────────────────────────────────────────────────────────

    /// <summary>
    /// AC2: GET /api/v1/search?q=&amp;tag= returns 200 OK without error.
    /// </summary>
    [Fact]
    public async Task Search_WithTagFilter_Returns200()
    {
        var result = await Controller().Search(q: "content", tag: 1L);
        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// AC2: Response with tag filter has correct shape (non-null Items, non-negative count).
    /// </summary>
    [Fact]
    public async Task Search_WithTagFilter_ResponseShapeIsCorrect()
    {
        var result = await Controller().Search(q: "veterans", tag: 999L);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.NotNull(response.Items);
        Assert.True(response.TotalItems >= 0);
    }

    // ── AC3: SearchController has new parameters ───────────────────────────────

    /// <summary>
    /// AC3: SearchController.Search method has 'from' parameter of type DateTime?.
    /// </summary>
    [Fact]
    public void SearchController_HasFromParameter()
    {
        var method = typeof(SearchController).GetMethod("Search");
        Assert.NotNull(method);
        var param = method!.GetParameters().FirstOrDefault(p => p.Name == "from");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateTime?), param!.ParameterType);
    }

    /// <summary>
    /// AC3: SearchController.Search method has 'to' parameter of type DateTime?.
    /// </summary>
    [Fact]
    public void SearchController_HasToParameter()
    {
        var method = typeof(SearchController).GetMethod("Search");
        Assert.NotNull(method);
        var param = method!.GetParameters().FirstOrDefault(p => p.Name == "to");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateTime?), param!.ParameterType);
    }

    /// <summary>
    /// AC3: SearchController.Search method has 'tag' parameter of type long?.
    /// </summary>
    [Fact]
    public void SearchController_HasTagParameter()
    {
        var method = typeof(SearchController).GetMethod("Search");
        Assert.NotNull(method);
        var param = method!.GetParameters().FirstOrDefault(p => p.Name == "tag");
        Assert.NotNull(param);
        Assert.Equal(typeof(long?), param!.ParameterType);
    }

    // ── AC4: non-regression — existing #49 acceptance tests ───────────────────

    /// <summary>
    /// AC4 (non-regression): SearchController is still AllowAnonymous.
    /// </summary>
    [Fact]
    public void SearchController_StillAllowAnonymous()
    {
        var controllerType = typeof(SearchController);
        var hasAllowAnonymous = controllerType
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true)
            .Any();
        Assert.True(hasAllowAnonymous);
    }

    /// <summary>
    /// AC4 (non-regression): Missing q= still returns 400.
    /// </summary>
    [Fact]
    public async Task Search_MissingQuery_StillReturns400()
    {
        var result = await Controller().Search(q: null);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>
    /// AC4 (non-regression): SearchResultDto still has all required fields.
    /// </summary>
    [Fact]
    public void SearchResultDto_StillHasAllRequiredFields()
    {
        var type = typeof(SearchResultDto);
        Assert.NotNull(type.GetProperty("Title"));
        Assert.NotNull(type.GetProperty("ContentTypeName"));
        Assert.NotNull(type.GetProperty("Slug"));
        Assert.NotNull(type.GetProperty("Excerpt"));
        Assert.NotNull(type.GetProperty("PublishedAt"));
        Assert.NotNull(type.GetProperty("Rank"));
    }

    // ── AC5: pagination echoed with filters ────────────────────────────────────

    /// <summary>
    /// AC5: Page and pageSize are echoed correctly when using new filter parameters.
    /// </summary>
    [Fact]
    public async Task Search_WithFilters_PaginationFieldsEchoed()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = await Controller().Search(q: "content", from: from, page: 2, pageSize: 10);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal(2, response.Page);
        Assert.Equal(10, response.PageSize);
    }

    /// <summary>
    /// AC5: pageSize is still clamped to 100 when filters are present.
    /// </summary>
    [Fact]
    public async Task Search_WithFilters_PageSizeClamped()
    {
        var result = await Controller().Search(q: "content", tag: 1L, pageSize: 999);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal(100, response.PageSize);
    }

    // ── V025 migration ────────────────────────────────────────────────────────

    /// <summary>
    /// V025 migration: usp_Search_FullText SP still exists after V025 update.
    /// </summary>
    [Fact]
    public async Task V025_UspSearchFullText_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM sys.objects
            WHERE [name] = 'usp_Search_FullText'
              AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }
}
