using Microsoft.AspNetCore.Http;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #45 — Add virus scan integration hook on upload.
///
/// BRD FR-MEDIA-04.
///
/// Acceptance criteria:
///   AC1: IVirusScanService interface exists with ScanAsync(stream) method.
///   AC2: Default no-op implementation (NoOpVirusScanService) always returns clean.
///   AC3: File flagged as virus is rejected; MediaAsset.IsVirusScanPassed = 0; file deleted from storage.
///   AC4: VA teams can inject their AV tool via the interface (via DI substitution).
///
/// Test strategy:
///   - Interface / no-op unit tests (no DB, no storage).
///   - MediaUploadService integration tests: real DB + TrackingInMemoryStorageBackend.
///     Inject stub scan implementations to simulate clean / infected results.
///   - Verify IsVirusScanPassed persisted in DB after both clean and infected uploads.
/// </summary>
[Collection("Database")]
public class Issue45AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private IMediaAssetRepository AssetRepo() =>
        new MediaAssetRepository(fixture.CreateDb());

    private IMediaExtendedRepository ExtendedRepo() =>
        new MediaExtendedRepository(fixture.CreateDb());

    private static IFormFile MakeFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static VirusScanTestStorageBackend NewStorage() => new();

    private static byte[] MinimalPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // ── AC1: IVirusScanService interface ────────────────────────────────────

    /// <summary>AC1: IVirusScanService has a ScanAsync method accepting a Stream.</summary>
    [Fact]
    public void IVirusScanService_HasScanAsyncMethod()
    {
        var method = typeof(IVirusScanService).GetMethod(
            nameof(IVirusScanService.ScanAsync),
            new[] { typeof(Stream), typeof(CancellationToken) });

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method!.ReturnType);
    }

    // ── AC2: NoOpVirusScanService always returns clean ───────────────────────

    /// <summary>AC2: No-op always returns true (clean) regardless of content.</summary>
    [Fact]
    public async Task NoOpVirusScanService_AlwaysReturnsClean()
    {
        IVirusScanService svc = new NoOpVirusScanService();
        using var stream = new MemoryStream(new byte[] { 0x58, 0x35, 0x4F, 0x21 }); // EICAR start
        var result = await svc.ScanAsync(stream);
        Assert.True(result, "NoOpVirusScanService must always return clean");
    }

    /// <summary>AC2: No-op returns true for empty stream.</summary>
    [Fact]
    public async Task NoOpVirusScanService_EmptyStream_ReturnsClean()
    {
        IVirusScanService svc = new NoOpVirusScanService();
        using var stream = new MemoryStream(Array.Empty<byte>());
        var result = await svc.ScanAsync(stream);
        Assert.True(result);
    }

    // ── AC3: Infected file is rejected; IsVirusScanPassed = 0; file deleted ──

    /// <summary>
    /// AC3: When the scan returns false (virus detected), UploadAsync returns an error,
    /// no file is retained in storage, and the DB row has IsVirusScanPassed = 0.
    /// </summary>
    [Fact]
    public async Task Upload_InfectedFile_IsRejected_NotRetainedInStorage()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var storage = NewStorage();
        var service = BuildService(storage, virusDetected: true);

        var pngBytes = MinimalPng();
        var file     = MakeFormFile(pngBytes, "infected.png", "image/png");

        var (asset, error) = await service.UploadAsync(file, userId);

        // AC3: Upload returns error, asset is null
        Assert.Null(asset);
        Assert.NotNull(error);
        Assert.Contains("virus", error!, StringComparison.OrdinalIgnoreCase);

        // AC3: File is deleted from storage (storage has no clean files)
        Assert.Equal(0, storage.CleanFileCount);
    }

    /// <summary>
    /// AC3: The rejected upload creates an auditable DB row with IsVirusScanPassed = 0.
    /// </summary>
    [Fact]
    public async Task Upload_InfectedFile_DbRow_IsVirusScanPassed_IsFalse()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var storage = NewStorage();
        var service = BuildService(storage, virusDetected: true);

        var pngBytes = MinimalPng();
        var file     = MakeFormFile(pngBytes, "eicar.png", "image/png");

        // Upload — this will fail (virus)
        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(asset);
        Assert.NotNull(error);

        // Find the tombstone row via the storage path that was recorded and deleted.
        // Since we cannot know the exact ID, we verify via the extended repo that
        // at least one asset with IsVirusScanPassed = false exists for this uploader.
        var page = await AssetRepo().ListAsync(1, 200);
        // The rejected asset is NOT shown in the public-facing list (IsVirusScanPassed = 0 is filtered out).
        // Verify via GetByIdAsync — we need to find the tombstone directly.
        // Use a direct DB query to confirm the tombstone exists.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT TOP 1 Id, IsVirusScanPassed FROM [MediaAsset] " +
            "WHERE UploadedById = @UserId AND IsVirusScanPassed = 0 " +
            "ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected a tombstone row with IsVirusScanPassed = 0");
        var isVirusScanPassed = reader.IsDBNull(reader.GetOrdinal("IsVirusScanPassed"))
            ? (bool?)null
            : reader.GetBoolean(reader.GetOrdinal("IsVirusScanPassed"));
        Assert.False(isVirusScanPassed, "IsVirusScanPassed must be 0 for rejected file");
    }

    // ── AC1 + AC4: Clean upload sets IsVirusScanPassed = 1 ──────────────────

    /// <summary>
    /// AC4: When the scan returns true (clean), upload succeeds and
    /// IsVirusScanPassed = 1 is persisted in the DB.
    /// </summary>
    [Fact]
    public async Task Upload_CleanFile_IsVirusScanPassed_IsTrue()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var storage = NewStorage();
        var service = BuildService(storage, virusDetected: false);

        var pngBytes = MinimalPng();
        var file     = MakeFormFile(pngBytes, "clean.png", "image/png");

        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);

        // Reload from DB and verify IsVirusScanPassed = 1
        var fromDb = await AssetRepo().GetByIdAsync(asset!.Id);
        Assert.NotNull(fromDb);
        Assert.True(fromDb!.IsVirusScanPassed, "IsVirusScanPassed must be 1 for clean upload");
    }

    /// <summary>
    /// AC4: VA teams can substitute a custom IVirusScanService implementation via DI.
    /// Verify the interface is injectable by creating a custom stub directly.
    /// </summary>
    [Fact]
    public async Task CustomVirusScanService_IsInjectable_AndCalledOnUpload()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var storage = NewStorage();
        var scanSpy = new SpyVirusScanService(returnValue: true);

        var service = new MediaUploadService(
            storage,
            AssetRepo(),
            new ImageProcessingService(),
            scanSpy,
            ExtendedRepo());

        var file = MakeFormFile(MinimalPng(), "custom-scan.png", "image/png");
        var (asset, error) = await service.UploadAsync(file, userId);

        Assert.Null(error);
        Assert.NotNull(asset);
        Assert.True(scanSpy.WasCalled, "Custom IVirusScanService implementation must be called on upload");
    }

    /// <summary>
    /// AC3: Infected file is removed from storage — storage has zero clean files after rejection.
    /// </summary>
    [Fact]
    public async Task Upload_InfectedFile_FileIsRemovedFromStorage()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var storage = NewStorage();
        var service = BuildService(storage, virusDetected: true);

        var file = MakeFormFile(MinimalPng(), "malware.png", "image/png");
        await service.UploadAsync(file, userId);

        // File must not remain in storage after rejection
        Assert.Equal(0, storage.CleanFileCount);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private MediaUploadService BuildService(
        VirusScanTestStorageBackend storage,
        bool virusDetected)
    {
        return new MediaUploadService(
            storage,
            AssetRepo(),
            new ImageProcessingService(),
            new StubVirusScanService(!virusDetected),   // ScanAsync returns !virusDetected
            ExtendedRepo());
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>
/// Stub that always returns a fixed scan result.
/// </summary>
internal sealed class StubVirusScanService : IVirusScanService
{
    private readonly bool _result;
    public StubVirusScanService(bool result) => _result = result;
    public Task<bool> ScanAsync(Stream stream, CancellationToken ct = default)
        => Task.FromResult(_result);
}

/// <summary>
/// Spy that records whether ScanAsync was called.
/// </summary>
internal sealed class SpyVirusScanService : IVirusScanService
{
    private readonly bool _result;
    public bool WasCalled { get; private set; }
    public SpyVirusScanService(bool returnValue) => _result = returnValue;
    public Task<bool> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        WasCalled = true;
        return Task.FromResult(_result);
    }
}

/// <summary>
/// In-memory storage backend for Issue #45 tests.
/// Supports DeleteAsync (required for virus scan rejection) and exposes CleanFileCount.
/// </summary>
internal sealed class VirusScanTestStorageBackend : IStorageBackend
{
    private readonly Dictionary<string, byte[]> _files = new();

    public string BackendName => "local";

    /// <summary>Files that have been saved and NOT subsequently deleted.</summary>
    public int CleanFileCount => _files.Count;
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
