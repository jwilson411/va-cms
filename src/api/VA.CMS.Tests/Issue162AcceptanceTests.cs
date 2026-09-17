using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers;
using VA.CMS.API.Middleware;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #162: HTTP hardening.
///   - Security headers on every API response (health, content, media serve, errors).
///   - CSP Report-Only ↔ enforced by the security.cspReportOnly site setting; report endpoint.
///   - AllowedHosts wildcard refused outside Development.
///   - Forwarded-header trust only from configured proxies/networks.
///   - Swagger requires CanDevelop outside Development.
/// </summary>
public class Issue162AcceptanceTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/v1/content/no-such-slug")]        // 404 path still carries the headers
    [InlineData("/api/v1/admin/users")]                 // 401 path too
    public async Task Every_Response_Carries_Security_Headers(string path)
    {
        await using var factory = new HardeningFactory();
        var resp = await factory.CreateClient().GetAsync(path);

        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", resp.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", resp.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", resp.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", resp.Headers.GetValues("Content-Security-Policy-Report-Only").Single());
        Assert.Contains($"report-uri {SecurityHeadersMiddleware.ReportPath}", resp.Headers.GetValues("Content-Security-Policy-Report-Only").Single());
        Assert.False(resp.Headers.Contains("Strict-Transport-Security"));   // http in Development
    }

    [Fact]
    public async Task Csp_Is_Enforced_When_ReportOnly_Setting_Is_Off()
    {
        await using var factory = new HardeningFactory(cspReportOnly: false);
        var resp = await factory.CreateClient().GetAsync("/health");

        Assert.StartsWith(SecurityHeadersMiddleware.ApiCsp, resp.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(resp.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task Media_Serve_Keeps_Its_Own_Csp_And_Gets_The_Rest()
    {
        await using var factory = new HardeningFactory(cspReportOnly: false);
        var resp = await factory.CreateClient().GetAsync("/api/v1/media/serve/1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(MediaResponsePolicy.SandboxCsp, resp.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Hsts_Is_Sent_On_Https_Outside_Development_With_Preload_Per_Setting()
    {
        await using var plain   = new HardeningFactory(environment: "Staging");
        await using var preload = new HardeningFactory(environment: "Staging", hstsPreload: true);

        var a = await plain.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }).GetAsync("/health");
        var b = await preload.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }).GetAsync("/health");

        Assert.Equal("max-age=31536000; includeSubDomains", a.Headers.GetValues("Strict-Transport-Security").Single());
        Assert.Equal("max-age=31536000; includeSubDomains; preload", b.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public async Task Csp_Report_Endpoint_Accepts_Both_Formats_And_Caps_Size()
    {
        await using var factory = new HardeningFactory();
        var client = factory.CreateClient();

        var legacy = new StringContent("{\"csp-report\":{\"document-uri\":\"https://cms/x\",\"violated-directive\":\"script-src\",\"blocked-uri\":\"inline\"}}", Encoding.UTF8, "application/csp-report");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/security/csp-report", legacy)).StatusCode);

        var modern = new StringContent("[{\"type\":\"csp-violation\",\"body\":{\"documentURL\":\"https://cms/x\",\"effectiveDirective\":\"script-src\"}}]", Encoding.UTF8, "application/reports+json");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/security/csp-report", modern)).StatusCode);

        var huge = new StringContent(new string('x', 20_000), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/v1/security/csp-report", huge)).StatusCode);

        Assert.Equal("document-uri=https://cms/x violated-directive=script-src blocked-uri=inline",
            SecurityReportController.Summarise("{\"csp-report\":{\"document-uri\":\"https://cms/x\",\"violated-directive\":\"script-src\",\"blocked-uri\":\"inline\"}}"));
    }

    [Fact]
    public void AllowedHosts_Wildcard_Refused_Outside_Development()
    {
        Assert.Null(HostHardeningOptions.ValidateAllowedHosts("*", isDevelopment: true));
        Assert.NotNull(HostHardeningOptions.ValidateAllowedHosts("*", isDevelopment: false));
        Assert.NotNull(HostHardeningOptions.ValidateAllowedHosts("", isDevelopment: false));
        Assert.NotNull(HostHardeningOptions.ValidateAllowedHosts("cms.va.gov;*", isDevelopment: false));
        Assert.Null(HostHardeningOptions.ValidateAllowedHosts("cms.va.gov;cms-admin.va.gov", isDevelopment: false));

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new HardeningFactory(environment: "Staging", allowedHosts: "*");
            factory.CreateClient();
        });
        Assert.Contains("AllowedHosts", ex.Message);
    }

    [Fact]
    public void Forwarded_Headers_Trust_Only_Configured_Hops()
    {
        Assert.Null(HostHardeningOptions.BuildForwardedHeaders(new ConfigurationBuilder().Build()));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"]  = "10.1.2.3",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.20.0.0/16",
            ["ForwardedHeaders:ForwardLimit"]    = "2",
        }).Build();

        var options = HostHardeningOptions.BuildForwardedHeaders(config)!;

        Assert.Equal([IPAddress.Parse("10.1.2.3")], options.KnownProxies);
        Assert.Single(options.KnownNetworks);
        Assert.Equal(16, options.KnownNetworks[0].PrefixLength);
        Assert.Equal(2, options.ForwardLimit);
        Assert.DoesNotContain(IPAddress.Loopback, options.KnownProxies);   // defaults cleared

        var bad = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ForwardedHeaders:KnownNetworks:0"] = "10.20.0.0" }).Build();
        Assert.Throws<InvalidOperationException>(() => HostHardeningOptions.BuildForwardedHeaders(bad));
    }

    [Fact]
    public async Task Forwarded_Proto_From_Known_Proxy_Turns_On_Hsts()
    {
        // TestServer connections have no remote IP, so trust the "any" network for this check.
        await using var factory = new HardeningFactory(environment: "Staging", knownNetwork: "0.0.0.0/0");
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-Forwarded-Proto", "https");
        var resp = await client.SendAsync(req);

        Assert.True(resp.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Forwarded_Proto_Is_Ignored_When_No_Proxy_Is_Configured()
    {
        await using var factory = new HardeningFactory(environment: "Staging");
        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-Forwarded-Proto", "https");

        var resp = await factory.CreateClient().SendAsync(req);

        Assert.False(resp.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Swagger_Requires_CanDevelop_Outside_Development()
    {
        await using var factory = new HardeningFactory(environment: "Staging", swaggerUi: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,    (await factory.CreateAuthenticatedClient(CmsRoles.Editor).GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,           (await factory.CreateAuthenticatedClient(CmsRoles.Developer).GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Swagger_Is_Open_In_Development_With_Its_Own_Csp()
    {
        await using var factory = new HardeningFactory();
        var resp = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith(SecurityHeadersMiddleware.SwaggerCsp, resp.Headers.GetValues("Content-Security-Policy-Report-Only").Single());
    }

    // ── factory ───────────────────────────────────────────────────────────────

    private sealed class HardeningFactory(
        string environment = "Development",
        bool cspReportOnly = true,
        bool hstsPreload = false,
        bool swaggerUi = false,
        string allowedHosts = "localhost",
        string? knownNetwork = null) : WebApplicationFactory<Program>
    {
        public HttpClient CreateAuthenticatedClient(string roleName)
        {
            var client = CreateClient();
            var jwt    = Services.GetRequiredService<IJwtService>();
            var user   = new User { Id = 1, ExternalId = "x", Email = "x@va.gov", DisplayName = "X", IsActive = true };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                jwt.IssueAccessToken(user, [new UserRoleAssignment { RoleId = 1, RoleName = roleName }]));
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("AllowedHosts", allowedHosts);
            if (knownNetwork is not null)
                builder.UseSetting("ForwardedHeaders:KnownNetworks:0", knownNetwork);
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            // #164: the fake Negotiate handler is refused outside Development, and the real
            // one cannot run on TestServer. Non-Development hosts use the AzureAd registration
            // instead; these hosts only exercise /health, swagger and bearer-authenticated routes.
            if (environment == "Development")
            {
                builder.UseSetting("Auth:Mode", "WindowsAuth");
                builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
            }
            else
            {
                builder.UseSetting("Auth:Mode", "AzureAd");
                builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
                builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
                builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
                builder.UseSetting("AzureAd:ClientSecret", "test-secret");
            }
            builder.UseSetting("Jwt:SigningKey",  "issue-162-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                Replace<IContentEntryRepository>(services,  _ => new Issue23ContentEntryStub());
                Replace<IMediaAssetRepository>(services,    _ => new Issue158AcceptanceTests.AssetRepoStub());
                Replace<IMediaExtendedRepository>(services, _ => new Issue158AcceptanceTests.UsageRepoStub());
                Replace<IStorageBackend>(services,          _ => new Issue158AcceptanceTests.StorageStub());
                Replace<IDbMonitorRepository>(services,     _ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults
                    .With(SiteSettingKeys.SecurityCspReportOnly, cspReportOnly)
                    .With(SiteSettingKeys.SecurityHstsPreload,   hstsPreload)
                    .With(SiteSettingKeys.FeatureSwaggerUi,      swaggerUi));
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
