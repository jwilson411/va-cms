using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #49 — Configure SQL Server Full-Text Search on published content.
///
/// BRD FR-SEARCH-02.
///
/// Acceptance criteria:
///   AC1: FTS catalog and indexes created by migration (V002 — guarded when FTS not installed).
///   AC2: Computed column fn_ExtractPlainText extracts plain text from ContentVersion.FieldsJson.
///   AC3: GET /api/v1/search?q= returns results ordered by rank (200 OK; empty list when FTS absent).
///   AC4: Results include: title, content type, slug, summary excerpt, published date.
///   AC5: GET /api/v1/search with no q= returns 400 Bad Request.
///   AC6: SearchController is decorated with [AllowAnonymous] — no JWT required for public search.
///   AC7: Every search query is logged via usp_Search_LogQuery (SearchRepository.LogQueryAsync).
///   AC8: pageSize is clamped to [1, 100] and page defaults to 1.
///
/// Test strategy:
///   - SearchController unit tests against a real DB (via DatabaseFixture, TestContainers SQL Server).
///   - V024 migration is included in the test DB so the enhanced SP is available.
///   - When FTS is not installed (typical TestContainers SQL Server image), SP returns 0 rows;
///     tests verify no exception and correct response shape — NOT that results exist.
///   - Structural tests (attribute checks, DTO shape) do not require FTS to be installed.
/// </summary>
[Collection("Database")]
public class Issue49AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private ISearchRepository Repo() => new SearchRepository(fixture.CreateDb());

    private SearchController Controller() => new SearchController(Repo(), StaticSiteSettings.Defaults);

    // ── AC1: V002 / V024 migrations ran without error ─────────────────────────

    /// <summary>
    /// AC1: The fn_ExtractPlainText UDF exists in the database (always created by V002, no FTS needed).
    /// </summary>
    [Fact]
    public async Task FnExtractPlainText_UdfExists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM sys.objects
            WHERE [name] = 'fn_ExtractPlainText'
              AND [type] = 'FN';";
        var count = (int)await cmd.ExecuteScalarAsync()!;
        Assert.Equal(1, count);
    }

    /// <summary>
    /// AC1: usp_Search_FullText stored procedure exists in the database (created by V008, updated by V024).
    /// </summary>
    [Fact]
    public async Task UspSearchFullText_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM sys.objects
            WHERE [name] = 'usp_Search_FullText'
              AND [type] = 'P';";
        var count = (int)await cmd.ExecuteScalarAsync()!;
        Assert.Equal(1, count);
    }

    /// <summary>
    /// AC1: usp_Search_LogQuery stored procedure exists in the database.
    /// </summary>
    [Fact]
    public async Task UspSearchLogQuery_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM sys.objects
            WHERE [name] = 'usp_Search_LogQuery'
              AND [type] = 'P';";
        var count = (int)await cmd.ExecuteScalarAsync()!;
        Assert.Equal(1, count);
    }

    // ── AC2: computed column and UDF ──────────────────────────────────────────

    /// <summary>
    /// AC2: fn_ExtractPlainText strips JSON delimiters and returns plain text.
    /// This test exercises the UDF directly without FTS.
    /// </summary>
    [Fact]
    public async Task FnExtractPlainText_StripsJsonDelimiters()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // A typical FieldsJson value
        cmd.CommandText = "SELECT dbo.fn_ExtractPlainText('{\"title\":\"Hello World\",\"body\":\"Some content here.\"}');";
        var result = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
        // The UDF replaces { } [ ] " : , with spaces — the word "Hello" must appear
        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
        // JSON syntax chars must be replaced
        Assert.DoesNotContain("{", result);
        Assert.DoesNotContain("\"", result);
    }

    // ── AC3: GET /api/v1/search?q= returns 200 ────────────────────────────────

    /// <summary>
    /// AC3: Search endpoint returns 200 OK for a valid query.
    /// When FTS is not installed, the result list is empty but the response shape is correct.
    /// </summary>
    [Fact]
    public async Task Search_ValidQuery_Returns200()
    {
        var result = await Controller().Search(q: "content", type: null, page: 1, pageSize: 10);
        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// AC3: Response body is a SearchResponse with the correct query echoed back.
    /// </summary>
    [Fact]
    public async Task Search_ValidQuery_ReturnsSearchResponseWithQueryEchoed()
    {
        var result   = await Controller().Search(q: "content");
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal("content", response.Query);
    }

    /// <summary>
    /// AC3: TotalItems is non-negative and Items list is non-null.
    /// </summary>
    [Fact]
    public async Task Search_ValidQuery_ItemsListIsNonNull()
    {
        var result   = await Controller().Search(q: "VA benefits");
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.NotNull(response.Items);
        Assert.True(response.TotalItems >= 0);
    }

    /// <summary>
    /// AC3: Results are paginated — PageSize and Page are echoed in the response.
    /// </summary>
    [Fact]
    public async Task Search_ValidQuery_PaginationFieldsEchoed()
    {
        var result   = await Controller().Search(q: "content", page: 2, pageSize: 5);
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal(2, response.Page);
        Assert.Equal(5, response.PageSize);
    }

    // ── AC4: result shape includes required fields ────────────────────────────

    /// <summary>
    /// AC4: SearchResultDto exposes all required fields for issue #49 acceptance.
    /// This test verifies the DTO properties exist at compile-time via reflection.
    /// </summary>
    [Fact]
    public void SearchResultDto_HasAllRequiredFields()
    {
        var type = typeof(SearchResultDto);

        // Title
        Assert.NotNull(type.GetProperty("Title"));
        // Content type (name, not just id)
        Assert.NotNull(type.GetProperty("ContentTypeName"));
        // Slug
        Assert.NotNull(type.GetProperty("Slug"));
        // Summary excerpt
        Assert.NotNull(type.GetProperty("Excerpt"));
        // Published date
        Assert.NotNull(type.GetProperty("PublishedAt"));
    }

    /// <summary>
    /// AC4: SearchResult POCO includes Title, ContentTypeName, Excerpt, PublishedAt.
    /// These are populated by usp_Search_FullText (V024) and mapped in SearchRepository.
    /// </summary>
    [Fact]
    public void SearchResultPoco_HasV024Fields()
    {
        var type = typeof(VA.CMS.Infrastructure.Data.Pocos.SearchResult);
        Assert.NotNull(type.GetProperty("Title"));
        Assert.NotNull(type.GetProperty("ContentTypeName"));
        Assert.NotNull(type.GetProperty("PublishedAt"));
        Assert.NotNull(type.GetProperty("Excerpt"));
    }

    // ── AC5: missing q= returns 400 ──────────────────────────────────────────

    /// <summary>
    /// AC5: GET /api/v1/search with no q parameter returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Search_MissingQuery_Returns400()
    {
        var result = await Controller().Search(q: null);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>
    /// AC5: GET /api/v1/search?q= (empty string) returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Search_EmptyQuery_Returns400()
    {
        var result = await Controller().Search(q: "   ");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── AC6: public endpoint — no JWT required ────────────────────────────────

    /// <summary>
    /// AC6: SearchController is decorated with [AllowAnonymous] — public search requires no JWT.
    /// </summary>
    [Fact]
    public void SearchController_IsAllowAnonymous()
    {
        var controllerType = typeof(SearchController);
        var hasAllowAnonymous = controllerType
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true)
            .Any();
        Assert.True(hasAllowAnonymous,
            "SearchController must be decorated with [AllowAnonymous] — public search needs no JWT.");
    }

    // ── AC7: query logging works ──────────────────────────────────────────────

    /// <summary>
    /// AC7: LogQueryAsync writes a row to SearchQueryLog without throwing.
    /// Verifies the analytics sink is wired up for zero-result tracking.
    /// </summary>
    [Fact]
    public async Task LogQueryAsync_DoesNotThrow_AndRowAppears()
    {
        var uniqueQuery = $"issue49-acceptance-{Guid.NewGuid():N}";
        var repo = Repo();

        // Should not throw
        await repo.LogQueryAsync(uniqueQuery, 0);

        // Verify row was inserted
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM [dbo].[SearchQueryLog] WHERE [Query] = @q;";
        cmd.Parameters.AddWithValue("@q", uniqueQuery);
        var count = (int)await cmd.ExecuteScalarAsync()!;
        Assert.Equal(1, count);
    }

    // ── AC8: pageSize clamping ────────────────────────────────────────────────

    /// <summary>
    /// AC8: pageSize > 100 is clamped to 100 in the response.
    /// </summary>
    [Fact]
    public async Task Search_PageSizeOver100_IsClamped()
    {
        var result   = await Controller().Search(q: "content", pageSize: 999);
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal(100, response.PageSize);
    }

    /// <summary>
    /// AC8: pageSize of 0 or negative is clamped to 1.
    /// </summary>
    [Fact]
    public async Task Search_PageSizeZero_IsClampedToOne()
    {
        var result   = await Controller().Search(q: "content", pageSize: 0);
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal(1, response.PageSize);
    }

    /// <summary>
    /// AC8: negative page is normalized to 1.
    /// </summary>
    [Fact]
    public async Task Search_NegativePage_IsNormalizedToOne()
    {
        var result   = await Controller().Search(q: "content", page: -5);
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchResponse>(ok.Value);
        Assert.Equal(1, response.Page);
    }
}
