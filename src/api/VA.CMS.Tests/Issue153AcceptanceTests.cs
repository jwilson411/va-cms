using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #153: refresh must not honour X-Dev-Groups outside
/// DevBypass+Development, group-mapped roles must survive a refresh, WindowsAuth
/// login must apply group mappings, and expiresIn must follow auth.accessTokenMinutes.
///
/// The stub user has one explicit role (Editor). A mapping "VA-CMS-Admins" →
/// SystemAdmin is installed so a forged/valid group claim is observable in the JWT.
/// </summary>
public class Issue153AcceptanceTests
{
    private const string AdminGroup   = "VA-CMS-Admins";
    private const string AdminSid     = "S-1-5-21-1111-2222-3333-5001";
    private const long   SystemAdminRoleId = 1;

    // ── AC1: X-Dev-Groups is ignored unless DevBypass + Development ───────────

    [Fact]
    public async Task AzureAd_Refresh_Ignores_Forged_XDevGroups_Header()
    {
        await using var factory = new Issue153TestFactory(AuthMode.AzureAd);
        var roles = await RefreshRolesAsync(factory, loginGroups: [], devGroupsHeader: AdminGroup);

        Assert.Contains(CmsRoles.Editor, roles);
        Assert.DoesNotContain(CmsRoles.SystemAdmin, roles);
    }

    [Fact]
    public async Task WindowsAuth_Refresh_Ignores_Forged_XDevGroups_Header()
    {
        await using var factory = new Issue153TestFactory(AuthMode.WindowsAuth);
        var roles = await RefreshRolesAsync(factory, loginGroups: [], devGroupsHeader: AdminGroup);

        Assert.DoesNotContain(CmsRoles.SystemAdmin, roles);
    }

    [Fact]
    public async Task DevBypass_Outside_Development_Ignores_XDevGroups_Header()
    {
        // DevBypass is permitted in Staging (only Production refuses it), but the
        // header must still be inert there.
        await using var factory = new Issue153TestFactory(AuthMode.DevBypass, environment: "Staging");
        var roles = await RefreshRolesAsync(factory, loginGroups: [], devGroupsHeader: AdminGroup);

        Assert.DoesNotContain(CmsRoles.SystemAdmin, roles);
    }

    [Fact]
    public async Task DevBypass_In_Development_Honours_XDevGroups_Header()
    {
        await using var factory = new Issue153TestFactory(AuthMode.DevBypass, environment: "Development");
        var roles = await RefreshRolesAsync(factory, loginGroups: [], devGroupsHeader: AdminGroup);

        Assert.Contains(CmsRoles.SystemAdmin, roles);
    }

    // ── AC2: login-time groups persist with the session and are re-resolved ───

    [Fact]
    public async Task AzureAd_GroupMapped_Role_Survives_Refresh()
    {
        await using var factory = new Issue153TestFactory(AuthMode.AzureAd);
        var roles = await RefreshRolesAsync(factory, loginGroups: [AdminGroup], devGroupsHeader: null);

        Assert.Contains(CmsRoles.Editor,      roles);
        Assert.Contains(CmsRoles.SystemAdmin, roles);
    }

    [Fact]
    public async Task AzureAd_Refresh_Applies_Mapping_Changes_Made_After_Login()
    {
        await using var factory = new Issue153TestFactory(AuthMode.AzureAd, installMapping: false);

        // Session captured the group at login, but no mapping existed yet.
        var before = await RefreshRolesAsync(factory, loginGroups: [AdminGroup], devGroupsHeader: null);
        Assert.DoesNotContain(CmsRoles.SystemAdmin, before);

        // Admin adds the mapping; the next refresh of the same session picks it up.
        await factory.Mappings.UpsertAsync(AdminGroup, SystemAdminRoleId, createdById: 1);
        factory.Mappings.AddRoleName(SystemAdminRoleId, CmsRoles.SystemAdmin);

        var after = await RefreshRolesAsync(factory, loginGroups: [AdminGroup], devGroupsHeader: null);
        Assert.Contains(CmsRoles.SystemAdmin, after);
    }

    // ── AC3: WindowsAuth login resolves GroupSid claims and applies mappings ──

    [Fact]
    public async Task WindowsLogin_Applies_GroupSid_Mappings_And_Persists_Them_For_Refresh()
    {
        await using var factory = new Issue153TestFactory(AuthMode.WindowsAuth, mappedGroup: AdminSid);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies     = true,
        });

        var login = new HttpRequestMessage(HttpMethod.Get, "/api/auth/windows-login");
        login.Headers.Add(FakeNegotiateHandler.TestUpnHeader,       "alice@va.gov");
        login.Headers.Add(FakeNegotiateHandler.TestGroupSidsHeader, $"S-1-5-21-1111-2222-3333-513,{AdminSid}");

        var loginResp = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var loginRoles = RolesOf(factory, (await loginResp.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);
        Assert.Contains(CmsRoles.SystemAdmin, loginRoles);

        // The cookie jar carries cms_rt; the refresh must keep the group-mapped role.
        var refreshResp = await client.GetAsync("/api/auth/refresh");
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

        var refreshRoles = RolesOf(factory, (await refreshResp.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);
        Assert.Contains(CmsRoles.SystemAdmin, refreshRoles);
    }

    [Fact]
    public async Task WindowsLogin_Without_Mapped_Group_Has_Only_Explicit_Roles()
    {
        await using var factory = new Issue153TestFactory(AuthMode.WindowsAuth, mappedGroup: AdminSid);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = new HttpRequestMessage(HttpMethod.Get, "/api/auth/windows-login");
        login.Headers.Add(FakeNegotiateHandler.TestUpnHeader,       "alice@va.gov");
        login.Headers.Add(FakeNegotiateHandler.TestGroupSidsHeader, "S-1-5-21-1111-2222-3333-513");

        var resp  = await client.SendAsync(login);
        var roles = RolesOf(factory, (await resp.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);

        Assert.Equal([CmsRoles.Editor], roles);
    }

    // ── AC4: expiresIn follows auth.accessTokenMinutes ────────────────────────

    [Fact]
    public async Task Refresh_ExpiresIn_Reflects_AccessTokenMinutes_Setting()
    {
        await using var factory = new Issue153TestFactory(AuthMode.AzureAd, accessTokenMinutes: 45);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var token = factory.Services.GetRequiredService<IRefreshTokenService>().Issue(AuthTestStubs.ActiveUserId);
        var req   = new HttpRequestMessage(HttpMethod.Get, "/api/auth/refresh");
        req.Headers.Add("Cookie", $"{AuthCookieHelper.RefreshTokenCookieName}={token}");

        var body = await (await client.SendAsync(req)).Content.ReadFromJsonAsync<TokenResponse>();

        Assert.Equal(45 * 60, body!.ExpiresIn);
    }

    [Fact]
    public async Task WindowsLogin_ExpiresIn_Reflects_AccessTokenMinutes_Setting()
    {
        await using var factory = new Issue153TestFactory(AuthMode.WindowsAuth, accessTokenMinutes: 30);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = new HttpRequestMessage(HttpMethod.Get, "/api/auth/windows-login");
        login.Headers.Add(FakeNegotiateHandler.TestUpnHeader, "alice@va.gov");

        var body = await (await client.SendAsync(login)).Content.ReadFromJsonAsync<TokenResponse>();

        Assert.Equal(30 * 60, body!.ExpiresIn);
    }

    // ── Resolver: Negotiate claim extraction ──────────────────────────────────

    [Fact]
    public void ExtractGroups_Collects_Aad_Groups_And_Negotiate_GroupSids()
    {
        var resolver = new AdGroupRoleResolver(new InMemoryAdGroupMappingRepository());

        var aad = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("groups", "oid-1"), new Claim("groups", "oid-2"), new Claim(ClaimTypes.Role, "not-a-group")],
            "AzureAd"));
        Assert.Equal(["oid-1", "oid-2"], resolver.ExtractGroups(aad));

        var negotiate = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.GroupSid, AdminSid), new Claim(ClaimTypes.Role, "DOMAIN\\CMS Admins")],
            "Negotiate"));
        var groups = resolver.ExtractGroups(negotiate);
        Assert.Contains(AdminSid, groups);
        Assert.Contains("DOMAIN\\CMS Admins", groups);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed record TokenResponse(string AccessToken, int ExpiresIn, string TokenType);

    private static async Task<List<string>> RefreshRolesAsync(
        Issue153TestFactory factory, string[] loginGroups, string? devGroupsHeader)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var token = factory.Services.GetRequiredService<IRefreshTokenService>()
            .Issue(AuthTestStubs.ActiveUserId, loginGroups);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/refresh");
        req.Headers.Add("Cookie", $"{AuthCookieHelper.RefreshTokenCookieName}={token}");
        if (devGroupsHeader is not null)
            req.Headers.Add("X-Dev-Groups", devGroupsHeader);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return RolesOf(factory, body!.AccessToken);
    }

    private static List<string> RolesOf(Issue153TestFactory factory, string accessToken)
    {
        var principal = factory.Services.GetRequiredService<IJwtService>().ValidateToken(accessToken);
        Assert.NotNull(principal);
        return principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
    }
}

// ── Test factory ──────────────────────────────────────────────────────────────

public sealed class Issue153TestFactory : WebApplicationFactory<Program>
{
    private readonly AuthMode _mode;
    private readonly string   _environment;
    private readonly bool     _installMapping;
    private readonly string   _mappedGroup;
    private readonly int?     _accessTokenMinutes;

    public InMemoryAdGroupMappingRepository Mappings { get; } = new();

    public Issue153TestFactory(
        AuthMode mode,
        string   environment        = "Development",
        bool     installMapping     = true,
        string   mappedGroup        = "VA-CMS-Admins",
        int?     accessTokenMinutes = null)
    {
        _mode               = mode;
        _environment        = environment;
        _installMapping     = installMapping;
        _mappedGroup        = mappedGroup;
        _accessTokenMinutes = accessTokenMinutes;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_installMapping)
        {
            Mappings.UpsertAsync(_mappedGroup, roleId: 1, createdById: 1).GetAwaiter().GetResult();
            Mappings.AddRoleName(1, CmsRoles.SystemAdmin);
        }

        builder.UseEnvironment(_environment);
        builder.UseSetting("SKIP_MIGRATIONS", "true");
        builder.UseSetting("Auth:Mode",       _mode.ToString());
        builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
        builder.UseSetting("Jwt:SigningKey",  "issue-153-acceptance-key-32chars!");
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
            Replace<IUserRepository>(services,           _ => new AuthTestStubs.StubUserRepository());
            Replace<IDbMonitorRepository>(services,      _ => new AuthTestStubs.StubDbMonitorRepository());
            Replace<IAdGroupMappingRepository>(services, _ => Mappings);

            if (_accessTokenMinutes is { } minutes)
            {
                services.AddSingleton<ISiteSettingsService>(
                    StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthAccessTokenMinutes, minutes));
            }
        });
    }

    private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory)
        where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddScoped<T>(factory);
    }
}
