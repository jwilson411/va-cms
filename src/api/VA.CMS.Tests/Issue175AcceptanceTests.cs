using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VA.CMS.API.Auth;
using VA.CMS.API.Search;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Search;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #175 (epic #152): search analytics is not a PII store.
///   - Query text is redacted before it is queued: SSN, 9-digit runs, VA file numbers, phone, e-mail.
///   - Patterns come from search.analytics.redactionPatterns; a bad pattern is skipped, not fatal.
///   - Analytics read endpoints need CanManageSite; CanRead roles get 403.
///   - Raw rows carry no IP address or session key.
///   - usp_Maint_RollupSearchLogs honours search.analytics.retentionDays and is idempotent.
///   - vacms_readonly cannot SELECT the raw tables; the aggregated summary stays readable.
/// </summary>
public class Issue175AcceptanceTests
{
    // ── redaction ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("123-45-6789",                          "[redacted]")]
    [InlineData("123 45 6789",                          "[redacted]")]
    [InlineData("123456789",                            "[redacted]")]
    [InlineData("my ssn is 123-45-6789 please help",    "my ssn is [redacted] please help")]
    [InlineData("claim C12345678 status",               "claim [redacted] status")]
    [InlineData("file number css 1234567",              "file number [redacted]")]
    [InlineData("call me at (555) 123-4567",            "call me at [redacted]")]
    [InlineData("555-123-4567",                         "[redacted]")]
    [InlineData("+1 555.123.4567 ext",                  "[redacted] ext")]
    [InlineData("john.doe+va@example.com benefits",     "[redacted] benefits")]
    [InlineData("JOHN.DOE@VA.GOV",                      "[redacted]")]
    [InlineData("two 123-45-6789 and 987654321",        "two [redacted] and [redacted]")]
    public void Default_Patterns_Redact_Identifiers(string query, string expected)
    {
        var redactor = new SearchQueryRedactor(StaticSiteSettings.Defaults);
        Assert.Equal(expected, redactor.Redact(query));
    }

    [Theory]
    [InlineData("disability rating")]
    [InlineData("form 21-526EZ")]
    [InlineData("chapter 35 benefits 2026")]
    [InlineData("gi bill 2024")]
    [InlineData("va hospital 10 miles")]
    [InlineData("covid-19 vaccine")]
    [InlineData("38 CFR 3.159")]
    public void Ordinary_Queries_Pass_Through_Unchanged(string query)
    {
        var redactor = new SearchQueryRedactor(StaticSiteSettings.Defaults);
        Assert.Equal(query, redactor.Redact(query));
    }

    [Fact]
    public void Patterns_Come_From_The_Setting_And_A_Bad_One_Is_Skipped()
    {
        var settings = StaticSiteSettings.Defaults.WithJson(SiteSettingKeys.SearchAnalyticsRedactionPatterns,
            new[] { "(unbalanced", @"\bsecret\b" });
        var redactor = new SearchQueryRedactor(settings, NullLogger<SearchQueryRedactor>.Instance);

        Assert.Equal("the [redacted] word", redactor.Redact("the SECRET word"));
        Assert.Equal("123-45-6789", redactor.Redact("123-45-6789"));   // the defaults are gone: the operator owns the list
        Assert.Equal([@"\bsecret\b"], redactor.ActivePatterns);
    }

    [Fact]
    public void An_Empty_Pattern_List_Stores_Queries_As_Typed()
    {
        var settings = StaticSiteSettings.Defaults.WithJson(SiteSettingKeys.SearchAnalyticsRedactionPatterns, Array.Empty<string>());
        Assert.Equal("123-45-6789", new SearchQueryRedactor(settings).Redact("123-45-6789"));
    }

    [Fact]
    public void Queue_Redacts_Before_Buffering_So_The_Writer_Never_Sees_The_Raw_Text()
    {
        var settings = StaticSiteSettings.Defaults;
        var queue = new SearchLogQueue(settings, new SearchQueryRedactor(settings), NullLogger<SearchLogQueue>.Instance);

        Assert.True(queue.TryEnqueue(new SearchQueryLogItem("ssn 123-45-6789", 0, null)));
        Assert.True(queue.TryEnqueue(new SearchClickLogItem("bob@example.com", "hr/test", 1, null)));

        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal("ssn [redacted]", ((SearchQueryLogItem)first!).Query);
        Assert.True(queue.Reader.TryRead(out var second));
        Assert.Equal("[redacted]", ((SearchClickLogItem)second!).Query);
    }

    [Fact]
    public async Task Search_And_Click_Rows_Reach_The_Repositories_Redacted()
    {
        await using var factory = new PiiFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=" + Uri.EscapeDataString("status of 123-45-6789"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/search/click",
            new { query = "call 555-123-4567", clickedSlug = "hr/test", resultRank = 1 })).StatusCode);

        var queue = factory.Services.GetRequiredService<ISearchLogQueue>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (queue.Pending > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Equal(["status of [redacted]"], factory.Search.Queries.Select(q => q.Query).ToArray());
        Assert.Equal(["call [redacted]"],      factory.Analytics.Clicks.Select(c => c.Query).ToArray());
    }

    // ── settings ──────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_Are_Declared_With_Safe_Defaults()
    {
        var patterns = SiteSettingDefinitions.Get(SiteSettingKeys.SearchAnalyticsRedactionPatterns);
        Assert.Equal(SiteSettingType.Json, patterns.Type);
        Assert.Equal(SiteSettingScope.Server, patterns.Scope);
        Assert.Equal(SiteSettingDefinitions.DefaultSearchRedactionPatterns, StaticSiteSettings.Defaults.GetStringList(patterns.Key));

        var retention = SiteSettingDefinitions.Get(SiteSettingKeys.SearchAnalyticsRetentionDays);
        Assert.Equal(SiteSettingType.Int, retention.Type);
        Assert.Equal(90, StaticSiteSettings.Defaults.GetInt(retention.Key));
    }

    // ── authorization ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/admin/search/analytics/summary")]
    [InlineData("/api/v1/admin/search/analytics?daysBack=30")]
    public async Task Analytics_Reads_Need_CanManageSite(string path)
    {
        await using var factory = new PiiFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(path)).StatusCode);

        foreach (var refused in new[] { CmsRoles.ReadOnly, CmsRoles.ContentOwner, CmsRoles.Editor, CmsRoles.Developer })
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateAuthenticatedClient(refused).GetAsync(path)).StatusCode);

        foreach (var allowed in new[] { CmsRoles.SiteAdmin, CmsRoles.SystemAdmin })
            Assert.Equal(HttpStatusCode.OK, (await factory.CreateAuthenticatedClient(allowed).GetAsync(path)).StatusCode);
    }

    // ── host ──────────────────────────────────────────────────────────────────

    private sealed class PiiFactory : WebApplicationFactory<Program>
    {
        public Issue167AcceptanceTests.RecordingSearchRepo    Search    { get; } = new();
        public Issue167AcceptanceTests.RecordingAnalyticsRepo Analytics { get; } = new();

        public HttpClient CreateAuthenticatedClient(string roleName, long userId = 1)
        {
            var client = CreateClient();
            var jwt    = Services.GetRequiredService<IJwtService>();
            var user   = new User { Id = userId, ExternalId = "x" + userId, Email = $"u{userId}@va.gov", DisplayName = "U", IsActive = true };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                jwt.IssueAccessToken(user, [new UserRoleAssignment { RoleId = 1, RoleName = roleName }]));
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Logging:Sinks:Console:Enabled", "false");
            builder.UseSetting("Auth:Mode", "WindowsAuth");
            builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
            builder.UseSetting("Jwt:SigningKey",  "issue-175-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                Replace<IContentEntryRepository>(services,     _ => new AnySlugPublishedStub());
                Replace<ISearchRepository>(services,           _ => Search);
                Replace<ISearchAnalyticsRepository>(services,  _ => Analytics);
                Replace<IMediaAssetRepository>(services,       _ => new Issue158AcceptanceTests.AssetRepoStub());
                Replace<IMediaExtendedRepository>(services,    _ => new Issue158AcceptanceTests.UsageRepoStub());
                Replace<IStorageBackend>(services,             _ => new Issue158AcceptanceTests.StorageStub());
                Replace<IDbMonitorRepository>(services,        _ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.ApiRateLimitsEnabled, false));
            });
        }

        private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
        {
            foreach (var existing in services.Where(d => d.ServiceType == typeof(T)).ToList())
                services.Remove(existing);
            services.AddScoped<T>(factory);
        }
    }
}

// ── database ─────────────────────────────────────────────────────────────────

[Collection("Database")]
public class Issue175DatabaseTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("SearchQueryLog")]
    [InlineData("SearchResultClick")]
    [InlineData("SearchQuerySummary")]
    public async Task Raw_Tables_Store_No_Ip_Address_Or_Session_Key(string table)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM sys.columns
            WHERE  [object_id] = OBJECT_ID('dbo.' + @t)
              AND ([name] LIKE '%Ip%' OR [name] LIKE '%Address%' OR [name] LIKE '%Session%' OR [name] LIKE '%Cookie%' OR [name] LIKE '%Agent%')";
        cmd.Parameters.AddWithValue("@t", table);
        Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Logging_Procedures_Take_No_Client_Identifier()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT STRING_AGG(p.[name], ',') WITHIN GROUP (ORDER BY p.parameter_id)
            FROM   sys.parameters p
            WHERE  p.[object_id] = OBJECT_ID(@sp)";
        var q = cmd.Parameters.AddWithValue("@sp", "dbo.usp_Search_LogQuery");
        Assert.Equal("@Query,@ResultCount,@UserId", (string)(await cmd.ExecuteScalarAsync())!);
        q.Value = "dbo.usp_Search_LogClick";
        Assert.Equal("@Query,@ClickedSlug,@ResultRank,@UserId", (string)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Procedures_Scrub_A_Bare_Ssn_Even_When_The_Api_Is_Bypassed()
    {
        var marker = $"v049-{Guid.NewGuid():N}";
        await new SearchRepository(fixture.CreateDb()).LogQueryAsync($"{marker} 123-45-6789", 0, null);
        await new SearchRepository(fixture.CreateDb()).LogQueryAsync($"{marker} benefits", 0, null);
        await new SearchAnalyticsRepository(fixture.CreateDb()).LogClickAsync($"{marker} 987654321", "hr/test", 1, null);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM dbo.SearchQueryLog WHERE [Query] LIKE @m + '%'
            UNION ALL SELECT COUNT(*) FROM dbo.SearchQueryLog WHERE [Query] = N'[redacted]' AND [CreatedAt] > DATEADD(MINUTE, -1, SYSUTCDATETIME())
            UNION ALL SELECT COUNT(*) FROM dbo.SearchResultClick WHERE [Query] LIKE @m + '%'";
        cmd.Parameters.AddWithValue("@m", marker);
        var counts = new List<int>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) counts.Add(reader.GetInt32(0));

        Assert.Equal(1, counts[0]);        // only the clean query kept its text
        Assert.True(counts[1] >= 1);       // the SSN one became the token
        Assert.Equal(0, counts[2]);        // click with a 9-digit run too
    }

    [Fact]
    public async Task Rollup_Honours_The_Retention_Setting_And_Is_Idempotent()
    {
        var marker = $"rollup-{Guid.NewGuid():N}";
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        async Task Exec(string sql, params (string Name, object Value)[] args)
        {
            await using var c = conn.CreateCommand();
            c.CommandText = sql;
            foreach (var (n, v) in args) c.Parameters.AddWithValue(n, v);
            await c.ExecuteNonQueryAsync();
        }
        async Task<int> Count(string sql, params (string Name, object Value)[] args)
        {
            await using var c = conn.CreateCommand();
            c.CommandText = sql;
            foreach (var (n, v) in args) c.Parameters.AddWithValue(n, v);
            return (int)(await c.ExecuteScalarAsync())!;
        }

        try
        {
            // 7-day retention for this run. The procedure reads the SiteSetting row directly; the
            // row itself is created by the API at start-up, so seed it here the same way.
            var def = SiteSettingDefinitions.Get(SiteSettingKeys.SearchAnalyticsRetentionDays);
            await Exec("EXEC dbo.usp_SiteSetting_EnsureDefinition @Key, @Default, N'int', @Category, N'Server'",
                ("@Key", def.Key), ("@Default", def.Default), ("@Category", def.Category));
            await Exec("UPDATE dbo.SiteSetting SET [Value] = N'7' WHERE [Key] = @Key", ("@Key", def.Key));

            // Raw rows: 3 days old (kept, aggregated), 30 days old (aggregated then purged), and a 30-day-old click.
            await Exec(@"
                INSERT INTO dbo.SearchQueryLog ([Query],[ResultCount],[CreatedAt]) VALUES
                    (@m + ' recent', 3, DATEADD(DAY, -3,  SYSUTCDATETIME())),
                    (@m + ' recent', 0, DATEADD(DAY, -3,  SYSUTCDATETIME())),
                    (@m + ' old',    1, DATEADD(DAY, -30, SYSUTCDATETIME()));
                INSERT INTO dbo.SearchResultClick ([Query],[ClickedSlug],[ResultRank],[CreatedAt]) VALUES
                    (@m + ' old', 'hr/test', 1, DATEADD(DAY, -30, SYSUTCDATETIME()));", ("@m", marker));

            await Exec("EXEC dbo.usp_Maint_RollupSearchLogs");
            await Exec("EXEC dbo.usp_Maint_RollupSearchLogs");   // second run must add nothing

            Assert.Equal(2, await Count("SELECT COUNT(*) FROM dbo.SearchQueryLog WHERE [Query] = @q", ("@q", marker + " recent")));
            Assert.Equal(0, await Count("SELECT COUNT(*) FROM dbo.SearchQueryLog WHERE [Query] = @q", ("@q", marker + " old")));
            Assert.Equal(0, await Count("SELECT COUNT(*) FROM dbo.SearchResultClick WHERE [Query] = @q", ("@q", marker + " old")));

            Assert.Equal(1, await Count("SELECT COUNT(*) FROM dbo.SearchQuerySummary WHERE [Query] = @q", ("@q", marker + " old")));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM dbo.SearchQuerySummary WHERE [Query] = @q", ("@q", marker + " recent")));
            Assert.Equal(2, await Count("SELECT [SearchCount] FROM dbo.SearchQuerySummary WHERE [Query] = @q", ("@q", marker + " recent")));
            Assert.Equal(1, await Count("SELECT [ZeroResults] FROM dbo.SearchQuerySummary WHERE [Query] = @q", ("@q", marker + " recent")));
        }
        finally
        {
            await Exec("UPDATE dbo.SiteSetting SET [Value] = NULL WHERE [Key] = @Key", ("@Key", SiteSettingKeys.SearchAnalyticsRetentionDays));
            await Exec("DELETE FROM dbo.SearchQuerySummary WHERE [Query] LIKE @m + '%'; DELETE FROM dbo.SearchQueryLog WHERE [Query] LIKE @m + '%';", ("@m", marker));
        }
    }

    [Fact]
    public async Task Rollup_Falls_Back_To_Ninety_Days_When_The_Setting_Is_Unset()
    {
        var marker = $"fallback-{Guid.NewGuid():N}";
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        async Task Exec(string sql, params (string Name, object Value)[] args)
        {
            await using var c = conn.CreateCommand();
            c.CommandText = sql;
            foreach (var (n, v) in args) c.Parameters.AddWithValue(n, v);
            await c.ExecuteNonQueryAsync();
        }

        try
        {
            await Exec("UPDATE dbo.SiteSetting SET [Value] = NULL WHERE [Key] = @Key", ("@Key", SiteSettingKeys.SearchAnalyticsRetentionDays));
            await Exec(@"
                INSERT INTO dbo.SearchQueryLog ([Query],[ResultCount],[CreatedAt]) VALUES
                    (@m + ' kept',   1, DATEADD(DAY, -60,  SYSUTCDATETIME())),
                    (@m + ' purged', 1, DATEADD(DAY, -120, SYSUTCDATETIME()));", ("@m", marker));
            await Exec("EXEC dbo.usp_Maint_RollupSearchLogs");

            await using var count = conn.CreateCommand();
            count.CommandText = "SELECT STRING_AGG([Query], ',') WITHIN GROUP (ORDER BY [Query]) FROM dbo.SearchQueryLog WHERE [Query] LIKE @m + '%'";
            count.Parameters.AddWithValue("@m", marker);
            Assert.Equal(marker + " kept", (string)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            await Exec("DELETE FROM dbo.SearchQuerySummary WHERE [Query] LIKE @m + '%'; DELETE FROM dbo.SearchQueryLog WHERE [Query] LIKE @m + '%';", ("@m", marker));
        }
    }

    [Fact]
    public async Task Readonly_Login_Cannot_Read_Raw_Query_Tables_But_Can_Read_The_Summary()
    {
        await using var conn = new SqlConnection(fixture.ReadonlyConnectionString());
        await conn.OpenAsync();

        await using (var summary = conn.CreateCommand())
        {
            summary.CommandText = "SELECT COUNT(*) FROM dbo.SearchQuerySummary";
            Assert.NotNull(await summary.ExecuteScalarAsync());
        }

        foreach (var table in new[] { "SearchQueryLog", "SearchResultClick" })
        {
            await using var raw = conn.CreateCommand();
            raw.CommandText = $"SELECT TOP 1 [Query] FROM dbo.{table}";
            var ex = await Assert.ThrowsAsync<SqlException>(() => raw.ExecuteScalarAsync());
            Assert.Equal(229, ex.Number);   // "The SELECT permission was denied on the object"
        }
    }
}
