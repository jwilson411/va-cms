using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #68: DevBypass auth mode for local development.
///
/// AC1: Auth:Mode=DevBypass setting in appsettings.Development.json enables the bypass.
///   → The test factory boots the host with Auth:Mode=DevBypass and verifies
///     that the mode is registered without error.
///
/// AC2: When enabled, API accepts X-Dev-User header and issues a valid JWT.
///   → POST /api/auth/dev-login with X-Dev-User: alice@va.gov returns 200
///     with an accessToken.
///   → Subsequent request with that token succeeds on a protected endpoint.
///
/// AC3: DevBypass mode refuses to start in Production.
///   → Verified by unit-testing the Program.cs guard logic (IWebHostEnvironment stub).
///     (A full integration test that boots the app in Production with DevBypass would
///     throw during WebApplicationFactory construction — verified via exception test.)
///
/// AC4: CI pipeline can obtain a token via X-Dev-User header on integration tests.
///   → Tested: GET protected endpoint with X-Dev-User header + DevBypass host returns 200.
///     (DevBypassMiddleware auto-injects the JWT so no explicit token needed.)
///
/// AC5: appsettings.Development.json.example is pre-configured with DevBypass and sample UPNs.
///   → Checked via file content assertion.
///
/// AC6: README dev setup docs updated with DevBypass instructions.
///   → Checked via file content assertion.
///
/// Extra: UPN not in DevBypassAllowedUsers is rejected with 401.
/// </summary>
public class Issue68AcceptanceTests
{
    // ── AC1: Host boots with Auth:Mode=DevBypass ──────────────────────────────

    [Fact]
    public async Task AC1_DevBypassMode_HostBoots_Successfully()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient();
        // Health endpoint is always anonymous — just confirm the host started
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── AC2: X-Dev-User header issues a valid JWT via dev-login endpoint ──────

    [Fact]
    public async Task AC2_DevLogin_Returns200_And_AccessToken_For_AllowedUpn()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/dev-login");
        req.Headers.Add("X-Dev-User", "alice@va.gov");

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DevLoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken), "accessToken must be non-empty");
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal(900, body.ExpiresIn);
    }

    [Fact]
    public async Task AC2_DevLogin_Returns200_And_AccessToken_For_SecondSampleUpn()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/dev-login");
        req.Headers.Add("X-Dev-User", "bob@va.gov");

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DevLoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
    }

    [Fact]
    public async Task AC2_DevLogin_IssuedToken_Authenticates_Protected_Endpoint()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Step 1: obtain token
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/dev-login");
        loginReq.Headers.Add("X-Dev-User", "alice@va.gov");
        var loginResp = await client.SendAsync(loginReq);
        loginResp.EnsureSuccessStatusCode();
        var body = await loginResp.Content.ReadFromJsonAsync<DevLoginResponse>();
        Assert.NotNull(body);

        // Step 2: use token on a protected endpoint
        var apiReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/health/db");
        apiReq.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var apiResp = await client.SendAsync(apiReq);
        // The Developer role satisfies cms:develop policy; DbHealthController requires that.
        // With a stub DB monitor, expect 200 (or 503 if DB is unreachable — both are not 401/403).
        Assert.NotEqual(HttpStatusCode.Unauthorized, apiResp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, apiResp.StatusCode);
    }

    // ── AC3: DevBypass refused in Production ──────────────────────────────────

    [Fact]
    public void AC3_DevBypass_Refused_In_Production()
    {
        // The guard in Program.cs throws an InvalidOperationException when
        // Auth:Mode=DevBypass and ASPNETCORE_ENVIRONMENT=Production.
        // WebApplicationFactory propagates this as an exception during CreateClient.
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new Issue68ProductionTestFactory();
            // CreateClient triggers host startup, which runs Program.cs
            factory.CreateClient();
        });

        Assert.Contains("DevBypass", ex.Message);
        Assert.Contains("Production", ex.Message);
    }

    // ── AC4: CI pipeline can use X-Dev-User directly (middleware auto-injects JWT) ─

    [Fact]
    public async Task AC4_ProtectedEndpoint_Accepts_XDevUser_Header_Without_Explicit_Token()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Send X-Dev-User without any Authorization header — middleware issues the JWT
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/health/db");
        req.Headers.Add("X-Dev-User", "alice@va.gov");

        var response = await client.SendAsync(req);

        // Must not be 401 or 403; middleware successfully injected the JWT
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Extra: UPN not in allowed list is rejected ────────────────────────────

    [Fact]
    public async Task Extra_DevLogin_Returns401_For_UnlistedUpn()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/dev-login");
        req.Headers.Add("X-Dev-User", "hacker@evil.com");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Extra_DevLogin_Returns400_When_XDevUser_Header_Missing()
    {
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsync("/api/auth/dev-login", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Extra_DevBypassMiddleware_Skipped_When_Authorization_Header_Present()
    {
        // If the caller already supplies a Bearer token, the middleware must not
        // overwrite it with a dev-bypass token.
        await using var factory = new Issue68TestFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Send a deliberately invalid Bearer token alongside X-Dev-User.
        // If the middleware overwrote it, we'd get 200; if it respects the existing header
        // and passes through, the bad token causes 401.
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/health/db");
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid.token.here");
        req.Headers.Add("X-Dev-User", "alice@va.gov");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── AC5: appsettings.Development.json.example has DevBypass config ────────

    [Fact]
    public void AC5_DevSettings_Example_Contains_DevBypass_Config()
    {
        // Locate the example file relative to this test assembly
        var repoRoot = FindRepoRoot();
        var examplePath = Path.Combine(
            repoRoot, "src", "api", "VA.CMS.API",
            "appsettings.Development.json.example");

        Assert.True(File.Exists(examplePath),
            $"appsettings.Development.json.example not found at: {examplePath}");

        var content = File.ReadAllText(examplePath);

        Assert.Contains("DevBypass",        content);
        Assert.Contains("alice@va.gov",     content);
        Assert.Contains("bob@va.gov",       content);
        Assert.Contains("DevBypassAllowedUsers", content);
    }

    // ── AC6: README has DevBypass documentation ───────────────────────────────

    [Fact]
    public void AC6_README_Contains_DevBypass_Instructions()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");

        Assert.True(File.Exists(readmePath),
            $"README.md not found at: {readmePath}");

        var content = File.ReadAllText(readmePath);

        Assert.Contains("DevBypass",        content);
        Assert.Contains("X-Dev-User",       content);
        Assert.Contains("appsettings.Development.json", content);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        // Walk up from the test binary until we find the migrations/ directory
        // (a reliable marker of the repo root).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "migrations")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (no migrations/ directory found in ancestors).");
    }
}

// ── DTO ───────────────────────────────────────────────────────────────────────

internal sealed record DevLoginResponse(
    string AccessToken,
    int ExpiresIn,
    string TokenType);

// ── Test factories ────────────────────────────────────────────────────────────

/// <summary>
/// Test factory that boots the host in DevBypass mode with two allowed UPNs.
/// Stubs out the database so no live SQL Server is needed.
/// </summary>
public sealed class Issue68TestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("SKIP_MIGRATIONS",   "true");
        builder.UseSetting("Auth:Mode",         "DevBypass");
        builder.UseSetting("Auth:DevBypassAllowedUsers:0", "alice@va.gov");
        builder.UseSetting("Auth:DevBypassAllowedUsers:1", "bob@va.gov");
        builder.UseSetting("Jwt:SigningKey",    "issue-68-devbypass-signing-key!!");
        builder.UseSetting("Jwt:Issuer",        "va-cms-api");
        builder.UseSetting("Jwt:Audience",      "va-cms-spa");
        // AzureAd settings required by Microsoft.Identity.Web registration (even in DevBypass)
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("AzureAd:CallbackPath", "/api/auth/callback");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IUserRepository>(services,      _ => new Issue68UserStub());
            Replace<IDbMonitorRepository>(services, _ => new Issue68DbMonitorStub());
        });
    }

    private static void Replace<T>(IServiceCollection services,
        Func<IServiceProvider, T> factory) where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddScoped<T>(factory);
    }
}

/// <summary>
/// Factory that boots the host pretending to be Production to verify
/// the DevBypass startup guard.
/// </summary>
public sealed class Issue68ProductionTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment to Production — this is what triggers the guard
        builder.UseEnvironment("Production");

        builder.UseSetting("SKIP_MIGRATIONS",   "true");
        builder.UseSetting("Auth:Mode",         "DevBypass");
        builder.UseSetting("Jwt:SigningKey",    "issue-68-production-guard-key!!aa");
        builder.UseSetting("Jwt:Issuer",        "va-cms-api");
        builder.UseSetting("Jwt:Audience",      "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("AzureAd:CallbackPath", "/api/auth/callback");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IUserRepository>(services,      _ => new Issue68UserStub());
            Replace<IDbMonitorRepository>(services, _ => new Issue68DbMonitorStub());
        });
    }

    private static void Replace<T>(IServiceCollection services,
        Func<IServiceProvider, T> factory) where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddScoped<T>(factory);
    }
}

// ── In-memory stubs ───────────────────────────────────────────────────────────

internal sealed class Issue68UserStub : IUserRepository
{
    private readonly Dictionary<string, (long Id, User User)> _users = new(StringComparer.OrdinalIgnoreCase);
    private long _nextId = 1;

    public Task<long> UpsertAsync(string externalId, string email, string displayName)
    {
        if (_users.TryGetValue(externalId, out var existing))
            return Task.FromResult(existing.Id);

        var id = _nextId++;
        _users[externalId] = (id, new User
        {
            Id          = id,
            ExternalId  = externalId,
            Email       = email,
            DisplayName = displayName,
            IsActive    = true,
        });
        return Task.FromResult(id);
    }

    public Task<User?> GetByIdAsync(long id)
    {
        var found = _users.Values.FirstOrDefault(u => u.Id == id);
        return Task.FromResult<User?>(found == default ? null : found.User);
    }

    public Task<User?> GetByExternalIdAsync(string externalId)
    {
        _users.TryGetValue(externalId, out var found);
        return Task.FromResult<User?>(found == default ? null : found.User);
    }

    public Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId)
        => Task.FromResult<IEnumerable<UserRoleAssignment>>(Array.Empty<UserRoleAssignment>());
}

internal sealed class Issue68DbMonitorStub : IDbMonitorRepository
{
    public Task<IEnumerable<IndexFragmentationRow>> GetIndexFragmentationAsync()
        => Task.FromResult<IEnumerable<IndexFragmentationRow>>(Array.Empty<IndexFragmentationRow>());
    public Task<IEnumerable<TableSizeRow>> GetTableSizesAsync()
        => Task.FromResult<IEnumerable<TableSizeRow>>(Array.Empty<TableSizeRow>());
    public Task<IEnumerable<LongRunningQueryRow>> GetLongRunningQueriesAsync()
        => Task.FromResult<IEnumerable<LongRunningQueryRow>>(Array.Empty<LongRunningQueryRow>());
}
