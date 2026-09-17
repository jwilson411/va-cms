using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Services;

namespace VA.CMS.Tests;

/// <summary>
/// Integration tests for DemoSeedService.
/// Runs against a real SQL Server TestContainers instance with all DbUp migrations applied.
/// </summary>
[Collection("Database")]
public class DemoSeedServiceTests(DatabaseFixture fixture)
{
    private DemoSeedService CreateService() =>
        new DemoSeedService(fixture.CreateDb());

    // -----------------------------------------------------------------------
    // SeedDemoAsync — basic seeding
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SeedDemoAsync_CreatesContentTypes()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();

        var types = await GetContentTypesAsync();
        Assert.Contains(types, t => t == "standard_page");
        Assert.Contains(types, t => t == "news_article");
    }

    [Fact]
    public async Task SeedDemoAsync_CreatesFiveStandardPages()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();

        var stdPages = await CountEntriesByContentTypeAsync("standard_page");
        Assert.Equal(5, stdPages);
    }

    [Fact]
    public async Task SeedDemoAsync_CreatesThreeNewsArticles()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();

        var newsArticles = await CountEntriesByContentTypeAsync("news_article");
        Assert.Equal(3, newsArticles);
    }

    [Fact]
    public async Task SeedDemoAsync_CreatesTwoTaxonomyTerms()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();

        var termCount = await CountTermsByTaxonomyAsync("topics");
        Assert.Equal(2, termCount);
    }

    [Fact]
    public async Task SeedDemoAsync_CreatesDemoUserPerRole()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();

        // Built-in roles: SuperAdmin, Admin, Editor, ContentOwner, Reviewer, ReadOnly = 6
        var demoUsers = await CountDemoUsersAsync();
        Assert.Equal(6, demoUsers);
    }

    // -----------------------------------------------------------------------
    // Idempotency — calling SeedDemoAsync twice must not duplicate rows
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SeedDemoAsync_IsIdempotent_ContentTypes()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();
        await svc.SeedDemoAsync(); // second call must be a no-op

        var types = await GetContentTypesAsync();
        Assert.Equal(1, types.Count(t => t == "standard_page"));
        Assert.Equal(1, types.Count(t => t == "news_article"));
    }

    [Fact]
    public async Task SeedDemoAsync_IsIdempotent_Pages()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();
        await svc.SeedDemoAsync();

        var stdPages = await CountEntriesByContentTypeAsync("standard_page");
        Assert.Equal(5, stdPages);
    }

    [Fact]
    public async Task SeedDemoAsync_IsIdempotent_TaxonomyTerms()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();
        await svc.SeedDemoAsync();

        var termCount = await CountTermsByTaxonomyAsync("topics");
        Assert.Equal(2, termCount);
    }

    [Fact]
    public async Task SeedDemoAsync_IsIdempotent_Users()
    {
        var svc = CreateService();
        await svc.SeedDemoAsync();
        await svc.SeedDemoAsync();

        var demoUsers = await CountDemoUsersAsync();
        Assert.Equal(6, demoUsers);
    }

    // -----------------------------------------------------------------------
    // ResetAndSeedDemoAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResetAndSeedDemoAsync_DropsAndRecreatesContent()
    {
        var svc = CreateService();

        // Seed first
        await svc.SeedDemoAsync();
        var beforePages = await CountEntriesByContentTypeAsync("standard_page");
        Assert.Equal(5, beforePages);

        // Reset
        await svc.ResetAndSeedDemoAsync();

        // After reset+re-seed counts must be exactly the same
        var afterPages = await CountEntriesByContentTypeAsync("standard_page");
        Assert.Equal(5, afterPages);

        var afterNews = await CountEntriesByContentTypeAsync("news_article");
        Assert.Equal(3, afterNews);

        var afterUsers = await CountDemoUsersAsync();
        Assert.Equal(6, afterUsers);
    }

    [Fact]
    public async Task ResetAndSeedDemoAsync_IsIdempotent_AfterDoubleReset()
    {
        var svc = CreateService();
        await svc.ResetAndSeedDemoAsync();
        await svc.ResetAndSeedDemoAsync();

        var pages = await CountEntriesByContentTypeAsync("standard_page");
        Assert.Equal(5, pages);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<IList<string>> GetContentTypesAsync()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [Name] FROM [dbo].[ContentType] WHERE [Name] IN ('standard_page','news_article')";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private async Task<int> CountEntriesByContentTypeAsync(string contentTypeName)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM   [dbo].[ContentEntry] e
            JOIN   [dbo].[ContentType]  t ON t.Id = e.ContentTypeId
            WHERE  t.[Name] = @TypeName
              AND  e.[Slug] LIKE 'demo/%'";
        cmd.Parameters.AddWithValue("@TypeName", contentTypeName);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountTermsByTaxonomyAsync(string handle)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM   [dbo].[TaxonomyTerm] tt
            JOIN   [dbo].[Taxonomy]     tx ON tx.Id = tt.TaxonomyId
            WHERE  tx.[Handle] = @Handle";
        cmd.Parameters.AddWithValue("@Handle", handle);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountDemoUsersAsync()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // One demo user per *system* role. Other test classes add throw-away roles to the
        // shared database (StoredProcedureTests), which the seed also picks up, so count
        // only the users that correspond to the six built-in roles.
        cmd.CommandText = @"SELECT COUNT(*) FROM [dbo].[User] u
                            WHERE u.[ExternalId] LIKE 'demo-%'
                              AND EXISTS (SELECT 1 FROM [dbo].[Role] r
                                          WHERE r.[IsSystemRole] = 1
                                            AND u.[ExternalId] = 'demo-' + LOWER(r.[Name]))";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
