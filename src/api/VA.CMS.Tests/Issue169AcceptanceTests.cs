using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers;
using VA.CMS.API.Navigation;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #169 (epic #152) — redirects are served.
///
///   - GET /api/v1/redirects/resolve?path= is anonymous, cached, returns { fromPath, toPath, statusCode } or 404
///   - a published entry's slug change stores the public path (/pages/… or /news/…) so the resolver matches it
///   - loops are rejected on create/update (A→B when B→A); chains are flattened to the final target,
///     both for the new rule and for existing rules that pointed at its FromPath
///   - redirects.updated is a known webhook event, emitted by the admin controller and by a slug change
///   - the resolver honours the trailing-slash twin and caches misses until Invalidate()
/// </summary>
public class Issue169UnitTests
{
    [Theory]
    [InlineData("/pages/a", "/pages/a")]
    [InlineData("  /pages/a  ", "/pages/a")]
    [InlineData("/pages/a?utm=1", "/pages/a")]
    [InlineData("/pages/a#top", "/pages/a")]
    [InlineData("/", "/")]
    public void Normalize_Keeps_SiteRelative_Path_Without_Query(string input, string expected)
        => Assert.Equal(expected, RedirectResolver.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pages/a")]
    [InlineData("//evil.example/x")]
    [InlineData("/\\evil.example/x")]
    [InlineData("https://www.va.gov/x")]
    [InlineData("?only=query")]
    public void Normalize_Rejects_What_Cannot_Be_A_FromPath(string? input)
        => Assert.Null(RedirectResolver.Normalize(input));

    [Fact]
    public void Normalize_Rejects_Paths_Longer_Than_The_Column()
        => Assert.Null(RedirectResolver.Normalize("/" + new string('a', RedirectResolver.MaxPathLength)));

    [Fact]
    public async Task Resolver_Caches_Hits_And_Misses_Until_Invalidated()
    {
        var nav      = new CountingNav { Rules = { ["/old"] = new Redirect { FromPath = "/old", ToPath = "/new", StatusCode = 301 } } };
        var cache    = new RedirectResolveCache();
        var resolver = new RedirectResolver(nav, cache, StaticSiteSettings.Defaults);

        Assert.Equal("/new", (await resolver.ResolveAsync("/old"))!.ToPath);
        Assert.Equal("/new", (await resolver.ResolveAsync("/old?x=1"))!.ToPath);
        Assert.Null(await resolver.ResolveAsync("/missing"));
        Assert.Null(await resolver.ResolveAsync("/missing"));
        Assert.Equal(2, nav.Lookups);                       // one per distinct path

        nav.Rules["/missing"] = new Redirect { FromPath = "/missing", ToPath = "/found", StatusCode = 302 };
        Assert.Null(await resolver.ResolveAsync("/missing")); // still the cached miss

        resolver.Invalidate();
        Assert.Equal("/found", (await resolver.ResolveAsync("/missing"))!.ToPath);
        Assert.Equal(3, nav.Lookups);
    }

    [Fact]
    public async Task Resolver_With_Zero_Ttl_Does_Not_Cache()
    {
        var nav      = new CountingNav();
        var settings = StaticSiteSettings.Defaults.With(SiteSettingKeys.RedirectsCacheSeconds, 0);
        var resolver = new RedirectResolver(nav, new RedirectResolveCache(), settings);

        await resolver.ResolveAsync("/a");
        await resolver.ResolveAsync("/a");
        Assert.Equal(2, nav.Lookups);
        Assert.Equal(TimeSpan.Zero, resolver.CacheTtl);
    }

    [Fact]
    public void RedirectsUpdated_Is_A_Known_Webhook_Event()
        => Assert.Contains(WebhookEvents.RedirectsUpdated, WebhookEvents.All);

    [Theory]
    [InlineData("news_article", "/news/")]
    [InlineData("standard_page", "/pages/")]
    [InlineData(null, "/pages/")]
    public void PublicPathPrefix_Matches_The_Site_Routes(string? type, string expected)
        => Assert.Equal(expected, RedirectPathValidator.PublicPathPrefix(type));

    internal sealed class CountingNav : INavigationRepository
    {
        public Dictionary<string, Redirect> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Lookups { get; private set; }

        public Task<Redirect?> GetRedirectByPathAsync(string fromPath)
        {
            Lookups++;
            return Task.FromResult(Rules.TryGetValue(fromPath, out var r) ? r : null);
        }

        public Task<IEnumerable<NavigationItem>> GetMenuTreeAsync(string handle) => Task.FromResult<IEnumerable<NavigationItem>>([]);
        public Task<IEnumerable<NavigationItem>> GetMenuTreeAdminAsync(string handle) => Task.FromResult<IEnumerable<NavigationItem>>([]);
        public Task<NavigationItem?> GetItemAsync(long id) => Task.FromResult<NavigationItem?>(null);
        public Task<long> UpsertItemAsync(NavigationItem item) => Task.FromResult(1L);
        public Task DeleteItemAsync(long id) => Task.CompletedTask;
        public Task BulkReorderAsync(long menuId, string itemsJson) => Task.CompletedTask;
        public Task<long> CreateRedirectAsync(Redirect redirect) { Rules[redirect.FromPath] = redirect; return Task.FromResult(1L); }
        public Task<(IReadOnlyList<RedirectAdminRow> Rows, int TotalRows)> ListRedirectsAsync(bool? isActive = null, int page = 1, int pageSize = 50)
            => Task.FromResult<(IReadOnlyList<RedirectAdminRow>, int)>(([], 0));
        public Task<RedirectAdminRow?> GetRedirectByIdAsync(long id)
            => Task.FromResult<RedirectAdminRow?>(id == 1 && Rules.Count > 0
                ? new RedirectAdminRow { Id = 1, FromPath = Rules.Values.First().FromPath, ToPath = Rules.Values.First().ToPath, StatusCode = 301, IsActive = true }
                : null);
        public Task UpdateRedirectAsync(long id, string fromPath, string toPath, int statusCode) => Task.CompletedTask;
        public Task DeactivateRedirectAsync(long id) => Task.CompletedTask;
    }
}

[Collection("Database")]
public class Issue169AcceptanceTests(DatabaseFixture fixture)
{
    private INavigationRepository   Nav()     => new NavigationRepository(fixture.CreateDb());
    private IContentEntryRepository Entries() => new ContentEntryRepository(fixture.CreateDb());

    private async Task<RedirectAdminController> ControllerAsync(ISiteSettingsService? settings = null)
    {
        settings ??= StaticSiteSettings.Defaults;
        var controller = new RedirectAdminController(Nav(), Entries(), settings,
            new RedirectResolver(Nav(), new RedirectResolveCache(), settings), new RbacService());

        // CreatedById comes from the "sub" claim; the FK needs a real user row.
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
            },
        };
        return controller;
    }

    private async Task<long> SeedRuleAsync(string from, string to, int status = 301)
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        return await Nav().CreateRedirectAsync(new Redirect { FromPath = from, ToPath = to, StatusCode = status, IsActive = true, CreatedById = userId });
    }

    private async Task<(long EntryId, long UserId)> SeedPublishedAsync(string slug, string typeName)
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId    = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, typeName);
        var entryId = await TestSeeder.CreateEntryAsync(fixture.ConnectionString, ctId, slug, "en-US", userId);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_UpdateStatus @Id, @Status, @PubVerId";
        cmd.Parameters.AddWithValue("@Id", entryId);
        cmd.Parameters.AddWithValue("@Status", "Published");
        cmd.Parameters.Add("@PubVerId", System.Data.SqlDbType.BigInt).Value = DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
        return (entryId, userId);
    }

    private static string Unique(string stem) => $"/{stem}-{Guid.NewGuid():N}";

    /// <summary>Any type but news_article is served under /pages/; a private name keeps out of the demo seed's way.</summary>
    private const string PageType = "i169_page";

    // ── Slug change writes the public path ────────────────────────────────────

    [Fact]
    public async Task SlugChange_On_Published_Page_Redirects_The_Public_Path()
    {
        var oldSlug = $"i169-old-{Guid.NewGuid():N}";
        var newSlug = $"i169-new-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedPublishedAsync(oldSlug, PageType);

        var (ok, err) = await Entries().UpdateSlugAsync(entryId, newSlug, userId);
        Assert.True(ok, err);

        var hit = await Nav().GetRedirectByPathAsync($"/pages/{oldSlug}");
        Assert.NotNull(hit);
        Assert.Equal($"/pages/{newSlug}", hit!.ToPath);
        Assert.Equal(301, hit.StatusCode);
        Assert.Null(await Nav().GetRedirectByPathAsync(oldSlug)); // no bare-slug row any more
    }

    [Fact]
    public async Task SlugChange_On_Published_News_Uses_The_News_Route()
    {
        // "demo/" slugs so usp_DemoSeed_Reset (DemoSeedServiceTests) can still drop the
        // shared news_article content type after this test has used it.
        var oldSlug = $"demo/i169-news-old-{Guid.NewGuid():N}";
        var newSlug = $"demo/i169-news-new-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedPublishedAsync(oldSlug, "news_article");

        Assert.True((await Entries().UpdateSlugAsync(entryId, newSlug, userId)).Success);

        var hit = await Nav().GetRedirectByPathAsync($"/news/{oldSlug}");
        Assert.Equal($"/news/{newSlug}", hit?.ToPath);
    }

    [Fact]
    public async Task SlugChange_Back_To_An_Old_Slug_Stops_The_Rule_That_Would_Shadow_It()
    {
        var a = $"i169-a-{Guid.NewGuid():N}";
        var b = $"i169-b-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedPublishedAsync(a, PageType);

        Assert.True((await Entries().UpdateSlugAsync(entryId, b, userId)).Success); // /pages/a → /pages/b
        Assert.True((await Entries().UpdateSlugAsync(entryId, a, userId)).Success); // back: /pages/b → /pages/a

        Assert.Null(await Nav().GetRedirectByPathAsync($"/pages/{a}"));           // a is live again
        Assert.Equal($"/pages/{a}", (await Nav().GetRedirectByPathAsync($"/pages/{b}"))?.ToPath);
    }

    // ── Trailing slash ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByPath_Matches_The_Trailing_Slash_Twin_But_Prefers_Exact()
    {
        var from = Unique("i169-slash");
        await SeedRuleAsync(from, "/exact");
        await SeedRuleAsync(from + "/", "/slashed");

        Assert.Equal("/exact",   (await Nav().GetRedirectByPathAsync(from))?.ToPath);
        Assert.Equal("/slashed", (await Nav().GetRedirectByPathAsync(from + "/"))?.ToPath);

        var only = Unique("i169-only");
        await SeedRuleAsync(only, "/target");
        var twin = await Nav().GetRedirectByPathAsync(only + "/");
        Assert.Equal("/target", twin?.ToPath);
        Assert.Equal(only, twin?.FromPath);
    }

    // ── Loops and chains ──────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Rejects_A_Loop()
    {
        var a = Unique("i169-loop-a");
        var b = Unique("i169-loop-b");
        await SeedRuleAsync(b, a);

        var result = await (await ControllerAsync()).CreateRedirect(new CreateRedirectRequest { FromPath = a, ToPath = b, StatusCode = 301 });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("loop", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await Nav().GetRedirectByPathAsync(a));
    }

    [Fact]
    public async Task Create_Rejects_A_Loop_Through_A_Chain()
    {
        var a = Unique("i169-chain-a");
        var b = Unique("i169-chain-b");
        var c = Unique("i169-chain-c");
        await SeedRuleAsync(c, a);
        await SeedRuleAsync(b, c);   // seeded straight through the SP, so b → c → a is a real two-hop chain

        var result = await (await ControllerAsync()).CreateRedirect(new CreateRedirectRequest { FromPath = a, ToPath = b, StatusCode = 301 });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_Flattens_The_New_Rule_To_The_Final_Target()
    {
        var a = Unique("i169-flat-a");
        var b = Unique("i169-flat-b");
        var c = Unique("i169-flat-c");
        await SeedRuleAsync(b, c);

        var result = await (await ControllerAsync()).CreateRedirect(new CreateRedirectRequest { FromPath = a, ToPath = b, StatusCode = 302 });

        var created = Assert.IsType<CreatedResult>(result);
        var dto     = Assert.IsType<RedirectAdminDto>(created.Value);
        Assert.Equal(c, dto.ToPath);
        Assert.Equal(302, dto.StatusCode);
        Assert.Equal(c, (await Nav().GetRedirectByPathAsync(a))?.ToPath);
    }

    [Fact]
    public async Task Create_Repoints_Existing_Rules_That_Targeted_The_New_FromPath()
    {
        var x = Unique("i169-up-x");
        var a = Unique("i169-up-a");
        var b = Unique("i169-up-b");
        await SeedRuleAsync(x, a);

        Assert.IsType<CreatedResult>(await (await ControllerAsync()).CreateRedirect(new CreateRedirectRequest { FromPath = a, ToPath = b, StatusCode = 301 }));

        Assert.Equal(b, (await Nav().GetRedirectByPathAsync(x))?.ToPath);
        Assert.Equal(b, (await Nav().GetRedirectByPathAsync(a))?.ToPath);
    }

    [Fact]
    public async Task Update_Rejects_A_Loop_And_Flattens_Otherwise()
    {
        var a = Unique("i169-upd-a");
        var b = Unique("i169-upd-b");
        var c = Unique("i169-upd-c");
        var d = Unique("i169-upd-d");
        var id = await SeedRuleAsync(a, d);
        await SeedRuleAsync(b, a);   // b → a; editing a → b would loop
        await SeedRuleAsync(c, d);

        var loop = await (await ControllerAsync()).UpdateRedirect(id, new UpdateRedirectRequest { FromPath = a, ToPath = b, StatusCode = 301 });
        Assert.IsType<BadRequestObjectResult>(loop);

        // a → c, and c → d, so the stored target is d
        var ok = await (await ControllerAsync()).UpdateRedirect(id, new UpdateRedirectRequest { FromPath = a, ToPath = c, StatusCode = 301 });
        Assert.IsType<NoContentResult>(ok);
        Assert.Equal(d, (await Nav().GetRedirectByPathAsync(a))?.ToPath);
    }

    [Fact]
    public async Task External_Targets_Are_Stored_As_Given()
    {
        var a = Unique("i169-ext");
        var controller = await ControllerAsync(
            StaticSiteSettings.Defaults.WithJson(SiteSettingKeys.RedirectsAllowedExternalHosts, new[] { "www.va.gov" }));

        var created = Assert.IsType<CreatedResult>(await controller.CreateRedirect(
            new CreateRedirectRequest { FromPath = a, ToPath = "https://www.va.gov/health-care", StatusCode = 302 }));
        Assert.Equal("https://www.va.gov/health-care", Assert.IsType<RedirectAdminDto>(created.Value).ToPath);
    }
}

/// <summary>HTTP contract of the public resolver and the webhook emitted by the admin API.</summary>
public class Issue169HttpTests
{
    [Fact]
    public async Task Resolve_Is_Anonymous_And_Cached()
    {
        using var factory = new ResolverFactory(cacheSeconds: 120);
        factory.Nav.Rules["/pages/old"] = new Redirect { FromPath = "/pages/old", ToPath = "/pages/new", StatusCode = 301 };
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/redirects/resolve?path=/pages/old?utm=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("public, max-age=120", resp.Headers.CacheControl!.ToString());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("/pages/old", body.GetProperty("fromPath").GetString());
        Assert.Equal("/pages/new", body.GetProperty("toPath").GetString());
        Assert.Equal(301, body.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Resolve_Miss_Is_404_With_The_Same_Cache_Header()
    {
        using var factory = new ResolverFactory(cacheSeconds: 30);
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/redirects/resolve?path=/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("public, max-age=30", resp.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?path=")]
    [InlineData("?path=pages/old")]
    [InlineData("?path=//evil.example/x")]
    [InlineData("?path=https://www.va.gov/x")]
    public async Task Resolve_Rejects_Paths_That_Cannot_Match(string query)
    {
        using var factory = new ResolverFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/redirects/resolve" + query);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Resolve_With_Caching_Off_Sends_No_Store()
    {
        using var factory = new ResolverFactory(cacheSeconds: 0);
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/redirects/resolve?path=/x");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.True(resp.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Admin_Mutations_Emit_RedirectsUpdated()
    {
        using var factory = new ResolverFactory();
        using var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath = "/legacy/x", toPath = "/pages/x", statusCode = 301 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        Assert.Contains(factory.Webhooks.Events, e => e == WebhookEvents.RedirectsUpdated);
        Assert.Equal(1, factory.Nav.Rules["/legacy/x"].CreatedById);   // the bearer token's cms_user_id, not 0
    }

    [Fact]
    public async Task Failed_Admin_Mutation_Emits_Nothing()
    {
        using var factory = new ResolverFactory();
        using var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath = "not-a-path", toPath = "/pages/x", statusCode = 301 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(factory.Webhooks.Events);
    }

    internal sealed class RecordingWebhooks : IWebhookBackgroundDispatcher
    {
        public List<string> Events { get; } = new();
        public List<object> Payloads { get; } = new();
        public Task EnqueueAsync(string eventName, object payload, CancellationToken ct = default)
        {
            Events.Add(eventName); Payloads.Add(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class ResolverFactory(int cacheSeconds = 60) : WebApplicationFactory<Program>
    {
        public Issue169UnitTests.CountingNav Nav      { get; } = new();
        public RecordingWebhooks             Webhooks { get; } = new();

        public HttpClient CreateAuthenticatedClient(string roleName)
        {
            var client = CreateClient();
            var jwt    = Services.GetRequiredService<IJwtService>();
            var user   = new User { Id = 1, ExternalId = "x1", Email = "u1@va.gov", DisplayName = "U", IsActive = true };
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
            builder.UseSetting("Jwt:SigningKey",  "issue-169-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                Replace<INavigationRepository>(services,   _ => Nav);
                Replace<IContentEntryRepository>(services, _ => new Issue23ContentEntryStub());
                Replace<IDbMonitorRepository>(services,    _ => new AuthTestStubs.StubDbMonitorRepository());
                foreach (var d in services.Where(d => d.ServiceType == typeof(IWebhookBackgroundDispatcher)).ToList())
                    services.Remove(d);
                services.AddSingleton<IWebhookBackgroundDispatcher>(Webhooks);
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.RedirectsCacheSeconds, cacheSeconds));
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
