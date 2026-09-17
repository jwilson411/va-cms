using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Controllers;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Markdown;

namespace VA.CMS.Tests;

/// <summary>
/// Public content delivery: GET /api/v1/content/{slug} (docs/ARCHITECTURE.md
/// "Content Rendering Pipeline"; consumed by src/public/lib/cms/content.ts).
///
///   - Anonymous — no token required.
///   - Slugs may contain "/" (catch-all route); numeric ids still route to the
///     authenticated GetById, and the list endpoint is still protected.
///   - 404 when no published entry, or when ?type= does not match.
///   - fields = FieldsJson + renderedBody (from RenderedFieldsJson, else rendered now).
/// </summary>
public class PublicContentDeliveryTests
{
    [Fact]
    public async Task Anonymous_MultiSegmentSlug_Returns200_WithFields()
    {
        await using var factory = new PublicContentTestFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/v1/content/demo/home?type=standard_page");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("demo/home",     root.GetProperty("slug").GetString());
        Assert.Equal("standard_page", root.GetProperty("contentTypeName").GetString());
        Assert.Equal("Published",     root.GetProperty("status").GetString());
        var fields = root.GetProperty("fields");
        Assert.Equal("Home", fields.GetProperty("title").GetString());
        Assert.Contains("<h2", fields.GetProperty("renderedBody").GetString());
    }

    [Fact]
    public async Task PercentEncodedSlash_IsUnescaped()
    {
        await using var factory = new PublicContentTestFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/v1/content/demo%2Fhome");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task TypeMismatch_Returns404()
    {
        await using var factory = new PublicContentTestFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/v1/content/demo/home?type=news_article");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task UnknownSlug_Returns404()
    {
        await using var factory = new PublicContentTestFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/v1/content/does/not/exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRoutes_StillProtected()
    {
        await using var factory = new PublicContentTestFactory();
        var client = factory.CreateClient();

        // Numeric id → ContentController.GetById (CanRead), not the public catch-all.
        var byId = await client.GetAsync("/api/v1/content/1");
        Assert.Equal(HttpStatusCode.Unauthorized, byId.StatusCode);

        // Bare list endpoint remains protected.
        var list = await client.GetAsync("/api/v1/content");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    // ── BuildFields unit tests ───────────────────────────────────────────────

    private sealed class EchoRenderer : IMarkdownRenderer
    {
        public string Render(string markdown) => $"<rendered>{markdown}</rendered>";
    }

    [Fact]
    public void BuildFields_PrefersPublishTimeRenderedBody()
    {
        var fields = PublicContentController.BuildFields(
            """{"title":"T","body":"# md"}""",
            """{"title":"<p>T</p>","body":"<h1>md</h1>"}""",
            new EchoRenderer());

        Assert.Equal("<h1>md</h1>", fields["renderedBody"]!.GetValue<string>());
        Assert.Equal("# md",        fields["body"]!.GetValue<string>());   // raw markdown preserved
        Assert.Equal("T",           fields["title"]!.GetValue<string>());  // title never HTML-wrapped
    }

    [Fact]
    public void BuildFields_RendersOnTheFly_WhenNoRenderedJson()
    {
        var fields = PublicContentController.BuildFields(
            """{"title":"T","body":"# md"}""", null, new EchoRenderer());

        Assert.Equal("<rendered># md</rendered>", fields["renderedBody"]!.GetValue<string>());
    }

    [Fact]
    public void BuildFields_InvalidJson_ReturnsEmptyObject()
    {
        var fields = PublicContentController.BuildFields("{not json", null, new EchoRenderer());
        Assert.Empty(fields);
    }
}

// ── Test host ─────────────────────────────────────────────────────────────────

internal sealed class PublicContentTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("SKIP_MIGRATIONS",   "true");
        builder.UseSetting("Auth:Mode",         "DevBypass");
        builder.UseSetting("Jwt:SigningKey",    "public-content-delivery-key-32ch!");
        builder.UseSetting("Jwt:Issuer",        "va-cms-api");
        builder.UseSetting("Jwt:Audience",      "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IContentEntryRepository));
            if (existing != null) services.Remove(existing);
            services.AddScoped<IContentEntryRepository>(_ => new PublishedSlugStub());
        });
    }
}

/// <summary>Issue23 stub plus one published entry at slug "demo/home".</summary>
internal sealed class PublishedSlugStub : Issue23ContentEntryStub
{
    public override Task<PublishedContentEntry?> GetPublishedBySlugAsync(string slug, string locale = "en-US")
        => Task.FromResult<PublishedContentEntry?>(slug == "demo/home"
            ? new PublishedContentEntry
            {
                Id              = 1,
                ContentTypeId   = 1,
                ContentTypeName = "standard_page",
                TemplateId      = "StandardPageTemplate",
                Slug            = "demo/home",
                Locale          = locale,
                Status          = "Published",
                VersionNumber   = 1,
                FieldsJson      = """{"title":"Home","body":"## Welcome\n\nHello."}""",
                PublishedAt     = DateTime.UtcNow,
            }
            : null);
}
