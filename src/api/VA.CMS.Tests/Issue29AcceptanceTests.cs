using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #29:
/// Build content entry list screen (admin) — FR-AUTH-01.
///
/// AC:
/// - List shows: Title, Content Type, Author, Status, Last Modified (via API)
/// - Filters: Content Type, Status, Author search, Date Range
/// - Sortable by Title, Status, Last Modified
/// - Paginated (25/page)
/// - SP usp_ContentEntry_ListAdmin exists and returns expected columns
/// </summary>
[Collection("Database")]
public class Issue29AcceptanceTests(DatabaseFixture fixture)
{
    private IContentEntryRepository Repo() => new ContentEntryRepository(fixture.CreateDb());

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<(long EntryId, long UserId, long ContentTypeId)> SeedEntryWithVersionAsync(
        string slug = "test-entry",
        string status = "Draft",
        string titleFieldValue = "My Test Title",
        string contentTypeName = "list_test_type",
        string? authorDisplay = null)
    {
        var displayName = authorDisplay ?? $"Author {Guid.NewGuid():N}";
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString, displayName: displayName);
        var ctId   = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, contentTypeName);

        var uniqueSlug = $"{slug}-{Guid.NewGuid():N}";
        var entryId = await TestSeeder.CreateEntryAsync(fixture.ConnectionString, ctId, uniqueSlug, "en-US", userId);

        // Update status if not Draft
        if (status != "Draft")
        {
            await using var conn = new SqlConnection(fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "EXEC usp_ContentEntry_UpdateStatus @0, @1, @2";
            cmd.CommandText = "EXEC usp_ContentEntry_UpdateStatus @Id, @Status, @PubVerId";
            cmd.Parameters.AddWithValue("@Id", entryId);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.Add("@PubVerId", System.Data.SqlDbType.BigInt).Value = DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        // Create a content version with a title field in the JSON
        var fieldsJson = $"{{\"title\":\"{titleFieldValue}\",\"body\":\"Content body text.\"}}";
        await using var vConn = new SqlConnection(fixture.ConnectionString);
        await vConn.OpenAsync();
        await using var vCmd = vConn.CreateCommand();
        vCmd.CommandText = "EXEC usp_ContentVersion_Create @ContentEntryId, @FieldsJson, NULL, @Status, @AuthorId, NULL, @NewId OUTPUT";
        vCmd.Parameters.AddWithValue("@ContentEntryId", entryId);
        vCmd.Parameters.AddWithValue("@FieldsJson", fieldsJson);
        vCmd.Parameters.AddWithValue("@Status", status);
        vCmd.Parameters.AddWithValue("@AuthorId", userId);
        var outParam = vCmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await vCmd.ExecuteNonQueryAsync();

        return (entryId, userId, ctId);
    }

    // ── AC: SP returns required columns ──────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Returns_Required_Columns()
    {
        await SeedEntryWithVersionAsync(titleFieldValue: "My Required Column Title");

        var repo   = Repo();
        var result = await repo.ListAdminAsync(page: 1, pageSize: 100);

        Assert.NotNull(result);
        Assert.True(result.TotalRows >= 1);
        Assert.NotEmpty(result.Items);

        var row = result.Items.First();
        Assert.True(row.Id > 0,                    "Id must be positive");
        Assert.False(string.IsNullOrEmpty(row.Slug), "Slug must be populated");
        Assert.False(string.IsNullOrEmpty(row.Status), "Status must be populated");
        Assert.False(string.IsNullOrEmpty(row.ContentTypeName), "ContentTypeName must be populated");
        Assert.False(string.IsNullOrEmpty(row.AuthorDisplayName), "AuthorDisplayName must be populated");
        Assert.False(string.IsNullOrEmpty(row.Title), "Title must be populated");
        Assert.True(row.UpdatedAt > DateTime.MinValue, "UpdatedAt must be a real timestamp");
    }

    // ── AC: Title comes from FieldsJson of the latest version ─────────────────

    [Fact]
    public async Task ListAdmin_Title_Comes_From_FieldsJson()
    {
        var expected = $"Title-{Guid.NewGuid():N}";
        var (entryId, _, _) = await SeedEntryWithVersionAsync(titleFieldValue: expected);

        var repo   = Repo();
        var result = await repo.ListAdminAsync(page: 1, pageSize: 100);

        var row = result.Items.FirstOrDefault(r => r.Id == entryId);
        Assert.NotNull(row);
        Assert.Equal(expected, row!.Title);
    }

    // ── AC: Filter by ContentTypeId ───────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Filter_By_ContentTypeId()
    {
        var uniqueType = $"ct_filter_{Guid.NewGuid():N}";
        var (entryId, _, ctId) = await SeedEntryWithVersionAsync(contentTypeName: uniqueType);

        var repo   = Repo();
        var result = await repo.ListAdminAsync(contentTypeId: ctId, page: 1, pageSize: 100);

        Assert.True(result.TotalRows >= 1);
        Assert.All(result.Items, r => Assert.Equal(ctId, r.ContentTypeId));
    }

    // ── AC: Filter by Status ──────────────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Filter_By_Status_Draft()
    {
        await SeedEntryWithVersionAsync(status: "Draft");

        var repo   = Repo();
        var result = await repo.ListAdminAsync(status: "Draft", page: 1, pageSize: 100);

        Assert.True(result.TotalRows >= 1);
        Assert.All(result.Items, r => Assert.Equal("Draft", r.Status));
    }

    // ── AC: Filter by Author (search) ─────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Filter_By_AuthorSearch()
    {
        var uniqueName = $"UniqueAuthor-{Guid.NewGuid():N}";
        var (entryId, _, _) = await SeedEntryWithVersionAsync(authorDisplay: uniqueName);

        var repo   = Repo();
        // Search on a distinctive substring of the display name
        var result = await repo.ListAdminAsync(authorSearch: uniqueName.Substring(0, 20), page: 1, pageSize: 100);

        Assert.True(result.TotalRows >= 1);
        var row = result.Items.FirstOrDefault(r => r.Id == entryId);
        Assert.NotNull(row);
    }

    // ── AC: Archived entries excluded ─────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Excludes_Archived_Entries()
    {
        // Create a draft entry then archive it
        var (entryId, userId, _) = await SeedEntryWithVersionAsync();
        var repo = Repo();

        // Archive via SP
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_Archive @Id, @ActorId";
        cmd.Parameters.AddWithValue("@Id", entryId);
        cmd.Parameters.AddWithValue("@ActorId", userId);
        await cmd.ExecuteNonQueryAsync();

        var result = await repo.ListAdminAsync(page: 1, pageSize: 1000);

        Assert.DoesNotContain(result.Items, r => r.Id == entryId);
    }

    // ── AC: Pagination — TotalRows and TotalPages ─────────────────────────────

    [Fact]
    public async Task ListAdmin_Pagination_TotalRows_And_TotalPages()
    {
        // Seed enough entries to test pagination math
        var uniqueType = $"page_type_{Guid.NewGuid():N}";
        for (int i = 0; i < 3; i++)
            await SeedEntryWithVersionAsync(contentTypeName: uniqueType);

        var repo   = Repo();
        var result = await repo.ListAdminAsync(contentTypeId: null, page: 1, pageSize: 2);

        // Can't filter strictly by type here, but let's verify math is consistent
        Assert.True(result.TotalRows >= 3);
        Assert.True(result.TotalPages >= 1);
        Assert.True(result.TotalPages == (int)Math.Ceiling((double)result.TotalRows / result.PageSize));
    }

    // ── AC: Page size default is 25 ───────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Default_PageSize_Is_25()
    {
        var repo   = Repo();
        var result = await repo.ListAdminAsync();

        Assert.Equal(25, result.PageSize);
    }

    // ── AC: Sort by Title ASC ─────────────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Sort_By_Title_Asc_Returns_Results()
    {
        await SeedEntryWithVersionAsync(titleFieldValue: "Alpha Title");
        await SeedEntryWithVersionAsync(titleFieldValue: "Beta Title");

        var repo   = Repo();
        var result = await repo.ListAdminAsync(sortBy: "Title", sortDir: "ASC", page: 1, pageSize: 100);

        Assert.NotEmpty(result.Items);
        // The database sorts with its collation (word sort: hyphens ignored), which does
        // not agree with an ordinal comparison on every title other tests seed, so only
        // assert the relative order of the two titles this test owns.
        var titles = result.Items.Select(r => r.Title).ToList();
        var alpha  = titles.IndexOf("Alpha Title");
        var beta   = titles.IndexOf("Beta Title");
        Assert.True(alpha >= 0 && beta >= 0, "seeded titles missing from the sorted list");
        Assert.True(alpha < beta, $"Expected 'Alpha Title' (index {alpha}) before 'Beta Title' (index {beta}) in ASC sort");
    }

    // ── AC: Sort by Status ASC ────────────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_Sort_By_Status_Returns_Results()
    {
        var repo   = Repo();
        var result = await repo.ListAdminAsync(sortBy: "Status", sortDir: "ASC", page: 1, pageSize: 100);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
    }

    // ── AC: Sort by UpdatedAt DESC (default) ──────────────────────────────────

    [Fact]
    public async Task ListAdmin_Default_Sort_UpdatedAt_Desc()
    {
        await SeedEntryWithVersionAsync();

        var repo   = Repo();
        var result = await repo.ListAdminAsync(sortBy: "UpdatedAt", sortDir: "DESC", page: 1, pageSize: 100);

        Assert.NotEmpty(result.Items);
        var dates = result.Items.Select(r => r.UpdatedAt).ToList();
        for (int i = 1; i < dates.Count; i++)
            Assert.True(dates[i - 1] >= dates[i],
                $"Expected descending UpdatedAt: [{i - 1}]={dates[i-1]:O} should be >= [{i}]={dates[i]:O}");
    }

    // ── AC: Unrecognised sort falls back gracefully ────────────────────────────

    [Fact]
    public async Task ListAdmin_Invalid_SortBy_Fallback_Returns_Results()
    {
        var repo   = Repo();
        var result = await repo.ListAdminAsync(sortBy: "HackerField; DROP TABLE ContentEntry --", sortDir: "DESC");

        // SP sanitises the sort column; should return results without error
        Assert.NotNull(result);
    }

    // ── AC: Date range filter ──────────────────────────────────────────────────

    [Fact]
    public async Task ListAdmin_DateRange_Filter_Excludes_Old_Entries()
    {
        var repo        = Repo();
        var futureStart = DateTime.UtcNow.AddYears(100);
        var result      = await repo.ListAdminAsync(dateFrom: futureStart, page: 1, pageSize: 100);

        // No entries can have UpdatedAt in the year 2126
        Assert.Equal(0, result.TotalRows);
        Assert.Empty(result.Items);
    }
}
