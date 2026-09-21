using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #51 — Build search analytics dashboard widget in admin.
///
/// BRD FR-SEARCH-06.
///
/// Acceptance criteria:
///   AC1: Admin dashboard widget shows top 10 queries and top 10 zero-result queries (last 30 days).
///       => GET /api/v1/admin/search/analytics/summary returns SearchAnalyticsSummaryDto with both lists.
///   AC2: Admin page /admin/search/analytics shows full analytics table.
///       => GET /api/v1/admin/search/analytics returns paginated SearchAnalyticsPageDto.
///   AC3: Click-through rate is tracked when a user clicks a search result.
///       => POST /api/v1/search/click inserts a row in SearchResultClick.
///
/// Migration: V026 adds SearchResultClick table + 4 SPs.
///
/// Test strategy:
///   - SearchAnalyticsController unit tests against real DB (via DatabaseFixture/TestContainers).
///   - Seed SearchQueryLog rows and verify SP results are correctly mapped.
///   - Click-through: POST inserts a row, then raw SQL confirms.
///   - Structural tests (DTO shape) don't require rows in the DB.
/// </summary>
[Collection("Database")]
public class Issue51AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private ISearchAnalyticsRepository Repo() => new SearchAnalyticsRepository(fixture.CreateDb());

    private SearchAnalyticsController Controller() => new SearchAnalyticsController(Repo(), StaticSiteSettings.Defaults, new InlineSearchLogQueue(analytics: Repo()), new AnySlugPublishedStub());

    // ── AC1a: V026 migration — SPs and table exist ────────────────────────────

    [Fact]
    public async Task V026_SearchResultClickTable_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1) FROM sys.tables WHERE [name] = 'SearchResultClick';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V026_UspSearchGetTopQueries_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_Search_GetTopQueries' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V026_UspSearchGetZeroResultQueries_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_Search_GetZeroResultQueries' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V026_UspSearchGetAnalyticsFull_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_Search_GetAnalyticsFull' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V026_UspSearchLogClick_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_Search_LogClick' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    // ── AC1b: summary endpoint returns correct structure ──────────────────────

    [Fact]
    public async Task GetSummary_Returns200Ok()
    {
        var result = await Controller().GetSummary();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetSummary_ReturnsBothLists()
    {
        var result   = await Controller().GetSummary();
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchAnalyticsSummaryDto>(ok.Value);
        Assert.NotNull(response.TopQueries);
        Assert.NotNull(response.ZeroResultQueries);
    }

    [Fact]
    public async Task GetSummary_TopQueriesListHasAtMostTenItems()
    {
        // Seed more than 10 distinct queries with non-zero results
        await SeedQueryLogsAsync(count: 15, resultCount: 1, querySuffix: "top-q-limit");
        var result   = await Controller().GetSummary();
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchAnalyticsSummaryDto>(ok.Value);
        Assert.True(response.TopQueries.Count <= 10,
            $"TopQueries should have at most 10 items; got {response.TopQueries.Count}");
    }

    [Fact]
    public async Task GetSummary_ZeroResultQueriesListHasAtMostTenItems()
    {
        // Seed more than 10 distinct zero-result queries
        await SeedQueryLogsAsync(count: 15, resultCount: 0, querySuffix: "zero-q-limit");
        var result   = await Controller().GetSummary();
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchAnalyticsSummaryDto>(ok.Value);
        Assert.True(response.ZeroResultQueries.Count <= 10,
            $"ZeroResultQueries should have at most 10 items; got {response.ZeroResultQueries.Count}");
    }

    // ── AC1c: seeded queries appear in the analytics response ─────────────────

    [Fact]
    public async Task GetSummary_SeededQuery_AppearsInTopQueries()
    {
        var uniqueQuery = SearchTestText.Unique("ac1-top");
        await InsertQueryLogAsync(uniqueQuery, resultCount: 5);
        await InsertQueryLogAsync(uniqueQuery, resultCount: 3);

        var repo   = Repo();
        var result = await repo.GetTopQueriesAsync(topN: 200, daysBack: 30);

        var match = result.FirstOrDefault(r => r.Query == uniqueQuery);
        Assert.NotNull(match);
        Assert.Equal(2, match.SearchCount);
    }

    [Fact]
    public async Task GetSummary_SeededZeroResultQuery_AppearsInZeroResultList()
    {
        var uniqueQuery = SearchTestText.Unique("ac1-zero");
        await InsertQueryLogAsync(uniqueQuery, resultCount: 0);
        await InsertQueryLogAsync(uniqueQuery, resultCount: 0);

        var repo   = Repo();
        var result = await repo.GetZeroResultQueriesAsync(topN: 200, daysBack: 30);

        var match = result.FirstOrDefault(r => r.Query == uniqueQuery);
        Assert.NotNull(match);
        Assert.Equal(2, match.ZeroResultCount);
    }

    // ── AC2: full analytics page ──────────────────────────────────────────────

    [Fact]
    public async Task GetFull_Returns200Ok()
    {
        var result = await Controller().GetFull();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetFull_ResponseShapeIsCorrect()
    {
        var result   = await Controller().GetFull();
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchAnalyticsPageDto>(ok.Value);
        Assert.NotNull(response.Items);
        Assert.True(response.TotalRows >= 0);
        Assert.True(response.Page >= 1);
        Assert.True(response.PageSize >= 1);
    }

    [Fact]
    public async Task GetFull_PageSizeIsClamped()
    {
        var result   = await Controller().GetFull(pageSize: 9999);
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchAnalyticsPageDto>(ok.Value);
        Assert.Equal(200, response.PageSize);
    }

    [Fact]
    public async Task GetFull_SeededQuery_AppearsInFullTable()
    {
        var uniqueQuery = SearchTestText.Unique("ac2-full");
        await InsertQueryLogAsync(uniqueQuery, resultCount: 7);

        var repo   = Repo();
        var (items, _) = await repo.GetFullAnalyticsAsync(daysBack: 30, page: 1, pageSize: 500);

        var match = items.FirstOrDefault(r => r.Query == uniqueQuery);
        Assert.NotNull(match);
        Assert.Equal(1, match.SearchCount);
        Assert.Equal(0, match.ZeroResultCount);
    }

    [Fact]
    public void GetFull_IncludesCtrField()
    {
        var type = typeof(SearchAnalyticsRowDto);
        Assert.NotNull(type.GetProperty("ClickThroughRate"));
        Assert.NotNull(type.GetProperty("ClickCount"));
    }

    // ── AC4: server-side sorting and filtering (V050) ──────────────────────────

    [Fact]
    public async Task GetFull_SortByQueryAscThenDesc_ReversesOrder()
    {
        var tag = SearchTestText.Unique("sort");
        var queryA = $"aaa-{tag}";
        var queryZ = $"zzz-{tag}";
        await InsertQueryLogAsync(queryA, resultCount: 1);
        await InsertQueryLogAsync(queryZ, resultCount: 1);

        var repo = Repo();
        var (asc, _)  = await repo.GetFullAnalyticsAsync(daysBack: 30, page: 1, pageSize: 500, sortBy: "Query", sortDir: "ASC");
        var (desc, _) = await repo.GetFullAnalyticsAsync(daysBack: 30, page: 1, pageSize: 500, sortBy: "Query", sortDir: "DESC");

        var ascIndexA  = asc.ToList().FindIndex(r => r.Query == queryA);
        var ascIndexZ  = asc.ToList().FindIndex(r => r.Query == queryZ);
        var descIndexA = desc.ToList().FindIndex(r => r.Query == queryA);
        var descIndexZ = desc.ToList().FindIndex(r => r.Query == queryZ);

        Assert.True(ascIndexA < ascIndexZ, "ASC by Query should put 'aaa-...' before 'zzz-...'");
        Assert.True(descIndexZ < descIndexA, "DESC by Query should put 'zzz-...' before 'aaa-...'");
    }

    [Fact]
    public async Task GetFull_SortByZeroResultCount_OrdersByZeroResultCount()
    {
        var tag = SearchTestText.Unique("sort-zero");
        var lowQuery  = $"low-{tag}";
        var highQuery = $"high-{tag}";
        await InsertQueryLogAsync(lowQuery, resultCount: 5);   // 0 zero-results
        await InsertQueryLogAsync(highQuery, resultCount: 0);
        await InsertQueryLogAsync(highQuery, resultCount: 0);  // 2 zero-results

        var repo = Repo();
        var (items, _) = await repo.GetFullAnalyticsAsync(
            daysBack: 30, page: 1, pageSize: 500, sortBy: "ZeroResultCount", sortDir: "DESC");

        var list = items.ToList();
        var highIndex = list.FindIndex(r => r.Query == highQuery);
        var lowIndex  = list.FindIndex(r => r.Query == lowQuery);
        Assert.True(highIndex < lowIndex, "DESC by ZeroResultCount should rank the higher count first");
    }

    [Fact]
    public async Task GetFull_UnrecognizedSortBy_FallsBackWithoutError()
    {
        var repo = Repo();
        var (items, totalRows) = await repo.GetFullAnalyticsAsync(
            daysBack: 30, page: 1, pageSize: 10, sortBy: "'; DROP TABLE SearchQueryLog; --", sortDir: "DESC");

        Assert.NotNull(items);
        Assert.True(totalRows >= 0);
    }

    [Fact]
    public async Task GetFull_QueryFilter_MatchesOnlySubstring()
    {
        var tag = SearchTestText.Unique("filter");
        var matching    = $"matching-{tag}";
        var nonMatching = SearchTestText.Unique("other");
        await InsertQueryLogAsync(matching, resultCount: 1);
        await InsertQueryLogAsync(nonMatching, resultCount: 1);

        var repo = Repo();
        var (items, totalRows) = await repo.GetFullAnalyticsAsync(
            daysBack: 30, page: 1, pageSize: 500, queryFilter: tag);

        var list = items.ToList();
        Assert.Contains(list, r => r.Query == matching);
        Assert.DoesNotContain(list, r => r.Query == nonMatching);
        Assert.True(totalRows >= 1);
    }

    [Fact]
    public async Task GetFull_Controller_PassesSortAndFilterThrough()
    {
        var tag = SearchTestText.Unique("ctrl-filter");
        var matching = $"ctrl-match-{tag}";
        await InsertQueryLogAsync(matching, resultCount: 1);

        var result   = await Controller().GetFull(daysBack: 30, page: 1, pageSize: 500, sortBy: "Query", sortDir: "ASC", q: tag);
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchAnalyticsPageDto>(ok.Value);

        Assert.Contains(response.Items, r => r.Query == matching);
    }

    // ── AC3: click-through tracking ───────────────────────────────────────────

    [Fact]
    public async Task LogClick_Returns204NoContent()
    {
        var result = await Controller().LogClick(new LogClickRequest
        {
            Query       = "va benefits",
            ClickedSlug = "benefits/overview",
            ResultRank  = 1,
        });
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task LogClick_InsertsRowInSearchResultClick()
    {
        var uniqueQuery = SearchTestText.Unique("ac3-click");
        var uniqueSlug  = $"slug-{Guid.NewGuid():N}";

        await Controller().LogClick(new LogClickRequest
        {
            Query       = uniqueQuery,
            ClickedSlug = uniqueSlug,
            ResultRank  = 2,
        });

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1) FROM [dbo].[SearchResultClick]
            WHERE [Query] = @q AND [ClickedSlug] = @s;";
        cmd.Parameters.AddWithValue("@q", uniqueQuery);
        cmd.Parameters.AddWithValue("@s", uniqueSlug);
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LogClick_MissingQuery_Returns400()
    {
        var result = await Controller().LogClick(new LogClickRequest
        {
            Query       = "   ",
            ClickedSlug = "some/slug",
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task LogClick_MissingSlug_Returns400()
    {
        var result = await Controller().LogClick(new LogClickRequest
        {
            Query       = "some query",
            ClickedSlug = "",
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task LogClickAsync_Repo_DoesNotThrow()
    {
        var repo = Repo();
        await repo.LogClickAsync("va pension", "pension/rates", resultRank: 0);
        // No exception = pass
    }

    // ── AC3b: clicks appear in analytics CTR ─────────────────────────────────

    [Fact]
    public async Task GetFull_AfterClick_CtrIsNonZero()
    {
        var uniqueQuery = SearchTestText.Unique("ac3-ctr");
        var uniqueSlug  = $"slug-{Guid.NewGuid():N}";

        // 2 searches, 1 click → CTR = 50%
        await InsertQueryLogAsync(uniqueQuery, resultCount: 5);
        await InsertQueryLogAsync(uniqueQuery, resultCount: 3);

        var repo = Repo();
        await repo.LogClickAsync(uniqueQuery, uniqueSlug, resultRank: 1);

        var (items, _) = await repo.GetFullAnalyticsAsync(daysBack: 30, page: 1, pageSize: 500);
        var match = items.FirstOrDefault(r => r.Query == uniqueQuery);
        Assert.NotNull(match);
        Assert.Equal(1, match.ClickCount);
        Assert.True(match.ClickThroughRate > 0, "CTR should be > 0 after a click is logged");
    }

    // ── DTO shape: required properties exist ─────────────────────────────────

    [Fact]
    public void SearchAnalyticsSummaryDto_HasRequiredProperties()
    {
        var type = typeof(SearchAnalyticsSummaryDto);
        Assert.NotNull(type.GetProperty("TopQueries"));
        Assert.NotNull(type.GetProperty("ZeroResultQueries"));
    }

    [Fact]
    public void TopQueryDto_HasRequiredProperties()
    {
        var type = typeof(TopQueryDto);
        Assert.NotNull(type.GetProperty("Query"));
        Assert.NotNull(type.GetProperty("SearchCount"));
        Assert.NotNull(type.GetProperty("ZeroResultCount"));
        Assert.NotNull(type.GetProperty("LastSearchedAt"));
    }

    [Fact]
    public void ZeroResultQueryDto_HasRequiredProperties()
    {
        var type = typeof(ZeroResultQueryDto);
        Assert.NotNull(type.GetProperty("Query"));
        Assert.NotNull(type.GetProperty("ZeroResultCount"));
        Assert.NotNull(type.GetProperty("LastSearchedAt"));
    }

    [Fact]
    public void LogClickRequest_HasRequiredProperties()
    {
        var type = typeof(LogClickRequest);
        Assert.NotNull(type.GetProperty("Query"));
        Assert.NotNull(type.GetProperty("ClickedSlug"));
        Assert.NotNull(type.GetProperty("ResultRank"));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task InsertQueryLogAsync(string query, int resultCount)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Search_LogQuery @Query, @ResultCount, @UserId";
        cmd.Parameters.AddWithValue("@Query", query);
        cmd.Parameters.AddWithValue("@ResultCount", resultCount);
        cmd.Parameters.AddWithValue("@UserId", DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedQueryLogsAsync(int count, int resultCount, string querySuffix)
    {
        for (var i = 0; i < count; i++)
            await InsertQueryLogAsync($"seed-{i:D3}-{querySuffix}", resultCount);
    }
}
