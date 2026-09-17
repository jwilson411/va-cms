using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// OpenAPI drift gate (#161, BRD FR-DEV-01). The committed docs/openapi.json must
/// match what the built API serves at /swagger/v1/swagger.json. When a controller
/// changes, regenerate the snapshot with
///
///     UPDATE_OPENAPI_SNAPSHOT=true dotnet test --filter OpenApiSnapshot
///
/// and commit the result; CI runs this test without the variable and fails on drift.
/// </summary>
public class OpenApiSnapshotTests
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    [Fact]
    public async Task Committed_OpenApi_Document_Matches_The_Built_Api()
    {
        await using var factory = new SnapshotFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        var generated = Normalise(await response.Content.ReadAsStringAsync());
        var path      = SnapshotPath();

        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT"), "true", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(path, generated + "\n");
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing — run UPDATE_OPENAPI_SNAPSHOT=true dotnet test --filter OpenApiSnapshot");
        var committed = Normalise(await File.ReadAllTextAsync(path));

        Assert.True(committed == generated,
            "docs/openapi.json is out of date with the controllers. " +
            "Run: UPDATE_OPENAPI_SNAPSHOT=true dotnet test --filter OpenApiSnapshot, then commit docs/openapi.json.\n" +
            FirstDifference(committed, generated));
    }

    /// <summary>Canonical, indented JSON so formatting alone never counts as drift.</summary>
    private static string Normalise(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, Pretty);
    }

    private static string FirstDifference(string a, string b)
    {
        var al = a.Split('\n'); var bl = b.Split('\n');
        for (var i = 0; i < Math.Max(al.Length, bl.Length); i++)
        {
            var x = i < al.Length ? al[i] : "<eof>";
            var y = i < bl.Length ? bl[i] : "<eof>";
            if (x != y) return $"First difference at line {i + 1}:\n  committed: {x.Trim()}\n  generated: {y.Trim()}";
        }
        return "(documents differ only in length)";
    }

    private static string SnapshotPath()
    {
        var root = new DirectoryInfo(VA.CMS.Infrastructure.Data.Migrations.MigrationRunner.FindMigrationsPath()).Parent!.FullName;
        return IoPath.Combine(root, "docs", "openapi.json");
    }

    private sealed class SnapshotFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Jwt:SigningKey",  "openapi-snapshot-signing-key-32c!");
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
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IDbMonitorRepository));
                if (existing != null) services.Remove(existing);
                services.AddScoped<IDbMonitorRepository>(_ => new AuthTestStubs.StubDbMonitorRepository());
            });
        }
    }
}
