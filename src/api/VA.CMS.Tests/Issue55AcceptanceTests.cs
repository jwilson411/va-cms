using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #55 — Generate and publish OpenAPI 3.0 spec.
///
/// BRD FR-DEV-01.
///
/// Acceptance criteria:
///   AC1: Swagger UI accessible at /swagger in Development.
///   AC2: openapi.json is committed to /docs/openapi.json on CI build.
///   AC3: All endpoints documented with request/response schemas and error codes.
/// </summary>
public class Issue55AcceptanceTests
{
    // ── WebApplicationFactory setup ───────────────────────────────────────────

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Set ASPNETCORE_ENVIRONMENT to Development so Swagger UI is enabled
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.ConfigureServices(services =>
                {
                    // Replace the real DB with a dev-bypass-friendly no-op;
                    // these tests only need the HTTP pipeline and Swagger middleware.
                    services.AddSingleton<VA.CMS.Infrastructure.Data.CmsDatabase>(_ =>
                        new VA.CMS.Infrastructure.Data.CmsDatabase(
                            "Server=.;Database=_swagger_test_placeholder;Connection Timeout=1;TrustServerCertificate=True;User Id=sa;Password=none"));
                });
                builder.UseSetting("ConnectionStrings:DefaultConnection",
                    "Server=.;Database=_swagger_test_placeholder;Connection Timeout=1;TrustServerCertificate=True;User Id=sa;Password=none");
                builder.UseSetting("SKIP_MIGRATIONS", "true");
                builder.UseSetting("Auth:Mode", "DevBypass");
                builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[32]));
            });

    // ── AC1: Swagger UI accessible at /swagger ────────────────────────────────

    [Fact]
    public async Task AC1_SwaggerUi_Returns200InDevelopment()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger", content, StringComparison.OrdinalIgnoreCase);
    }

    // ── AC3a: /swagger/v1/swagger.json returns a valid OpenAPI 3.0 document ──

    [Fact]
    public async Task AC3_SwaggerEndpoint_ReturnsOpenApi3Document()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Must be OpenAPI 3.x
        Assert.True(doc.RootElement.TryGetProperty("openapi", out var openApiVersion));
        Assert.StartsWith("3.", openApiVersion.GetString());

        // Must have info block
        Assert.True(doc.RootElement.TryGetProperty("info", out _));

        // Must have paths
        Assert.True(doc.RootElement.TryGetProperty("paths", out var paths));
        var pathCount = paths.EnumerateObject().Count();
        Assert.True(pathCount > 0, $"Expected paths to be populated, got {pathCount}");
    }

    // ── AC3b: Key endpoint paths are present and documented ──────────────────

    [Fact]
    public async Task AC3_SwaggerJson_ContainsKeyEndpoints()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("paths", out var paths));
        var pathKeys = paths.EnumerateObject().Select(p => p.Name).ToList();

        // Content endpoints (BRD FR-DEV-01)
        Assert.Contains(pathKeys, k => k.StartsWith("/api/v1/content", StringComparison.OrdinalIgnoreCase));

        // Media endpoints
        Assert.Contains(pathKeys, k => k.StartsWith("/api/v1/media", StringComparison.OrdinalIgnoreCase));

        // Webhook endpoints (BRD FR-DEV-07)
        Assert.Contains(pathKeys, k => k.StartsWith("/api/v1/webhooks", StringComparison.OrdinalIgnoreCase));
    }

    // ── AC3c: Security scheme (Bearer JWT) is defined ────────────────────────

    [Fact]
    public async Task AC3_SwaggerJson_DefinesBearerSecurityScheme()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Must have components.securitySchemes.Bearer
        Assert.True(doc.RootElement.TryGetProperty("components", out var components));
        Assert.True(components.TryGetProperty("securitySchemes", out var schemes));
        Assert.True(schemes.TryGetProperty("Bearer", out var bearer));

        Assert.Equal("http", bearer.GetProperty("type").GetString(), ignoreCase: true);
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString(), ignoreCase: true);
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    // ── AC2: docs/openapi.json exists and is a valid OpenAPI document ─────────

    [Fact]
    public void AC2_DocsOpenApiJson_ExistsAndIsValidOpenApi()
    {
        // Locate the docs/openapi.json relative to the repo root.
        // The worktree and clone share the same layout: find from current dir up.
        var docsPath = FindDocsOpenApiJson();

        Assert.True(System.IO.File.Exists(docsPath),
            $"docs/openapi.json not found at '{docsPath}'. " +
            "Run 'dotnet swagger tofile --output docs/openapi.json' from the API project during CI.");

        var json = System.IO.File.ReadAllText(docsPath);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("openapi", out var ver));
        Assert.StartsWith("3.", ver.GetString());

        Assert.True(doc.RootElement.TryGetProperty("paths", out var paths));
        var pathCount = paths.EnumerateObject().Count();
        Assert.True(pathCount > 0, $"openapi.json has no paths (pathCount={pathCount})");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string FindDocsOpenApiJson()
    {
        // Walk up from the test assembly location to find the repo root (contains /docs)
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "docs", "openapi.json");
            if (System.IO.File.Exists(candidate))
                return candidate;

            // Also check if this dir has a /docs folder (repo root)
            var docsDir = System.IO.Path.Combine(dir.FullName, "docs");
            if (System.IO.Directory.Exists(docsDir))
                return candidate; // Return path even if missing (the test assertion handles it)

            dir = dir.Parent;
        }

        return System.IO.Path.Combine(AppContext.BaseDirectory, "docs", "openapi.json");
    }
}
