using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VA.CMS.API;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers;
using VA.CMS.API.Middleware;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #164 (session policy per VA 6500) and the host-level halves
/// of #163 (rotation on refresh, session-version guard) and #165 (auth events, 403 audit):
///   - DevBypass, fake Negotiate and fake OIDC refuse to start outside Development;
///     an empty DevBypassAllowedUsers refuses to start.
///   - GET /api/auth/login without ack=1 lands on the SPA /login page (system-use notice).
///   - The refresh cookie is Secure outside Development; HTTPS availability is validated.
///   - POST /api/auth/refresh rotates the cookie; GET is not an endpoint any more;
///     idle and replayed sessions are refused.
///   - A token whose "sv" claim is behind User.SessionVersion — or whose user is inactive — is refused.
///   - Logon / Refresh / Logoff / failures and policy denials are audit rows with Outcome.
/// </summary>
public class Issue164AcceptanceTests
{
    // ── Startup guards ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void DevBypass_Refuses_To_Start_Outside_Development(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var f = new SessionFactory(environment: environment, mode: "DevBypass");
            f.CreateClient();
        });
        Assert.Contains("DevBypass", ex.Message);
        Assert.Contains(environment, ex.Message);
    }

    [Fact]
    public void DevBypass_Refuses_Empty_Allowed_Users()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var f = new SessionFactory(mode: "DevBypass", allowedUsers: false);
            f.CreateClient();
        });
        Assert.Contains("DevBypassAllowedUsers", ex.Message);
    }

    [Fact]
    public void Fake_Negotiate_Refuses_To_Start_Outside_Development()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var f = new SessionFactory(environment: "Staging", fakeNegotiate: true);
            f.CreateClient();
        });
        Assert.Contains("WINDOWS_AUTH_FAKE_NEGOTIATE", ex.Message);
    }

    [Fact]
    public void Https_Availability_Is_Checked_Outside_Development()
    {
        static IConfiguration Cfg(params (string, string?)[] kv) =>
            new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(p => p.Item1, p => p.Item2)).Build();

        Assert.Null(HostHardeningOptions.ValidateHttpsAvailable(Cfg(("urls", "http://0.0.0.0:5100")), isDevelopment: true));
        Assert.Null(HostHardeningOptions.ValidateHttpsAvailable(Cfg(), isDevelopment: false));                       // IIS in-process: no urls
        Assert.Null(HostHardeningOptions.ValidateHttpsAvailable(Cfg(("urls", "https://0.0.0.0:5101")), isDevelopment: false));
        Assert.Null(HostHardeningOptions.ValidateHttpsAvailable(Cfg(("urls", "http://0.0.0.0:5100"), ("ForwardedHeaders:KnownProxies:0", "10.0.0.1")), isDevelopment: false));
        Assert.NotNull(HostHardeningOptions.ValidateHttpsAvailable(Cfg(("urls", "http://0.0.0.0:5100")), isDevelopment: false));
    }

    // ── Secure cookies ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Staging",     true)]
    [InlineData("Production",  true)]
    public void Refresh_Cookie_Is_Secure_Everywhere_But_Development(string environment, bool secure)
    {
        var env = new FakeEnv(environment);
        Assert.Equal(secure, AuthCookieHelper.SecureFor(env));
        Assert.Equal(secure, AuthCookieHelper.BuildCookieOptions(AuthCookieHelper.SecureFor(env)).Secure);
        Assert.Equal(secure, AuthCookieHelper.BuildExpiryCookieOptions(AuthCookieHelper.SecureFor(env)).Secure);
    }

    // ── System-use notice gate (AC-8) ──────────────────────────────────────────

    [Fact]
    public async Task Login_Without_Acknowledgement_Goes_To_The_Spa_Login_Page()
    {
        await using var f = new SessionFactory();
        var client = f.CreateClient(NoRedirect());

        var resp = await client.GetAsync("/api/auth/login?returnUrl=%2Fadmin%2Fcontent");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fadmin%2Fcontent", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_With_Acknowledgement_Proceeds_And_The_Logon_Row_Records_It()
    {
        await using var f = new SessionFactory();
        var client = f.CreateClient(NoRedirect(handleCookies: true));
        client.DefaultRequestHeaders.Add(FakeNegotiateHandler.TestUpnHeader, "alice@va.gov");

        var login = await client.GetAsync("/api/auth/login?returnUrl=%2Fadmin%2Fcontent&ack=1");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("ack=1", login.Headers.Location!.ToString());

        var win = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, win.StatusCode);
        Assert.Contains(win.Headers.GetValues("Set-Cookie"), c => c.StartsWith($"{AuthCookieHelper.RefreshTokenCookieName}="));

        var logon = Assert.Single(AuthTestStubs.AuditWrites(f.Services), w => w.Action == AuthAudit.Logon);
        Assert.Equal(AuthAudit.EntityType, logon.EntityType);
        Assert.Equal(AuthTestStubs.ActiveUserId, logon.ActorId);
        Assert.Contains("\"systemUseAcknowledged\":true", logon.DiffJson);
        Assert.Contains("\"mode\":\"WindowsAuth\"", logon.DiffJson);
    }

    [Fact]
    public void System_Use_Notice_Is_A_Public_Setting_With_The_VA_Wording()
    {
        var def = SiteSettingDefinitions.All.Single(d => d.Key == SiteSettingKeys.AuthSystemUseNotice);
        Assert.Equal(SiteSettingScope.Public, def.Scope);   // the anonymous login page must be able to read it
        Assert.StartsWith("This is a U.S. Government computer system", def.Default);
        Assert.Equal(SiteSettingScope.Admin, SiteSettingDefinitions.All.Single(d => d.Key == SiteSettingKeys.AuthIdleTimeoutMinutes).Scope);
    }

    // ── Refresh: POST, rotation, idle, replay ──────────────────────────────────

    [Fact]
    public async Task Refresh_Is_Not_A_Get_Endpoint()
    {
        await using var f = new SessionFactory();
        var store = f.Services.GetRequiredService<InMemoryRefreshTokenService>();
        var req   = new HttpRequestMessage(HttpMethod.Get, "/api/auth/refresh");
        req.Headers.Add("Cookie", $"{AuthCookieHelper.RefreshTokenCookieName}={await store.IssueAsync(AuthTestStubs.ActiveUserId)}");

        var resp = await f.CreateClient(NoRedirect()).SendAsync(req);

        // No GET action matches, so the default-deny fallback policy answers (401) — either
        // way a GET with a valid cookie never mints a token.
        Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized });
        Assert.DoesNotContain("accessToken", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_Rotates_The_Cookie_And_Audits()
    {
        await using var f = new SessionFactory();
        var client = f.CreateClient(NoRedirect());
        var store  = f.Services.GetRequiredService<InMemoryRefreshTokenService>();
        var first  = await store.IssueAsync(AuthTestStubs.ActiveUserId);

        var resp = await client.SendAsync(WithCookie(first));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var setCookie = resp.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{AuthCookieHelper.RefreshTokenCookieName}="));
        var second = setCookie.Split(';')[0].Split('=', 2)[1];
        Assert.NotEqual(first, second);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.True((await store.ValidateAsync(second)).Ok);

        var body = await resp.Content.ReadFromJsonAsync<TokenBody>();
        Assert.NotNull(body?.accessToken);
        Assert.Equal(15 * 60, body!.expiresIn);

        Assert.Contains(AuthTestStubs.AuditWrites(f.Services), w => w.Action == AuthAudit.Refresh && w.ActorId == AuthTestStubs.ActiveUserId);
    }

    [Fact]
    public async Task Replayed_Refresh_Token_Is_Refused_And_Audited_As_Failure()
    {
        await using var f = new SessionFactory(grace: 0);
        var client = f.CreateClient(NoRedirect());
        var store  = f.Services.GetRequiredService<InMemoryRefreshTokenService>();
        var first  = await store.IssueAsync(AuthTestStubs.ActiveUserId);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(WithCookie(first))).StatusCode);
        store.SetRevokedAt(first, DateTime.UtcNow.AddMinutes(-1));

        var replay = await client.SendAsync(WithCookie(first));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains("reuse detected", await replay.Content.ReadAsStringAsync());

        var row = Assert.Single(AuthTestStubs.AuditWrites(f.Services), w => w.Action == AuthAudit.RefreshReplay);
        Assert.Equal(AuditOutcome.Failure, row.Outcome);
    }

    [Fact]
    public async Task Idle_Session_Cannot_Refresh()
    {
        await using var f = new SessionFactory();
        var client = f.CreateClient(NoRedirect());
        var store  = f.Services.GetRequiredService<InMemoryRefreshTokenService>();
        var token  = await store.IssueAsync(AuthTestStubs.ActiveUserId);
        store.SetLastUsed(token, DateTime.UtcNow.AddMinutes(-30));

        var resp = await client.SendAsync(WithCookie(token));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Contains("inactivity", await resp.Content.ReadAsStringAsync());
        // and the stale cookie is cleared
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), c => c.StartsWith($"{AuthCookieHelper.RefreshTokenCookieName}=;"));
        Assert.Contains(AuthTestStubs.AuditWrites(f.Services), w => w.Action == AuthAudit.RefreshFailure && w.Outcome == AuditOutcome.Failure);
    }

    [Fact]
    public async Task Logout_Revokes_And_Audits_Logoff()
    {
        await using var f = new SessionFactory();
        var client = f.CreateClient(NoRedirect());
        var store  = f.Services.GetRequiredService<InMemoryRefreshTokenService>();
        var token  = await store.IssueAsync(AuthTestStubs.ActiveUserId);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        req.Headers.Add("Cookie", $"{AuthCookieHelper.RefreshTokenCookieName}={token}");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);

        Assert.Contains(AuthTestStubs.AuditWrites(f.Services), w => w.Action == AuthAudit.Logoff && w.ActorId == AuthTestStubs.ActiveUserId);
    }

    // ── Session-version guard (#163) ───────────────────────────────────────────

    [Fact]
    public async Task Access_Token_Minted_Before_A_Revocation_Is_Refused()
    {
        await using var f = new SessionFactory(revocationCheckSeconds: 0);
        var users = f.Users;
        var jwt   = f.Services.GetRequiredService<IJwtService>();

        var stale = jwt.IssueAccessToken(users.Active, [new UserRoleAssignment { RoleId = 1, RoleName = CmsRoles.SystemAdmin }]);
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().SendAsync(Bearer(stale, "/api/v1/admin/health/db"))).StatusCode);

        users.Active.SessionVersion++;   // what usp_RefreshToken_RevokeAllForUser does
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().SendAsync(Bearer(stale, "/api/v1/admin/health/db"))).StatusCode);

        var fresh = jwt.IssueAccessToken(users.Active, [new UserRoleAssignment { RoleId = 1, RoleName = CmsRoles.SystemAdmin }]);
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().SendAsync(Bearer(fresh, "/api/v1/admin/health/db"))).StatusCode);

        users.Active.IsActive = false;   // deactivation ends the session too
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().SendAsync(Bearer(fresh, "/api/v1/admin/health/db"))).StatusCode);
    }

    [Fact]
    public async Task Guard_Caches_Per_Node_And_Invalidate_Drops_The_Entry()
    {
        var settings = StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthRevocationCheckSeconds, 300);
        var guard    = new SessionRevocationGuard(settings);
        var users    = new MutableUserRepository();
        var jwt      = new JwtService(new JwtOptions { SigningKey = "issue-164-guard-signing-key-32ch!" });
        var principal = jwt.ValidateToken(jwt.IssueAccessToken(users.Active, []))!;

        Assert.True(await guard.IsCurrentAsync(principal, users));
        users.Active.SessionVersion++;
        Assert.True(await guard.IsCurrentAsync(principal, users));    // cached snapshot still says current
        guard.Invalidate(users.Active.Id);
        Assert.False(await guard.IsCurrentAsync(principal, users));   // re-read
    }

    // ── 403 audit (#165) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Policy_Denial_Is_Audited_Once_With_The_Policy_Name()
    {
        await using var f = new SessionFactory();
        var jwt   = f.Services.GetRequiredService<IJwtService>();
        var token = jwt.IssueAccessToken(f.Users.Active, [new UserRoleAssignment { RoleId = 1, RoleName = CmsRoles.Editor }]);

        var resp = await f.CreateClient().SendAsync(Bearer(token, "/api/v1/admin/audit"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var denied = Assert.Single(AuthTestStubs.AuditWrites(f.Services), w => w.Action == AuditingAuthorizationResultHandler.Action);
        Assert.Equal(AuditOutcome.Failure, denied.Outcome);
        Assert.Equal(AuthTestStubs.ActiveUserId, denied.ActorId);
        Assert.Contains(CmsRoles.Policies.CanAdminSystem, denied.DiffJson);
        Assert.Contains("/api/v1/admin/audit", denied.DiffJson);
    }

    [Fact]
    public async Task Correlation_Id_Is_Echoed_On_The_Response()
    {
        await using var f = new SessionFactory();
        var client = f.CreateClient();

        var anon = await client.GetAsync("/health");
        Assert.NotEmpty(anon.Headers.GetValues(AuditContextMiddleware.CorrelationHeader).Single());

        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add(AuditContextMiddleware.CorrelationHeader, "lb-abc-123 <script>");   // untrusted: only token chars survive
        var echoed = await client.SendAsync(req);
        Assert.Equal("lb-abc-123script", echoed.Headers.GetValues(AuditContextMiddleware.CorrelationHeader).Single());
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private sealed record TokenBody(string accessToken, int expiresIn, string tokenType);

    private static WebApplicationFactoryClientOptions NoRedirect(bool handleCookies = false)
        => new() { AllowAutoRedirect = false, HandleCookies = handleCookies };

    private static HttpRequestMessage WithCookie(string refreshToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        req.Headers.Add("Cookie", $"{AuthCookieHelper.RefreshTokenCookieName}={refreshToken}");
        return req;
    }

    private static HttpRequestMessage Bearer(string token, string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>User store whose single user can be mutated by a test (session version, active flag).</summary>
    internal sealed class MutableUserRepository : IUserRepository
    {
        public User Active { get; } = new()
        {
            Id = AuthTestStubs.ActiveUserId, ExternalId = "alice@va.gov", Email = "alice@va.gov", DisplayName = "Alice", IsActive = true,
        };

        public Task<long> UpsertAsync(string externalId, string email, string displayName) => Task.FromResult(Active.Id);
        public Task<User?> GetByIdAsync(long id) => Task.FromResult<User?>(id == Active.Id ? Active : null);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult<User?>(Active.ExternalId == externalId ? Active : null);
        public Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId)
            => Task.FromResult<IEnumerable<UserRoleAssignment>>([new UserRoleAssignment { RoleId = 1, RoleName = CmsRoles.Editor }]);
    }

    private sealed class SessionFactory(
        string environment = "Development",
        string mode = "WindowsAuth",
        bool allowedUsers = true,
        bool? fakeNegotiate = null,
        int grace = 30,
        int revocationCheckSeconds = 30) : WebApplicationFactory<Program>
    {
        public MutableUserRepository Users { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Auth:Mode", mode);
            if (allowedUsers)
                builder.UseSetting("Auth:DevBypassAllowedUsers:0", "alice@va.gov");
            if (fakeNegotiate ?? environment == "Development")
                builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
            builder.UseSetting("Jwt:SigningKey",  "issue-164-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(IUserRepository) || d.ServiceType == typeof(IDbMonitorRepository)).ToList())
                    services.Remove(d);
                services.AddSingleton<IUserRepository>(Users);
                services.AddScoped<IDbMonitorRepository>(_ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.AuthRefreshRotationGraceSeconds, grace)
                    .With(SiteSettingKeys.AuthRevocationCheckSeconds, revocationCheckSeconds));
            });
        }
    }
}
