using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers;
using VA.CMS.API.Navigation;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #155: default-deny authorization.
///
///   - Navigation and redirect admin mutations need CanManageSite; reads need any role.
///   - A principal with no CMS role gets 403 from every /api/v1/** endpoint.
///   - Redirect FromPath must be site-relative and must not shadow a published slug;
///     ToPath must be site-relative or an https URL on an allow-listed host.
///   - With auth.autoProvisionUsers off, unknown identities cannot sign in.
/// </summary>
public class Issue155AcceptanceTests
{
    // ── Policies over HTTP ────────────────────────────────────────────────────

    [Theory]
    [InlineData(CmsRoles.Editor)]
    [InlineData(CmsRoles.ReadOnly)]
    [InlineData(CmsRoles.ContentOwner)]
    [InlineData(CmsRoles.Developer)]
    public async Task Redirect_Mutations_Return_403_Without_ManageSite(string role)
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(role);

        var post  = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath = "/a", toPath = "/b", statusCode = 301 });
        var patch = await client.PatchAsJsonAsync("/api/v1/redirects/1", new { fromPath = "/a", toPath = "/b", statusCode = 301 });
        var del   = await client.DeleteAsync("/api/v1/redirects/1");

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    [Theory]
    [InlineData(CmsRoles.Editor)]
    [InlineData(CmsRoles.ReadOnly)]
    public async Task Navigation_Mutations_Return_403_Without_ManageSite(string role)
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(role);

        var createMenu = await client.PostAsJsonAsync("/api/v1/navigation", new { handle = "x", name = "X" });
        var addItem    = await client.PostAsJsonAsync("/api/v1/navigation/main/items", new { label = "L", url = "/l" });
        var delMenu    = await client.DeleteAsync("/api/v1/navigation/main");
        var reorder    = await client.PostAsJsonAsync("/api/v1/navigation/main/reorder", new { items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, createMenu.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addItem.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delMenu.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reorder.StatusCode);
    }

    [Theory]
    [InlineData(CmsRoles.SiteAdmin)]
    [InlineData(CmsRoles.SystemAdmin)]
    public async Task Redirect_Create_Allowed_For_ManageSite_Roles(string role)
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(role);

        var post = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath = "/old", toPath = "/new", statusCode = 301 });

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
    }

    [Fact]
    public async Task Redirect_And_Navigation_Reads_Allowed_For_Any_Role()
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.ReadOnly);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/redirects")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/navigation")).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/redirects")]
    [InlineData("/api/v1/navigation")]
    [InlineData("/api/v1/settings/client")]
    [InlineData("/api/v1/notifications")]
    [InlineData("/api/v1/admin/users")]
    public async Task Principal_With_No_Cms_Role_Gets_403(string path)
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(roleName: null);

        var resp = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Bootstrap_Endpoints_Still_Open()
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/settings/public")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    // ── ToPath / FromPath validation over HTTP ────────────────────────────────

    [Theory]
    [InlineData("https://evil.example/phish",   "not in the redirects.allowedExternalHosts")]
    [InlineData("http://www.va.gov/x",          "must use https")]
    [InlineData("//evil.example/phish",         "site-relative path")]
    [InlineData("javascript:alert(1)",          "site-relative path")]
    [InlineData("https://user:pw@www.va.gov/x", "must not embed credentials")]
    public async Task Create_Rejects_Unsafe_ToPath(string toPath, string expectedError)
    {
        await using var factory = new Issue155TestFactory(allowedHosts: ["www.va.gov"]);
        var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath = "/old", toPath, statusCode = 301 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains(expectedError, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("https://www.va.gov/health-care")]
    [InlineData("https://benefits.va.gov/x")]
    [InlineData("/pages/new-home")]
    public async Task Create_Accepts_Relative_And_AllowListed_ToPath(string toPath)
    {
        await using var factory = new Issue155TestFactory(allowedHosts: ["www.va.gov", "*.va.gov"]);
        var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath = "/old", toPath, statusCode = 301 });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Theory]
    [InlineData("old-page",                  "site-relative")]
    [InlineData("https://www.va.gov/old",    "site-relative")]
    [InlineData("//old",                     "site-relative")]
    [InlineData("/old?x=1",                  "query string")]
    public async Task Create_Rejects_Bad_FromPath(string fromPath, string expectedError)
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath, toPath = "/new", statusCode = 301 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains(expectedError, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/pages/hr/benefits")]
    [InlineData("/hr/benefits")]
    public async Task Create_Rejects_FromPath_That_Shadows_Published_Slug(string fromPath)
    {
        await using var factory = new Issue155TestFactory(publishedSlug: "hr/benefits");
        var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PostAsJsonAsync("/api/v1/redirects", new { fromPath, toPath = "/new", statusCode = 301 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("collides with published content", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_Applies_Same_Validation()
    {
        await using var factory = new Issue155TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.SiteAdmin);

        var resp = await client.PatchAsJsonAsync("/api/v1/redirects/1", new { fromPath = "/old", toPath = "https://evil.example/", statusCode = 301 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Validator unit tests ──────────────────────────────────────────────────

    [Theory]
    [InlineData("/",             true)]
    [InlineData("/a/b",          true)]
    [InlineData("//evil",        false)]
    [InlineData("/\\evil",       false)]
    [InlineData("a/b",           false)]
    [InlineData("/a b",          false)]
    [InlineData("",              false)]
    public void IsSiteRelative(string path, bool expected)
        => Assert.Equal(expected, RedirectPathValidator.IsSiteRelative(path));

    [Fact]
    public void ToPath_Host_Matching_Is_Case_Insensitive_And_Supports_Wildcards()
    {
        string[] hosts = ["WWW.va.gov", "*.example.gov"];
        Assert.Null(RedirectPathValidator.ValidateToPath("https://www.VA.gov/x", hosts));
        Assert.Null(RedirectPathValidator.ValidateToPath("https://a.b.example.gov/x", hosts));
        Assert.NotNull(RedirectPathValidator.ValidateToPath("https://example.gov/x", hosts));     // wildcard needs a subdomain
        Assert.NotNull(RedirectPathValidator.ValidateToPath("https://notexample.gov/x", hosts));
        Assert.NotNull(RedirectPathValidator.ValidateToPath("https://www.va.gov.evil.example/x", hosts));
    }

    [Fact]
    public void ToPath_With_Empty_AllowList_Only_Accepts_Relative()
    {
        Assert.Null(RedirectPathValidator.ValidateToPath("/x", []));
        Assert.NotNull(RedirectPathValidator.ValidateToPath("https://www.va.gov/x", []));
    }

    [Fact]
    public void SlugCandidates_Strips_Pages_Prefix()
    {
        Assert.Equal(["pages/a/b", "a/b"], RedirectPathValidator.SlugCandidates("/pages/a/b/"));
        Assert.Equal(["news/x"],           RedirectPathValidator.SlugCandidates("/news/x"));
        Assert.Empty(RedirectPathValidator.SlugCandidates("/"));
    }

    // ── auto-provisioning ─────────────────────────────────────────────────────

    [Fact]
    public async Task AzureAd_Unknown_User_Rejected_When_AutoProvision_Off()
    {
        await using var factory = new Issue154TestFactory(autoProvision: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader, "stranger@va.gov");

        var login = await client.GetAsync("/api/auth/login");
        var aad   = await client.GetAsync(login.Headers.Location);
        var cb    = await client.GetAsync(aad.Headers.Location);

        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Equal("/login?error=not_provisioned", cb.Headers.Location!.ToString());
        Assert.DoesNotContain(cb.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith($"{AuthCookieHelper.RefreshTokenCookieName}=") && !c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WindowsAuth_Unknown_User_Rejected_When_AutoProvision_Off()
    {
        await using var factory = new Issue155TestFactory(mode: AuthMode.WindowsAuth, autoProvision: false);
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/windows-login");
        req.Headers.Add(FakeNegotiateHandler.TestUpnHeader, "stranger@va.gov");

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("not been provisioned", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WindowsAuth_Known_User_Allowed_When_AutoProvision_Off()
    {
        await using var factory = new Issue155TestFactory(mode: AuthMode.WindowsAuth, autoProvision: false);
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/windows-login");
        req.Headers.Add(FakeNegotiateHandler.TestUpnHeader, Issue155TestFactory.KnownUpn);

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}

// ── Test factory ──────────────────────────────────────────────────────────────

public sealed class Issue155TestFactory : WebApplicationFactory<Program>
{
    public const string KnownUpn = "known@va.gov";

    private readonly AuthMode _mode;
    private readonly bool     _autoProvision;
    private readonly string[] _allowedHosts;
    private readonly string?  _publishedSlug;

    public Issue155TestFactory(
        AuthMode  mode          = AuthMode.AzureAd,
        bool      autoProvision = true,
        string[]? allowedHosts  = null,
        string?   publishedSlug = null)
    {
        _mode          = mode;
        _autoProvision = autoProvision;
        _allowedHosts  = allowedHosts ?? [];
        _publishedSlug = publishedSlug;
    }

    public HttpClient CreateAuthenticatedClient(string? roleName)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var jwt    = Services.GetRequiredService<IJwtService>();
        var user   = new User { Id = 1, ExternalId = KnownUpn, Email = KnownUpn, DisplayName = "Known", IsActive = true };
        var roles  = roleName is null
            ? Array.Empty<UserRoleAssignment>()
            : [new UserRoleAssignment { RoleId = 1, RoleName = roleName }];
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt.IssueAccessToken(user, roles));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("SKIP_MIGRATIONS", "true");
        builder.UseSetting("Auth:Mode",       _mode.ToString());
        builder.UseSetting("AZUREAD_FAKE_OIDC", "true");
        builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
        builder.UseSetting("Jwt:SigningKey",  "issue-155-acceptance-key-32chars!");
        builder.UseSetting("Jwt:Issuer",      "va-cms-api");
        builder.UseSetting("Jwt:Audience",    "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IUserRepository>(services,         _ => new KnownUserRepository());
            Replace<IDbMonitorRepository>(services,    _ => new AuthTestStubs.StubDbMonitorRepository());
            Replace<INavigationRepository>(services,   _ => new NavigationStub());
            Replace<INavigationMenuRepository>(services, _ => new MenuStub());
            Replace<IContentEntryRepository>(services, _ => new PublishedSlugStub(_publishedSlug));
            services.AddSingleton<ISiteSettingsService>(
                StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.AuthAutoProvisionUsers, _autoProvision)
                    .WithJson(SiteSettingKeys.RedirectsAllowedExternalHosts, _allowedHosts));
        });
    }

    private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory)
        where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddScoped<T>(factory);
    }

    /// <summary>Knows exactly one user (KnownUpn) — anyone else is "not provisioned".</summary>
    private sealed class KnownUserRepository : IUserRepository
    {
        private readonly User _known = new() { Id = 1, ExternalId = KnownUpn, Email = KnownUpn, DisplayName = "Known", IsActive = true };
        public Task<long> UpsertAsync(string externalId, string email, string displayName) => Task.FromResult(1L);
        public Task<User?> GetByIdAsync(long id) => Task.FromResult<User?>(id == 1 ? _known : null);
        public Task<User?> GetByExternalIdAsync(string externalId)
            => Task.FromResult<User?>(string.Equals(externalId, KnownUpn, StringComparison.OrdinalIgnoreCase) ? _known : null);
        public Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId)
            => Task.FromResult<IEnumerable<UserRoleAssignment>>([new UserRoleAssignment { RoleId = 1, RoleName = CmsRoles.Editor }]);
    }

    private sealed class PublishedSlugStub(string? publishedSlug) : Issue23ContentEntryStub
    {
        public override Task<PublishedContentEntry?> GetPublishedBySlugAsync(string slug, string locale = "en-US")
            => Task.FromResult(publishedSlug is not null && string.Equals(slug, publishedSlug, StringComparison.OrdinalIgnoreCase)
                ? new PublishedContentEntry { Id = 1, Slug = slug }
                : null);
    }

    private sealed class MenuStub : INavigationMenuRepository
    {
        public Task<NavigationMenu?> GetByHandleAsync(string handle) => Task.FromResult<NavigationMenu?>(null);
        public Task<IReadOnlyList<NavigationMenu>> ListAllAsync() => Task.FromResult<IReadOnlyList<NavigationMenu>>([]);
        public Task<long> CreateAsync(string name, string handle) => Task.FromResult(1L);
        public Task UpdateAsync(long id, string name) => Task.CompletedTask;
        public Task DeleteAsync(long id) => Task.CompletedTask;
    }

    private sealed class NavigationStub : INavigationRepository
    {
        private readonly List<RedirectAdminRow> _redirects = new();
        public Task<IEnumerable<NavigationItem>> GetMenuTreeAsync(string handle) => Task.FromResult<IEnumerable<NavigationItem>>([]);
        public Task<IEnumerable<NavigationItem>> GetMenuTreeAdminAsync(string handle) => Task.FromResult<IEnumerable<NavigationItem>>([]);
        public Task<NavigationItem?> GetItemAsync(long id) => Task.FromResult<NavigationItem?>(null);
        public Task<long> UpsertItemAsync(NavigationItem item) => Task.FromResult(1L);
        public Task DeleteItemAsync(long id) => Task.CompletedTask;
        public Task BulkReorderAsync(long menuId, string itemsJson) => Task.CompletedTask;
        public Task<Redirect?> GetRedirectByPathAsync(string fromPath) => Task.FromResult<Redirect?>(null);
        public Task<long> CreateRedirectAsync(Redirect redirect)
        {
            var id = _redirects.Count + 1;
            _redirects.Add(new RedirectAdminRow { Id = id, FromPath = redirect.FromPath, ToPath = redirect.ToPath, StatusCode = redirect.StatusCode, IsActive = true, CreatedById = redirect.CreatedById, CreatedAt = DateTime.UtcNow });
            return Task.FromResult<long>(id);
        }
        public Task<(IReadOnlyList<RedirectAdminRow> Rows, int TotalRows)> ListRedirectsAsync(bool? isActive = null, int page = 1, int pageSize = 50)
            => Task.FromResult<(IReadOnlyList<RedirectAdminRow>, int)>((_redirects, _redirects.Count));
        public Task<RedirectAdminRow?> GetRedirectByIdAsync(long id) => Task.FromResult(_redirects.FirstOrDefault(r => r.Id == id));
        public Task UpdateRedirectAsync(long id, string fromPath, string toPath, int statusCode) => Task.CompletedTask;
        public Task DeactivateRedirectAsync(long id) => Task.CompletedTask;
    }
}
