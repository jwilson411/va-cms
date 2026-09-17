using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #154: the Azure AD login flow end to end against
/// the fake OIDC handler (AZUREAD_FAKE_OIDC=true).
///
///   login → /signin-oidc (fake AAD) → /api/auth/callback → 302 into the SPA
///   (only the cms_rt cookie set, no token in URL/body) → refresh → logout →
///   /api/auth/signout → AAD end-session redirect.
/// </summary>
public class Issue154AcceptanceTests
{
    private const string AdminGroup = "VA-CMS-Admins";

    // ── login → callback → SPA redirect ───────────────────────────────────────

    [Fact]
    public async Task Login_Challenges_Oidc_With_Callback_As_RedirectUri()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect());

        var resp = await client.GetAsync("/api/auth/login?returnUrl=%2Fcontent%2F42&ack=1");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location!.ToString();
        Assert.StartsWith(AzureAdSchemes.CallbackPath, location);
        Assert.Contains(Uri.EscapeDataString("/api/auth/callback?returnUrl=%2Fcontent%2F42"), location);
    }

    [Fact]
    public async Task Login_Drops_NonLocal_ReturnUrl()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect());

        var resp = await client.GetAsync("/api/auth/login?returnUrl=https%3A%2F%2Fevil.example%2Fphish&ack=1");

        var location = Uri.UnescapeDataString(resp.Headers.Location!.ToString());
        Assert.DoesNotContain("evil.example", location);
    }

    [Fact]
    public async Task Callback_Sets_Refresh_Cookie_And_Redirects_To_ReturnUrl_Without_Token()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader, "alice@va.gov");

        var callbackResp = await FollowLoginToCallbackAsync(client, "/content/42");

        Assert.Equal(HttpStatusCode.Redirect, callbackResp.StatusCode);
        Assert.Equal("/content/42", callbackResp.Headers.Location!.ToString());

        var setCookies = callbackResp.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookies, c => c.StartsWith($"{AuthCookieHelper.RefreshTokenCookieName}=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        var body = await callbackResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body);
        Assert.DoesNotContain("accessToken", callbackResp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_Falls_Back_To_Root_For_NonLocal_ReturnUrl()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader, "alice@va.gov");

        // Bypass /login's own validation and hit the callback with a hostile returnUrl directly.
        await client.GetAsync($"{AzureAdSchemes.CallbackPath}?state=%2Fapi%2Fauth%2Fcallback");
        var resp = await client.GetAsync("/api/auth/callback?returnUrl=//evil.example/phish");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_Without_Aad_Session_Returns_401()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect());

        var resp = await client.GetAsync("/api/auth/callback");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── refresh after callback ────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_After_Callback_Issues_Jwt_With_GroupMapped_Role()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader,    "alice@va.gov");
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestGroupsHeader, AdminGroup);

        await FollowLoginToCallbackAsync(client, "/");

        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var token = (await refresh.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        var principal = factory.Services.GetRequiredService<IJwtService>().ValidateToken(token);
        var roles = principal!.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();

        Assert.Contains(CmsRoles.Editor,      roles);   // explicit (stub user repo)
        Assert.Contains(CmsRoles.SystemAdmin, roles);   // via AAD "groups" claim persisted with the session
    }

    // ── logout → AAD sign-out ─────────────────────────────────────────────────

    [Fact]
    public async Task Logout_Returns_SignOutUrl_And_Revokes_Refresh_Session()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader, "alice@va.gov");
        await FollowLoginToCallbackAsync(client, "/");

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        var body = await logout.Content.ReadFromJsonAsync<LogoutResponse>();
        Assert.Equal("/api/auth/signout", body!.SignOutUrl);

        // cms_rt is revoked server-side and expired in the browser
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task SignOut_Redirects_To_Aad_EndSession_With_Post_Logout_Redirect()
    {
        await using var factory = new Issue154TestFactory();
        var client = factory.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader, "alice@va.gov");
        await FollowLoginToCallbackAsync(client, "/");

        var resp = await client.GetAsync("/api/auth/signout");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location!.ToString();
        Assert.StartsWith(FakeAzureAdHandler.EndSessionEndpoint, location);
        Assert.Contains("post_logout_redirect_uri=", location);
        Assert.Contains(Uri.EscapeDataString("/login"), location);

        // AAD session cookie cleared alongside the redirect
        var setCookies = resp.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookies, c => c.StartsWith($"{AzureAdSchemes.CookieName}=") && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Logout_With_AadSignOut_Disabled_Clears_Aad_Cookie_And_Returns_No_Url()
    {
        await using var factory = new Issue154TestFactory(aadSignOut: false);
        var client = factory.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeAzureAdHandler.TestUpnHeader, "alice@va.gov");
        await FollowLoginToCallbackAsync(client, "/");

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        var body   = await logout.Content.ReadFromJsonAsync<LogoutResponse>();
        Assert.Null(body!.SignOutUrl);

        var setCookies = logout.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookies, c => c.StartsWith($"{AzureAdSchemes.CookieName}="));
    }

    // ── mode guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_And_SignOut_Return_404_Outside_AzureAd_Mode()
    {
        await using var factory = new Issue154TestFactory(mode: AuthMode.WindowsAuth);
        var client = factory.CreateClient(NoRedirect());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/callback")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/signout")).StatusCode);
    }

    [Fact]
    public void Startup_Rejects_CallbackPath_Under_Api()
    {
        var factory = new Issue154TestFactory(callbackPath: "/api/auth/callback");
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("AzureAd:CallbackPath", ex!.ToString());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed record TokenResponse(string AccessToken, int ExpiresIn, string TokenType);
    private sealed record LogoutResponse(string? SignOutUrl);

    private static WebApplicationFactoryClientOptions NoRedirect(bool handleCookies = false)
        => new() { AllowAutoRedirect = false, HandleCookies = handleCookies };

    /// <summary>Walks login → fake AAD → callback by hand so each hop can be asserted; returns the callback response.</summary>
    private static async Task<HttpResponseMessage> FollowLoginToCallbackAsync(HttpClient client, string returnUrl)
    {
        var login = await client.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}&ack=1");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var aad = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, aad.StatusCode);
        Assert.StartsWith("/api/auth/callback", aad.Headers.Location!.ToString());

        return await client.GetAsync(aad.Headers.Location);
    }
}

// ── Test factory ──────────────────────────────────────────────────────────────

public sealed class Issue154TestFactory : WebApplicationFactory<Program>
{
    private readonly AuthMode _mode;
    private readonly bool     _aadSignOut;
    private readonly bool     _autoProvision;
    private readonly string?  _callbackPath;

    public Issue154TestFactory(
        AuthMode mode = AuthMode.AzureAd, bool aadSignOut = true, bool autoProvision = true, string? callbackPath = null)
    {
        _mode          = mode;
        _aadSignOut    = aadSignOut;
        _autoProvision = autoProvision;
        _callbackPath  = callbackPath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var mappings = new InMemoryAdGroupMappingRepository();
        mappings.UpsertAsync("VA-CMS-Admins", roleId: 1, createdById: 1).GetAwaiter().GetResult();
        mappings.AddRoleName(1, CmsRoles.SystemAdmin);

        builder.UseSetting("SKIP_MIGRATIONS",    "true");
        builder.UseSetting("Auth:Mode",          _mode.ToString());
        builder.UseSetting("AZUREAD_FAKE_OIDC",  "true");
        builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
        builder.UseSetting("Jwt:SigningKey",     "issue-154-acceptance-key-32chars!");
        builder.UseSetting("Jwt:Issuer",         "va-cms-api");
        builder.UseSetting("Jwt:Audience",       "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        if (_callbackPath is not null)
            builder.UseSetting("AzureAd:CallbackPath", _callbackPath);
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IUserRepository>(services,           _ => new AuthTestStubs.StubUserRepository());
            Replace<IDbMonitorRepository>(services,      _ => new AuthTestStubs.StubDbMonitorRepository());
            AuthTestStubs.UseInMemoryAuth(services);
            Replace<IAdGroupMappingRepository>(services, _ => mappings);
            services.AddSingleton<ISiteSettingsService>(
                StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.AuthAzureAdSignOut,     _aadSignOut)
                    .With(SiteSettingKeys.AuthAutoProvisionUsers, _autoProvision));
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
