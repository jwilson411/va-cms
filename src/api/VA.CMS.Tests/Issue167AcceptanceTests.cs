using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PetaPoco;
using VA.CMS.API.Auth;
using VA.CMS.API.Middleware;
using VA.CMS.API.RateLimiting;
using VA.CMS.API.Search;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #167: rate limiting, input bounds, queued search logging.
///   - Named policies from api.rateLimits.* settings; 429 with Retry-After + ProblemDetails; per-IP / per-user keys.
///   - Every controller action gets a policy by convention; explicit attributes win.
///   - q ≤ search.maxQueryLength; click slug must be published; rank bounded; DTO annotations.
///   - Search/click rows go through a bounded drop-oldest channel and land after the response.
///   - Per-request body limit from api.maxRequestBodyBytes.
/// </summary>
public class Issue167AcceptanceTests
{
    // ── rate limiting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Public_Read_Is_429_After_The_Configured_Allowance_With_Retry_After()
    {
        await using var factory = new LimitsFactory(publicReadPerMinute: 3);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=benefits")).StatusCode);

        var limited = await client.GetAsync("/api/v1/search?q=benefits");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.True(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds >= 1);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal(429, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.GetProperty("retryAfterSeconds").GetInt32() >= 1);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("correlationId").GetString()));
        Assert.True(limited.Headers.Contains(CorrelationIdMiddleware.Header));

        // Other policies are separate buckets: an auth call still goes through.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Auth_Endpoints_Have_Their_Own_Stricter_Bucket()
    {
        await using var factory = new LimitsFactory(authPerMinute: 2);
        var client = factory.CreateClient();

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,    (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,    (await client.GetAsync("/api/auth/login?ack=1")).StatusCode);   // same bucket

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=benefits")).StatusCode);   // public-read untouched
    }

    [Fact]
    public async Task Admin_Calls_Are_Limited_Per_User_Not_Per_Address()
    {
        await using var factory = new LimitsFactory(adminPerMinute: 2);

        var alice = factory.CreateAuthenticatedClient(CmsRoles.Editor, userId: 1);
        var bob   = factory.CreateAuthenticatedClient(CmsRoles.Editor, userId: 2);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await alice.GetAsync("/api/v1/media")).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await alice.GetAsync("/api/v1/media")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,    (await alice.GetAsync("/api/v1/media")).StatusCode);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await bob.GetAsync("/api/v1/media")).StatusCode);   // Bob has his own bucket
    }

    [Fact]
    public async Task Master_Switch_And_Zero_Disable_Limiting()
    {
        await using var off = new LimitsFactory(publicReadPerMinute: 1, enabled: false);
        var client = off.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=benefits")).StatusCode);

        await using var zero = new LimitsFactory(publicReadPerMinute: 0);
        client = zero.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=benefits")).StatusCode);
    }

    [Fact]
    public void Client_Key_Is_The_User_When_Authenticated_Else_The_Address()
    {
        var anon = new DefaultHttpContext();
        anon.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        Assert.Equal("ip:10.1.2.3", RateLimitPolicies.ClientKey(anon));

        Assert.Equal("ip:unknown", RateLimitPolicies.ClientKey(new DefaultHttpContext()));

        var user = new DefaultHttpContext();
        user.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("cms_user_id", "42")], "test"));
        Assert.Equal("u:42", RateLimitPolicies.ClientKey(user));
    }

    [Theory]
    [InlineData("GET",  "/api/v1/search",              RateLimitPolicies.PublicRead)]
    [InlineData("GET",  "/api/v1/content/{**slug}",    RateLimitPolicies.PublicRead)]
    [InlineData("GET",  "/api/v1/navigation/{handle}", RateLimitPolicies.PublicRead)]
    [InlineData("GET",  "/api/v1/media/serve/{id:long}", RateLimitPolicies.PublicRead)]
    [InlineData("GET",  "/api/v1/settings/public",     RateLimitPolicies.PublicRead)]
    [InlineData("GET",  "/api/v1/preview",             RateLimitPolicies.PublicRead)]
    [InlineData("POST", "/api/auth/refresh",           RateLimitPolicies.Auth)]
    [InlineData("GET",  "/api/auth/login",             RateLimitPolicies.Auth)]
    [InlineData("GET",  "/api/auth/windows-login",     RateLimitPolicies.Auth)]
    [InlineData("POST", "/api/v1/search/click",        RateLimitPolicies.AnalyticsWrite)]
    [InlineData("POST", "/api/v1/security/csp-report", RateLimitPolicies.AnalyticsWrite)]
    [InlineData("GET",  "/api/v1/settings/client",     RateLimitPolicies.Admin)]
    [InlineData("GET",  "/api/v1/media",               RateLimitPolicies.Admin)]
    [InlineData("GET",  "/api/v1/admin/search/analytics", RateLimitPolicies.Admin)]
    [InlineData("GET",  "/api/v1/admin/settings",      RateLimitPolicies.Admin)]
    [InlineData("POST", "/api/graphql/{**slug}",       RateLimitPolicies.PublicRead)]
    [InlineData("GET",  "/health/ready",               RateLimitPolicies.PublicRead)]
    public void Every_Endpoint_Carries_A_Policy(string method, string pattern, string expected)
    {
        using var factory = new LimitsFactory();
        factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var endpoint = endpoints.FirstOrDefault(e =>
            string.Equals("/" + e.RoutePattern.RawText?.TrimStart('/'), pattern, StringComparison.OrdinalIgnoreCase)
            && (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? true));
        Assert.NotNull(endpoint);

        var policy = endpoint!.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        Assert.Equal(expected, policy);
    }

    [Fact]
    public void No_Controller_Action_Is_Left_Without_A_Policy()
    {
        using var factory = new LimitsFactory();
        factory.CreateClient();

        var unlimited = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is not null)
            .Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null && e.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null)
            .Select(e => e.DisplayName)
            .ToList();

        Assert.Empty(unlimited);
    }

    // ── input bounds ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_Query_Longer_Than_The_Setting_Is_400()
    {
        await using var factory = new LimitsFactory(maxQueryLength: 20);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=" + new string('a', 20))).StatusCode);

        var tooLong = await client.GetAsync("/api/v1/search?q=" + new string('a', 21));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("20 characters", await tooLong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Click_Tracking_Is_Bounded()
    {
        await using var factory = new LimitsFactory(maxQueryLength: 20);
        var client = factory.CreateClient();

        // slug must be a published entry
        var unknown = await client.PostAsJsonAsync("/api/v1/search/click", new { query = "benefits", clickedSlug = "no/such/page", resultRank = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("published", await unknown.Content.ReadAsStringAsync());

        // rank out of range → DataAnnotations 400
        var rank = await client.PostAsJsonAsync("/api/v1/search/click", new { query = "benefits", clickedSlug = "hr/test", resultRank = 5000 });
        Assert.Equal(HttpStatusCode.BadRequest, rank.StatusCode);
        Assert.Contains("ResultRank", await rank.Content.ReadAsStringAsync());

        // over-long slug → DataAnnotations 400
        var slug = await client.PostAsJsonAsync("/api/v1/search/click", new { query = "benefits", clickedSlug = new string('s', 501), resultRank = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, slug.StatusCode);

        // over-long query → setting-based 400
        var query = await client.PostAsJsonAsync("/api/v1/search/click", new { query = new string('q', 21), clickedSlug = "hr/test", resultRank = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Contains("20 characters", await query.Content.ReadAsStringAsync());

        Assert.Empty(factory.Analytics.Clicks);
    }

    [Fact]
    public async Task Request_Body_Limit_Comes_From_The_Setting_Except_For_Uploads()
    {
        var settings = StaticSiteSettings.Defaults
            .With(SiteSettingKeys.ApiMaxRequestBodyBytes, 2048)
            .With(SiteSettingKeys.MediaMaxUploadBytes, 1_000_000);

        Assert.Equal(2048, await LimitFor("POST", "/api/v1/search/click", settings));
        Assert.Equal(1_000_000 + 64 * 1024, await LimitFor("POST", "/api/v1/media/upload", settings));
        Assert.Equal(2048, await LimitFor("GET", "/api/v1/media/upload", settings));

        static async Task<long?> LimitFor(string method, string path, ISiteSettingsService settings)
        {
            var services = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();
            var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(services);
            app.UseSiteSettingGates(isDevelopment: true);
            var pipeline = app.Build();

            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = method;
            context.Request.Path   = path;
            var feature = new FakeBodySizeFeature();
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
            await pipeline(context);
            return feature.MaxRequestBodySize;
        }
    }

    // ── queued search logging ─────────────────────────────────────────────────

    [Fact]
    public async Task Search_And_Click_Rows_Land_After_The_Response_Through_The_Queue()
    {
        await using var factory = new LimitsFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=disability%20rating")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/search/click",
            new { query = "disability rating", clickedSlug = "/hr/test", resultRank = 2 })).StatusCode);

        var queue = factory.Services.GetRequiredService<ISearchLogQueue>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (queue.Pending > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Equal(0, queue.Pending);
        Assert.Equal(0, queue.Dropped);
        Assert.Equal([("disability rating", 0)], factory.Search.Queries.Select(q => (q.Query, q.ResultCount)).ToArray());
        Assert.Equal([("disability rating", "hr/test", 2)], factory.Analytics.Clicks.Select(c => (c.Query, c.Slug, c.Rank)).ToArray());
    }

    [Fact]
    public async Task Analytics_Switch_Off_Means_No_Rows_But_Still_204()
    {
        await using var factory = new LimitsFactory(searchAnalytics: false);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=benefits")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/search/click",
            new { query = "benefits", clickedSlug = "hr/test", resultRank = 1 })).StatusCode);

        await Task.Delay(100);
        Assert.Empty(factory.Search.Queries);
        Assert.Empty(factory.Analytics.Clicks);
    }

    [Fact]
    public void Queue_Drops_The_Oldest_When_Full_And_Counts_It()
    {
        var settings = StaticSiteSettings.Defaults.With(SiteSettingKeys.SearchLogQueueCapacity, 100);
        var queue = new SearchLogQueue(settings, NullLogger<SearchLogQueue>.Instance);

        for (var i = 0; i < 150; i++)
            Assert.True(queue.TryEnqueue(new SearchQueryLogItem($"q{i}", 0, null)));

        Assert.Equal(100, queue.Pending);
        Assert.Equal(50,  queue.Dropped);

        // What survived is the newest 100.
        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal("q50", ((SearchQueryLogItem)first!).Query);
    }

    [Fact]
    public async Task Writer_Survives_A_Failing_Row_And_Keeps_Going()
    {
        var search = new RecordingSearchRepo { FailOn = "bad" };
        var services = new ServiceCollection()
            .AddScoped<ISearchRepository>(_ => search)
            .AddScoped<ISearchAnalyticsRepository>(_ => new RecordingAnalyticsRepo())
            .BuildServiceProvider();
        var queue  = new SearchLogQueue(StaticSiteSettings.Defaults, NullLogger<SearchLogQueue>.Instance);
        var writer = new SearchLogWriter(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchLogWriter>.Instance);

        await writer.StartAsync(CancellationToken.None);
        queue.TryEnqueue(new SearchQueryLogItem("good", 1, null));
        queue.TryEnqueue(new SearchQueryLogItem("bad", 1, null));
        queue.TryEnqueue(new SearchQueryLogItem("good again", 1, null));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (queue.Pending > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(["good", "good again"], search.Queries.Select(q => q.Query).ToArray());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }

    internal sealed class RecordingSearchRepo : ISearchRepository
    {
        public string? FailOn { get; init; }
        public List<(string Query, int ResultCount)> Queries { get; } = [];

        public Task<Page<SearchResult>> FullTextSearchAsync(string query, long? contentTypeId = null, DateTime? fromDate = null,
            DateTime? toDate = null, long? tagTermId = null, int page = 1, int pageSize = 25)
            => Task.FromResult(new Page<SearchResult> { Items = [], TotalItems = 0, CurrentPage = page, ItemsPerPage = pageSize });

        public Task LogQueryAsync(string query, int resultCount, long? userId = null)
        {
            if (query == FailOn) throw new InvalidOperationException("simulated SQL failure");
            lock (Queries) Queries.Add((query, resultCount));
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingAnalyticsRepo : ISearchAnalyticsRepository
    {
        public List<(string Query, string Slug, int Rank)> Clicks { get; } = [];

        public Task<IReadOnlyList<SearchQueryStat>> GetTopQueriesAsync(int topN = 10, int daysBack = 30) => Task.FromResult<IReadOnlyList<SearchQueryStat>>([]);
        public Task<IReadOnlyList<SearchZeroResultStat>> GetZeroResultQueriesAsync(int topN = 10, int daysBack = 30) => Task.FromResult<IReadOnlyList<SearchZeroResultStat>>([]);
        public Task<(IReadOnlyList<SearchAnalyticsRow> Items, int TotalRows)> GetFullAnalyticsAsync(int daysBack = 30, int page = 1, int pageSize = 50)
            => Task.FromResult<(IReadOnlyList<SearchAnalyticsRow>, int)>(([], 0));

        public Task LogClickAsync(string query, string clickedSlug, int resultRank = 0, long? userId = null)
        {
            lock (Clicks) Clicks.Add((query, clickedSlug, resultRank));
            return Task.CompletedTask;
        }
    }

    private sealed class PublishedSlugStub : Issue23ContentEntryStub
    {
        public override Task<PublishedContentEntry?> GetPublishedBySlugAsync(string slug, string locale = "en-US")
            => Task.FromResult<PublishedContentEntry?>(slug == "hr/test"
                ? new PublishedContentEntry { Id = 1, ContentTypeId = 1, Slug = slug, Locale = locale }
                : null);
    }

    private sealed class LimitsFactory(
        int publicReadPerMinute = 300,
        int authPerMinute = 30,
        int adminPerMinute = 600,
        bool enabled = true,
        int maxQueryLength = 200,
        bool searchAnalytics = true) : WebApplicationFactory<Program>
    {
        public RecordingSearchRepo    Search    { get; } = new();
        public RecordingAnalyticsRepo Analytics { get; } = new();

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
            builder.UseSetting("Jwt:SigningKey",  "issue-167-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                Replace<IContentEntryRepository>(services,     _ => new PublishedSlugStub());
                Replace<ISearchRepository>(services,           _ => Search);
                Replace<ISearchAnalyticsRepository>(services,  _ => Analytics);
                Replace<IMediaAssetRepository>(services,       _ => new Issue158AcceptanceTests.AssetRepoStub());
                Replace<IMediaExtendedRepository>(services,    _ => new Issue158AcceptanceTests.UsageRepoStub());
                Replace<IStorageBackend>(services,             _ => new Issue158AcceptanceTests.StorageStub());
                Replace<IDbMonitorRepository>(services,        _ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.ApiRateLimitsEnabled, enabled)
                    .With(SiteSettingKeys.ApiRateLimitsPublicReadPerMinute, publicReadPerMinute)
                    .With(SiteSettingKeys.ApiRateLimitsAuthPerMinute, authPerMinute)
                    .With(SiteSettingKeys.ApiRateLimitsAdminPerMinute, adminPerMinute)
                    .With(SiteSettingKeys.SearchMaxQueryLength, maxQueryLength)
                    .With(SiteSettingKeys.FeatureSearchAnalytics, searchAnalytics));
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
