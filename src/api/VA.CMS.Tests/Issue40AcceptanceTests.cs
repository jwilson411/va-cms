using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #40 — Build media upload API with storage backend abstraction.
///
/// BRD FR-MEDIA-01, FR-MEDIA-07, FR-SECURITY-06.
///
/// Acceptance criteria:
///   AC1: POST /api/v1/media/upload accepts multipart file.
///   AC2: Storage backend selected by config: local, unc (on-prem only).
///   AC3: File stored outside web root.
///   AC4: File extension validated against MIME allow-list (BRD FR-SECURITY-06).
///   AC5: MediaAsset row created with path, MIME, size, dimensions.
///
/// Test strategy:
///   - MimeAllowList unit tests (no DB required).
///   - MediaUploadService integration tests: uses real SQL (DatabaseFixture) +
///     in-process InMemoryStorageBackend to isolate filesystem.
/// </summary>
[Collection("Database")]
public class Issue40AcceptanceTests(DatabaseFixture fixture)
{
    // ── MimeAllowList unit tests (no DB) ──────────────────────────────────────

    [Fact]
    public void MimeAllowList_PermittedTypes_Allowed()
    {
        Assert.True(MimeAllowList.IsAllowed("image/jpeg"));
        Assert.True(MimeAllowList.IsAllowed("image/png"));
        Assert.True(MimeAllowList.IsAllowed("image/gif"));
        Assert.True(MimeAllowList.IsAllowed("image/webp"));
        Assert.True(MimeAllowList.IsAllowed("application/pdf"));
        Assert.True(MimeAllowList.IsAllowed("text/plain"));
        Assert.True(MimeAllowList.IsAllowed("text/csv"));
    }

    [Fact]
    public void MimeAllowList_DangerousTypes_Rejected()
    {
        Assert.False(MimeAllowList.IsAllowed("application/x-msdownload")); // .exe
        Assert.False(MimeAllowList.IsAllowed("application/javascript"));
        Assert.False(MimeAllowList.IsAllowed("text/html"));
        Assert.False(MimeAllowList.IsAllowed("application/x-sh"));
        Assert.False(MimeAllowList.IsAllowed(""));
        Assert.False(MimeAllowList.IsAllowed(null!));
    }

    [Fact]
    public void MimeAllowList_CaseInsensitive()
    {
        Assert.True(MimeAllowList.IsAllowed("IMAGE/JPEG"));
        Assert.True(MimeAllowList.IsAllowed("Image/Png"));
    }

    // ── AC2: StorageOptions backend selection ─────────────────────────────────

    [Theory]
    [InlineData("local")]
    [InlineData("unc")]
    public void StorageOptions_BackendName_RoundTrips(string backend)
    {
        var opts = new StorageOptions { Backend = backend };
        Assert.Equal(backend, opts.Backend);
    }

    // ── MediaUploadService integration tests (real DB, in-memory storage) ─────

    private IMediaAssetRepository AssetRepo() => new MediaAssetRepository(fixture.CreateDb());

    private MediaUploadService MakeService() =>
        new MediaUploadService(new InMemoryStorageBackend(), AssetRepo(), new NoOpImageProcessingService(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

    private async Task<long> SeedUserAsync() =>
        await TestSeeder.UpsertUserAsync(fixture.ConnectionString);

    // ── AC1 + AC5: Upload creates MediaAsset row ──────────────────────────────

    /// <summary>
    /// AC1 + AC5: Uploading a valid image creates a MediaAsset row with the expected
    /// path, MIME type, file size, and (for PNG) pixel dimensions.
    /// </summary>
    [Fact]
    public async Task Upload_ValidPng_CreatesMediaAssetRow()
    {
        var userId  = await SeedUserAsync();
        var service = MakeService();

        // Minimal 1×1 PNG (67 bytes)
        var pngBytes = MinimalPng();
        var file     = MakeFormFile(pngBytes, "banner.png", "image/png");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);
        Assert.True(asset!.Id > 0);
        Assert.Equal("banner.png", asset.FileName);
        Assert.Equal("image/png",  asset.MimeType);
        Assert.Equal("local",      asset.StorageBackend);
        Assert.Equal(pngBytes.Length, asset.FileSizeBytes);

        // Dimensions should be parsed from PNG header
        Assert.Equal(1, asset.Width);
        Assert.Equal(1, asset.Height);

        // Verify the row is in the DB
        var fromDb = await AssetRepo().GetByIdAsync(asset.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("banner.png", fromDb!.FileName);
        Assert.Equal("image/png",  fromDb.MimeType);
    }

    /// <summary>AC5: Non-image upload (PDF) creates row without dimensions.</summary>
    [Fact]
    public async Task Upload_ValidPdf_CreatesAssetWithoutDimensions()
    {
        var userId  = await SeedUserAsync();
        var service = MakeService();

        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 fake content");
        var file     = MakeFormFile(pdfBytes, "report.pdf", "application/pdf");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);
        Assert.Equal("report.pdf",      asset!.FileName);
        Assert.Equal("application/pdf", asset.MimeType);
        Assert.Null(asset.Width);
        Assert.Null(asset.Height);
    }

    // ── AC4: MIME allow-list rejects disallowed types ─────────────────────────

    /// <summary>AC4: Disallowed MIME type returns error, no row created.</summary>
    [Fact]
    public async Task Upload_DisallowedMime_ReturnsError()
    {
        var userId  = await SeedUserAsync();
        var service = MakeService();

        var exeBytes = new byte[] { 0x4D, 0x5A, 0x00 }; // MZ header
        var file     = MakeFormFile(exeBytes, "malware.exe", "application/x-msdownload");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(asset);
        Assert.NotNull(error);
        Assert.Contains("not permitted", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AC4: Charset-parameterised Content-Type is normalised before allow-list check.</summary>
    [Fact]
    public async Task Upload_MimeWithCharset_NormalisedAndAllowed()
    {
        var userId  = await SeedUserAsync();
        var service = MakeService();

        var csvBytes = System.Text.Encoding.UTF8.GetBytes("col1,col2\nval1,val2");
        var file     = MakeFormFile(csvBytes, "data.csv", "text/csv; charset=utf-8");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);
        Assert.Equal("text/csv", asset!.MimeType);
    }

    /// <summary>Empty file returns validation error.</summary>
    [Fact]
    public async Task Upload_EmptyFile_ReturnsError()
    {
        var userId  = await SeedUserAsync();
        var service = MakeService();

        var file = MakeFormFile(Array.Empty<byte>(), "empty.png", "image/png");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(asset);
        Assert.NotNull(error);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── AC2: UNC backend rejects missing root path ────────────────────────────

    [Fact]
    public async Task Upload_UncBackend_MissingRootPath_ThrowsOnSave()
    {
        var userId  = await SeedUserAsync();
        var unc     = new UncStorageBackend(new StorageOptions { UncRootPath = string.Empty });
        var service = new MediaUploadService(unc, AssetRepo(), new NoOpImageProcessingService(), new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()));

        var pngBytes = MinimalPng();
        var file     = MakeFormFile(pngBytes, "test.png", "image/png");

        var (asset, error) = await service.UploadAsync(file, userId);

        // Error is returned (not thrown) because the service catches storage exceptions
        Assert.Null(asset);
        Assert.NotNull(error);
    }

    // ── On-prem only: unsupported backends are refused by option validation ──

    [Fact]
    public void StorageOptions_Rejects_Unsupported_Backends_And_Missing_Roots()
    {
        Assert.Contains("on-prem", new StorageOptions { Backend = "azure_blob" }.Validate());
        Assert.Contains("on-prem", new StorageOptions { Backend = "s3" }.Validate());
        Assert.Contains("UncRootPath", new StorageOptions { Backend = "unc" }.Validate());
        Assert.Contains("not a UNC path", new StorageOptions { Backend = "unc", UncRootPath = "D:\\share" }.Validate());
        Assert.Null(new StorageOptions { Backend = "unc", UncRootPath = @"\\files\va-cms" }.Validate());
        Assert.Null(new StorageOptions { Backend = "local", LocalRootPath = "/var/va-cms-uploads" }.Validate());
        Assert.Contains("inside the application root",
            new StorageOptions { Backend = "local", LocalRootPath = "/srv/app/wwwroot/files" }.Validate("/srv/app"));
        Assert.Null(new StorageOptions { Backend = "local", LocalRootPath = "/srv/app/.uploads" }.Validate());   // Development skips the web-root check
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
    /// Returns a valid 1×1 RGBA PNG (67 bytes).
    /// </summary>
    private static byte[] MinimalPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}

/// <summary>
/// In-memory storage backend for unit/integration tests.
/// Does not touch the filesystem — satisfies IStorageBackend contract.
/// </summary>
internal class InMemoryStorageBackend : IStorageBackend
{
    private readonly Dictionary<string, byte[]> _files = new();

    public string BackendName => "local";

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

    public byte[]? GetBytes(string storagePath) =>
        _files.TryGetValue(storagePath, out var b) ? b : null;

    /// <summary>Paths currently held (after deletes).</summary>
    public IReadOnlyCollection<string> Paths => _files.Keys;
}

/// <summary>
/// No-op imaging service for tests that don't need WebP processing.
/// ShouldProcess always returns false so the image pipeline is bypassed entirely.
/// </summary>
internal class NoOpImageProcessingService : IImageProcessingService
{
    public bool ShouldProcess(string mimeType) => false;

    public Task<byte[]> GenerateWebPAsync(byte[] imageBytes, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<byte>());
}
