using Microsoft.AspNetCore.Http;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #41 — Implement image resize and WebP conversion on upload.
///
/// BRD FR-MEDIA-02.
///
/// Acceptance criteria:
///   AC1: Uploaded images are resized to max 1920px wide (preserving aspect ratio).
///   AC2: A WebP version is generated and stored alongside the original.
///   AC3: JPEG/PNG originals retained for download; WebP served to browsers.
///   AC4: Non-image files skip the resize step.
///
/// Test strategy:
///   - ImageProcessingService unit tests: resize logic, WebP output (pure, no DB).
///   - MediaUploadService integration tests: real DB + InMemoryStorageBackend
///     with real ImageProcessingService to verify end-to-end.
///   - ShouldProcess coverage for MIME type filtering.
/// </summary>
[Collection("Database")]
public class Issue41AcceptanceTests(DatabaseFixture fixture)
{
    // ── ImageProcessingService unit tests (no DB) ─────────────────────────────

    private static ImageProcessingService Imaging() => new();

    /// <summary>AC4: ShouldProcess only returns true for JPEG, PNG, WebP.</summary>
    [Theory]
    [InlineData("image/jpeg",       true)]
    [InlineData("image/png",        true)]
    [InlineData("image/webp",       true)]
    [InlineData("image/gif",        false)]  // animated GIF: skip
    [InlineData("application/pdf",  false)]
    [InlineData("text/csv",         false)]
    [InlineData("",                 false)]
    public void ShouldProcess_MimeTypes(string mime, bool expected)
    {
        Assert.Equal(expected, Imaging().ShouldProcess(mime));
    }

    /// <summary>AC2: GenerateWebPAsync produces valid WebP bytes from a PNG.</summary>
    [Fact]
    public async Task GenerateWebP_FromPng_ProducesWebPBytes()
    {
        var pngBytes = MinimalPng1x1();
        var webpBytes = await Imaging().GenerateWebPAsync(pngBytes);

        // WebP files start with "RIFF" + 4-byte size + "WEBP"
        Assert.True(webpBytes.Length >= 12, "WebP output is too short");
        Assert.Equal('R', (char)webpBytes[0]);
        Assert.Equal('I', (char)webpBytes[1]);
        Assert.Equal('F', (char)webpBytes[2]);
        Assert.Equal('F', (char)webpBytes[3]);
        Assert.Equal('W', (char)webpBytes[8]);
        Assert.Equal('E', (char)webpBytes[9]);
        Assert.Equal('B', (char)webpBytes[10]);
        Assert.Equal('P', (char)webpBytes[11]);
    }

    /// <summary>
    /// AC1: Images wider than 1920px are resized down; narrower images are unchanged.
    /// Verifies using an in-process 2000x1000 PNG encoded by ImageSharp.
    /// </summary>
    [Fact]
    public async Task GenerateWebP_WideImage_ResizesToMax1920()
    {
        // Build a 2000×1000 solid-colour PNG via ImageSharp
        var pngBytes = BuildSolidPng(2000, 1000);
        var webpBytes = await Imaging().GenerateWebPAsync(pngBytes);

        // Decode the output WebP and check dimensions
        using var decoded = SixLabors.ImageSharp.Image.Load(webpBytes);
        Assert.Equal(1920, decoded.Width);
        // Height is computed by aspect ratio: 1000 * (1920/2000) = 960
        Assert.Equal(960, decoded.Height);
    }

    /// <summary>AC1: Images ≤1920px wide are NOT resized (width unchanged).</summary>
    [Fact]
    public async Task GenerateWebP_NarrowImage_NotResized()
    {
        var pngBytes = BuildSolidPng(800, 600);
        var webpBytes = await Imaging().GenerateWebPAsync(pngBytes);

        using var decoded = SixLabors.ImageSharp.Image.Load(webpBytes);
        Assert.Equal(800, decoded.Width);
        Assert.Equal(600, decoded.Height);
    }

    /// <summary>AC1: Image exactly 1920px wide is NOT resized.</summary>
    [Fact]
    public async Task GenerateWebP_ExactlyMaxWidth_NotResized()
    {
        var pngBytes = BuildSolidPng(1920, 540);
        var webpBytes = await Imaging().GenerateWebPAsync(pngBytes);

        using var decoded = SixLabors.ImageSharp.Image.Load(webpBytes);
        Assert.Equal(1920, decoded.Width);
        Assert.Equal(540, decoded.Height);
    }

    // ── MediaUploadService integration tests (real DB) ────────────────────────

    private IMediaAssetRepository AssetRepo() =>
        new MediaAssetRepository(fixture.CreateDb());

    private async Task<long> SeedUserAsync() =>
        await TestSeeder.UpsertUserAsync(fixture.ConnectionString);

    private static IFormFile MakeFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    /// <summary>
    /// AC2 + AC3: Uploading a JPEG creates the original row AND a WebP variant
    /// stored alongside it. WebPStoragePath is recorded in the DB.
    /// </summary>
    [Fact]
    public async Task Upload_Jpeg_GeneratesWebPVariant()
    {
        var userId  = await SeedUserAsync();
        var storage = new TrackingInMemoryStorageBackend();
        var service = new MediaUploadService(storage, AssetRepo(), Imaging(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

        // Build a small valid JPEG (use 1×1 PNG converted to JPEG bytes via ImageSharp)
        var jpegBytes = BuildJpegBytes(100, 80);
        var file = MakeFormFile(jpegBytes, "photo.jpg", "image/jpeg");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);

        // AC2: WebPStoragePath is set
        Assert.NotNull(asset!.WebPStoragePath);
        Assert.EndsWith(".webp", asset.WebPStoragePath, StringComparison.OrdinalIgnoreCase);

        // AC3: Original path is distinct from WebP path
        Assert.NotEqual(asset.StoragePath, asset.WebPStoragePath);

        // Both files exist in storage
        Assert.True(storage.Has(asset.StoragePath),    "Original JPEG not saved");
        Assert.True(storage.Has(asset.WebPStoragePath!), "WebP variant not saved");

        // Verify the WebP path is persisted in the DB
        var fromDb = await AssetRepo().GetByIdAsync(asset.Id);
        Assert.NotNull(fromDb);
        Assert.Equal(asset.WebPStoragePath, fromDb!.WebPStoragePath);
    }

    /// <summary>
    /// AC2 + AC3: Uploading a PNG produces a WebP variant.
    /// </summary>
    [Fact]
    public async Task Upload_Png_GeneratesWebPVariant()
    {
        var userId  = await SeedUserAsync();
        var storage = new TrackingInMemoryStorageBackend();
        var service = new MediaUploadService(storage, AssetRepo(), Imaging(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

        var pngBytes = MinimalPng1x1();
        var file     = MakeFormFile(pngBytes, "banner.png", "image/png");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset!.WebPStoragePath);
        Assert.True(storage.Has(asset.WebPStoragePath!));
    }

    /// <summary>
    /// AC4: Non-image (PDF) upload skips resize/WebP — WebPStoragePath stays null.
    /// </summary>
    [Fact]
    public async Task Upload_Pdf_SkipsWebPGeneration()
    {
        var userId  = await SeedUserAsync();
        var storage = new TrackingInMemoryStorageBackend();
        var service = new MediaUploadService(storage, AssetRepo(), Imaging(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 content");
        var file     = MakeFormFile(pdfBytes, "report.pdf", "application/pdf");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);
        Assert.Null(asset!.WebPStoragePath);   // AC4: no WebP for non-image
        Assert.Equal(1, storage.SavedCount);   // only original saved
    }

    /// <summary>
    /// AC4: GIF uploads skip WebP generation (animated GIF preservation).
    /// </summary>
    [Fact]
    public async Task Upload_Gif_SkipsWebPGeneration()
    {
        var userId  = await SeedUserAsync();
        var storage = new TrackingInMemoryStorageBackend();
        var service = new MediaUploadService(storage, AssetRepo(), Imaging(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

        // Minimal GIF89a header (13 bytes, treated as image/gif)
        var gifBytes = Convert.FromBase64String(
            "R0lGODdhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        var file = MakeFormFile(gifBytes, "anim.gif", "image/gif");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);
        Assert.Null(asset!.WebPStoragePath);   // GIF excluded from WebP processing
    }

    /// <summary>
    /// AC1 end-to-end: An oversized image (>1920px wide) is resized when WebP is generated.
    /// The stored WebP has width ≤ 1920.
    /// </summary>
    [Fact]
    public async Task Upload_WideJpeg_WebPIsResizedToMax1920()
    {
        var userId  = await SeedUserAsync();
        var storage = new TrackingInMemoryStorageBackend();
        var service = new MediaUploadService(storage, AssetRepo(), Imaging(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

        var wideJpeg = BuildJpegBytes(2400, 1200);
        var file     = MakeFormFile(wideJpeg, "wide.jpg", "image/jpeg");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset!.WebPStoragePath);

        // Decode the stored WebP and verify dimensions
        var webpBytes = storage.GetBytes(asset.WebPStoragePath!);
        Assert.NotNull(webpBytes);

        using var img = SixLabors.ImageSharp.Image.Load(webpBytes!);
        Assert.Equal(1920, img.Width);
        // Aspect ratio: 1200 * (1920/2400) = 960
        Assert.Equal(960, img.Height);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns a valid 1×1 RGBA PNG (67 bytes).</summary>
    private static byte[] MinimalPng1x1() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>Builds a solid-colour PNG of the given dimensions using ImageSharp.</summary>
    private static byte[] BuildSolidPng(int width, int height)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(width, height);
        // Fill every pixel with a solid colour without the Processing extension
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24(100, 149, 237); // CornflowerBlue
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return ms.ToArray();
    }

    /// <summary>Builds a solid-colour JPEG of the given dimensions using ImageSharp.</summary>
    private static byte[] BuildJpegBytes(int width, int height)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24(70, 130, 180); // SteelBlue
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }
}

/// <summary>
/// In-memory storage backend that tracks what was saved.
/// Exposes SavedCount and GetBytes for assertion in tests.
/// </summary>
internal class TrackingInMemoryStorageBackend : IStorageBackend
{
    private readonly Dictionary<string, byte[]> _files = new();

    public string BackendName => "local";
    public int SavedCount => _files.Count;

    public async Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        _files[storagePath] = ms.ToArray();
        return storagePath;
    }

    public Task<string> SaveBytesAsync(byte[] bytes, string storagePath, CancellationToken ct = default)
    {
        _files[storagePath] = bytes;
        return Task.FromResult(storagePath);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        _files.Remove(storagePath);
        return Task.CompletedTask;
    }

    public bool Has(string storagePath) => _files.ContainsKey(storagePath);
    public byte[]? GetBytes(string storagePath) =>
        _files.TryGetValue(storagePath, out var b) ? b : null;
}
