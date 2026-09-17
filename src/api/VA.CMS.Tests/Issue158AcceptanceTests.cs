using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PetaPoco;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers;
using VA.CMS.API.Services;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #158: media upload and serve hardening.
///
///   - SVG is off the default allow-list; when an admin allows it, uploads are sanitized.
///   - The declared Content-Type must agree with the sniffed content and the extension.
///   - Serve: nosniff, sandbox CSP for inline renders, attachment for non-image/PDF,
///     RFC 5987 file names, ETag / short public TTL, anonymous only for assets that
///     Published content references.
///   - File names are stripped of paths and length-limited.
/// </summary>
public class Issue158AcceptanceTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    private const string Svg =
        "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" onload=\"alert(1)\">" +
        "<script>alert(1)</script><foreignObject><body xmlns=\"http://www.w3.org/1999/xhtml\">x</body></foreignObject>" +
        "<image xlink:href=\"https://evil.example/t.png\"/><use href=\"#ok\"/><rect id=\"ok\" width=\"1\" height=\"1\" onclick=\"x()\" style=\"fill:url(https://evil.example/x)\"/></svg>";

    // ── sniffer ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sniffer_Recognises_Supported_Families()
    {
        Assert.Equal(["image/png"],        MediaContentSniffer.Detect(new MemoryStream(Png)));
        Assert.Equal(["image/jpeg"],       MediaContentSniffer.Detect(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0, 0])));
        Assert.Equal(["image/gif"],        MediaContentSniffer.Detect(new MemoryStream(Encoding.ASCII.GetBytes("GIF89a\0\0"))));
        Assert.Equal(["application/pdf"],  MediaContentSniffer.Detect(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\n"))));
        Assert.Equal(["image/svg+xml"],    MediaContentSniffer.Detect(new MemoryStream(Encoding.UTF8.GetBytes("\uFEFF <!-- c --> <svg/>"))));
        Assert.Equal(["text/plain", "text/csv"], MediaContentSniffer.Detect(new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"))));
        Assert.Contains("application/msword", MediaContentSniffer.Detect(new MemoryStream([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0])));
        Assert.Empty(MediaContentSniffer.Detect(new MemoryStream([0x4D, 0x5A, 0x90, 0x00])));   // PE executable
        Assert.Empty(MediaContentSniffer.Detect(new MemoryStream([0x00, 0x01, 0x02])));         // binary junk
    }

    [Fact]
    public void Sniffer_Distinguishes_Ooxml_From_Plain_Zip()
    {
        Assert.Equal(["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            MediaContentSniffer.Detect(new MemoryStream(Zip("word/document.xml"))));
        Assert.Equal(["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            MediaContentSniffer.Detect(new MemoryStream(Zip("xl/workbook.xml"))));
        Assert.Equal(["application/zip", "application/x-zip-compressed"],
            MediaContentSniffer.Detect(new MemoryStream(Zip("readme.txt"))));
    }

    [Theory]
    [InlineData("image/png", "png", true)]
    [InlineData("image/jpeg", "JPG", true)]
    [InlineData("image/png", "jpg", false)]
    [InlineData("application/pdf", "exe", false)]
    public void Extension_Must_Match_Mime(string mime, string ext, bool expected)
        => Assert.Equal(expected, MediaContentSniffer.ExtensionMatches(mime, ext));

    // ── sanitizer ─────────────────────────────────────────────────────────────

    [Fact]
    public void SvgSanitizer_Strips_Active_Content()
    {
        var (bytes, error) = SvgSanitizer.Sanitize(new MemoryStream(Encoding.UTF8.GetBytes(Svg)));

        Assert.Null(error);
        var text = Encoding.UTF8.GetString(bytes!);
        Assert.DoesNotContain("<script",       text);
        Assert.DoesNotContain("foreignObject", text);
        Assert.DoesNotContain("onload",        text);
        Assert.DoesNotContain("onclick",       text);
        Assert.DoesNotContain("evil.example",  text);
        Assert.Contains("href=\"#ok\"", text);        // fragment references survive
        Assert.Contains("<rect",        text);
    }

    [Fact]
    public void SvgSanitizer_Rejects_Dtd_And_Non_Svg()
    {
        var (_, dtdError) = SvgSanitizer.Sanitize(new MemoryStream(Encoding.UTF8.GetBytes(
            "<!DOCTYPE svg [<!ENTITY x \"y\">]><svg>&x;</svg>")));
        Assert.NotNull(dtdError);

        var (_, htmlError) = SvgSanitizer.Sanitize(new MemoryStream(Encoding.UTF8.GetBytes("<html><body/></html>")));
        Assert.NotNull(htmlError);
    }

    // ── upload service ────────────────────────────────────────────────────────

    [Fact]
    public async Task Default_AllowList_Excludes_Svg()
    {
        Assert.DoesNotContain("image/svg+xml", SiteSettingDefinitions.DefaultAllowedMimeTypes);

        var (asset, error) = await Service().UploadAsync(FormFile(Encoding.UTF8.GetBytes(Svg), "logo.svg", "image/svg+xml"), 1);

        Assert.Null(asset);
        Assert.Contains("not permitted", error);
    }

    [Fact]
    public async Task Svg_When_Allowed_Is_Sanitized_Before_Storage()
    {
        var storage = new InMemoryStorageBackend();
        var allowed = SiteSettingDefinitions.DefaultAllowedMimeTypes.Append("image/svg+xml").ToArray();
        var service = Service(storage, StaticSiteSettings.Defaults.WithJson(SiteSettingKeys.MediaAllowedMimeTypes, allowed));

        var (asset, error) = await service.UploadAsync(FormFile(Encoding.UTF8.GetBytes(Svg), "logo.svg", "image/svg+xml"), 1);

        Assert.Null(error);
        var stored = Encoding.UTF8.GetString(storage.GetBytes(asset!.StoragePath)!);
        Assert.DoesNotContain("<script", stored);
        Assert.DoesNotContain("onload",  stored);
        Assert.Equal(stored.Length, (int)asset.FileSizeBytes);   // size reflects the sanitized bytes
    }

    [Theory]
    [InlineData("image/jpeg", "photo.jpg", "does not match the declared type")]   // PNG bytes claimed as JPEG
    [InlineData("image/png",  "photo.jpg", "extension")]                          // right bytes, wrong extension
    public async Task Spoofed_ContentType_Or_Extension_Is_Rejected(string declared, string name, string expectedError)
    {
        var (asset, error) = await Service().UploadAsync(FormFile(Png, name, declared), 1);

        Assert.Null(asset);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public async Task Svg_Bytes_Declared_As_Png_Are_Rejected()
    {
        var (asset, error) = await Service().UploadAsync(FormFile(Encoding.UTF8.GetBytes(Svg), "logo.png", "image/png"), 1);

        Assert.Null(asset);
        Assert.Contains("image/svg+xml", error);
    }

    [Fact]
    public async Task Html_Declared_As_Png_Is_Rejected()
    {
        var (asset, error) = await Service().UploadAsync(FormFile(Encoding.UTF8.GetBytes("<html><script>1</script></html>"), "x.png", "image/png"), 1);

        Assert.Null(asset);
        Assert.Contains("does not match", error);
    }

    [Fact]
    public async Task Genuine_Png_Still_Uploads()
    {
        var (asset, error) = await Service().UploadAsync(FormFile(Png, "ok.png", "image/png"), 1);

        Assert.Null(error);
        Assert.Equal("ok.png", asset!.FileName);
    }

    [Theory]
    [InlineData("C:\\\\Users\\\\me\\\\..\\\\evil.png", "evil.png")]
    [InlineData("../../etc/passwd.png",          "passwd.png")]
    [InlineData("a\"b;c\u0007.png",               "abc.png")]
    [InlineData("",                               "upload")]
    public void FileName_Is_Stripped_Of_Paths_And_Control_Characters(string raw, string expected)
        => Assert.Equal(expected, MediaUploadService.SanitizeFileName(raw));

    [Fact]
    public void FileName_Is_Length_Limited_Keeping_Extension()
    {
        var name = MediaUploadService.SanitizeFileName(new string('a', 400) + ".png");

        Assert.Equal(MediaUploadService.MaxFileNameLength, name.Length);
        Assert.EndsWith(".png", name);
    }

    // ── usage extraction ──────────────────────────────────────────────────────

    [Fact]
    public void Usage_Extract_Finds_MediaReference_Fields_And_Serve_Urls()
    {
        var registry = new FakeRegistry(mediaFields: ["featuredImage"]);
        const string fields = """
            {
              "title": "x",
              "featuredImage": "12",
              "gallery": { "id": 13 },
              "body": "![a](/api/v1/media/serve/14) and https://cms.va.gov/api/v1/media/serve/14?variant=webp and /api/v1/media/serve/15"
            }
            """;

        var refs = MediaUsageSyncService.Extract("page", fields, registry).OrderBy(r => r.AssetId).ToList();

        Assert.Equal([(12L, "featuredImage"), (14L, "body"), (15L, "body")], refs);
    }

    // ── serve over HTTP ───────────────────────────────────────────────────────

    [Fact]
    public async Task Serve_Public_Image_Has_Security_And_Cache_Headers()
    {
        await using var factory = new ServeFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/media/serve/1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/png", resp.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(MediaResponsePolicy.SandboxCsp, resp.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("inline", resp.Content.Headers.ContentDisposition!.DispositionType);
        Assert.NotNull(resp.Headers.ETag);
        Assert.True(resp.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), resp.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Serve_Unreferenced_Asset_Is_404_Anonymously_And_Private_With_CanRead()
    {
        await using var factory = new ServeFactory();

        var anon = await factory.CreateClient().GetAsync("/api/v1/media/serve/2");
        Assert.Equal(HttpStatusCode.NotFound, anon.StatusCode);

        var editor = await factory.CreateAuthenticatedClient(CmsRoles.Editor).GetAsync("/api/v1/media/serve/2");
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
        Assert.True(editor.Headers.CacheControl!.Private);
        Assert.True(editor.Headers.CacheControl.NoStore);

        var noRole = await factory.CreateAuthenticatedClient(roleName: null).GetAsync("/api/v1/media/serve/2");
        Assert.Equal(HttpStatusCode.NotFound, noRole.StatusCode);
    }

    [Fact]
    public async Task Serve_Non_Image_Is_Attachment_With_Rfc5987_FileName()
    {
        await using var factory = new ServeFactory();

        var resp = await factory.CreateClient().GetAsync("/api/v1/media/serve/3");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cd = resp.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", cd.DispositionType);
        Assert.Contains("filename*=UTF-8''", cd.ToString());
        Assert.Contains("%C3%A9", cd.ToString());   // "résumé" encoded, not raw
        Assert.Equal(MediaResponsePolicy.SandboxCsp, resp.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Serve_Svg_Is_Sandboxed_With_No_Sources()
    {
        await using var factory = new ServeFactory();

        var resp = await factory.CreateClient().GetAsync("/api/v1/media/serve/4");

        Assert.Equal(MediaResponsePolicy.SvgCsp, resp.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("inline", resp.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task Serve_Pdf_Inline_Without_Sandbox_But_Nosniff()
    {
        await using var factory = new ServeFactory();

        var resp = await factory.CreateClient().GetAsync("/api/v1/media/serve/5");

        Assert.Equal("inline", resp.Content.Headers.ContentDisposition!.DispositionType);
        Assert.False(resp.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Serve_Honours_If_None_Match()
    {
        await using var factory = new ServeFactory();
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/v1/media/serve/1");
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/media/serve/1");
        req.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var second = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Serve_Virus_Flagged_Asset_Is_404()
    {
        await using var factory = new ServeFactory();

        var resp = await factory.CreateAuthenticatedClient(CmsRoles.SystemAdmin).GetAsync("/api/v1/media/serve/6");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static MediaUploadService Service(InMemoryStorageBackend? storage = null, ISiteSettingsService? settings = null)
        => new(storage ?? new InMemoryStorageBackend(), new AssetRepoStub(), new NoOpImageProcessingService(),
               new StubVirusScanService(true), new UsageRepoStub(), settings ?? StaticSiteSettings.Defaults);

    private static IFormFile FormFile(byte[] content, string fileName, string contentType)
        => new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(), ContentType = contentType,
        };

    private static byte[] Zip(string entryName)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry(entryName).Open());
            w.Write("<x/>");
        }
        return ms.ToArray();
    }

    private sealed class FakeRegistry(string[] mediaFields) : IFieldTypeRegistry
    {
        private sealed class Def(string[] mediaFields) : ContentTypeDefinitionBase
        {
            public override string Name => "page";
            public override string DisplayName => "Page";
            public override string? TemplateId => null;
            public override IReadOnlyList<FieldDefinition> Fields { get; } =
                mediaFields.Select(f => new FieldDefinition(f, FieldType.MediaReference)).ToList();
        }
        public IReadOnlyList<ContentTypeDefinitionBase> GetAll() => [new Def(mediaFields)];
        public ContentTypeDefinitionBase? GetByName(string name) => name == "page" ? new Def(mediaFields) : null;
        public IReadOnlyList<string> GetBuiltInFieldTypeNames() => [];
    }

    internal sealed class AssetRepoStub : IMediaAssetRepository
    {
        public static readonly MediaAsset[] Assets =
        [
            new() { Id = 1, FileName = "hero.png",   MimeType = "image/png",       StoragePath = "a/1.png", UpdatedAt = DateTime.UtcNow, IsVirusScanPassed = true },
            new() { Id = 2, FileName = "draft.png",  MimeType = "image/png",       StoragePath = "a/2.png", UpdatedAt = DateTime.UtcNow, IsVirusScanPassed = true },
            new() { Id = 3, FileName = "résumé.csv", MimeType = "text/csv",        StoragePath = "a/3.csv", UpdatedAt = DateTime.UtcNow, IsVirusScanPassed = true },
            new() { Id = 4, FileName = "logo.svg",   MimeType = "image/svg+xml",   StoragePath = "a/4.svg", UpdatedAt = DateTime.UtcNow, IsVirusScanPassed = true },
            new() { Id = 5, FileName = "guide.pdf",  MimeType = "application/pdf", StoragePath = "a/5.pdf", UpdatedAt = DateTime.UtcNow, IsVirusScanPassed = true },
            new() { Id = 6, FileName = "bad.png",    MimeType = "image/png",       StoragePath = "a/6.png", UpdatedAt = DateTime.UtcNow, IsVirusScanPassed = false },
        ];
        public Task<MediaAsset?> GetByIdAsync(long id) => Task.FromResult(Assets.FirstOrDefault(a => a.Id == id));
        public Task<Page<MediaAsset>> ListAsync(int page, int pageSize, string? mimeTypePrefix = null, string? searchTerm = null)
            => Task.FromResult(new Page<MediaAsset> { Items = Assets.ToList(), TotalItems = Assets.Length });
        public Task<long> CreateAsync(MediaAsset asset) { asset.Id = 100; return Task.FromResult(100L); }
        public Task UpdateAsync(MediaAsset asset) => Task.CompletedTask;
        public Task UpdateWebPPathAsync(long id, string webPStoragePath) => Task.CompletedTask;
    }

    /// <summary>Assets 1, 3, 4, 5 are used by a Published entry; 2 only by a Draft; 6 by nothing.</summary>
    internal sealed class UsageRepoStub : IMediaExtendedRepository
    {
        public Task<IEnumerable<MediaUsageDetail>> GetUsageAsync(long mediaAssetId)
            => Task.FromResult<IEnumerable<MediaUsageDetail>>(mediaAssetId switch
            {
                1 or 3 or 4 or 5 => [new MediaUsageDetail { ContentEntryId = 9, Status = "Published" }],
                2                => [new MediaUsageDetail { ContentEntryId = 8, Status = "Draft" }],
                _                => [],
            });
        public Task SetVirusScanResultAsync(long assetId, bool passed) => Task.CompletedTask;
        public Task<IEnumerable<MediaUsageWithTitle>> GetUsageWithTitleAsync(long mediaAssetId) => Task.FromResult<IEnumerable<MediaUsageWithTitle>>([]);
        public Task<int> SafeDeleteAsync(long assetId) => Task.FromResult(0);
        public Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName) => Task.CompletedTask;
        public Task DeleteUsageForEntryAsync(long contentEntryId) => Task.CompletedTask;
    }

    internal sealed class StorageStub : IStorageBackend
    {
        public string BackendName => "local";
        public Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default) => Task.FromResult(storagePath);
        public Task<string> SaveBytesAsync(byte[] bytes, string storagePath, CancellationToken ct = default) => Task.FromResult(storagePath);
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
            => Task.FromResult<Stream?>(new MemoryStream(storagePath.EndsWith(".png") ? Png : Encoding.UTF8.GetBytes("x")));
    }

    private sealed class ServeFactory : WebApplicationFactory<Program>
    {
        public HttpClient CreateAuthenticatedClient(string? roleName)
        {
            var client = CreateClient();
            var jwt    = Services.GetRequiredService<IJwtService>();
            var user   = new User { Id = 1, ExternalId = "x", Email = "x@va.gov", DisplayName = "X", IsActive = true };
            var roles  = roleName is null ? Array.Empty<UserRoleAssignment>() : [new UserRoleAssignment { RoleId = 1, RoleName = roleName }];
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.IssueAccessToken(user, roles));
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Jwt:SigningKey",  "issue-158-acceptance-key-32chars!");
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
                Replace<IMediaAssetRepository>(services,    _ => new AssetRepoStub());
                Replace<IMediaExtendedRepository>(services, _ => new UsageRepoStub());
                Replace<IStorageBackend>(services,          _ => new StorageStub());
                Replace<IDbMonitorRepository>(services,     _ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
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
