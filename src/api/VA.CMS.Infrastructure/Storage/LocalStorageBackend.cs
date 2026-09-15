using Microsoft.AspNetCore.Http;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Local filesystem storage backend.
/// Files are written to StorageOptions.LocalRootPath, which must be
/// outside the web root. BRD FR-SECURITY-06.
/// </summary>
public class LocalStorageBackend : IStorageBackend
{
    private readonly StorageOptions _options;

    public LocalStorageBackend(StorageOptions options)
    {
        _options = options;
    }

    public string BackendName => "local";

    public async Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_options.LocalRootPath, storagePath);
        var dir = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for path: {fullPath}");

        Directory.CreateDirectory(dir);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(fs, ct);

        return storagePath;
    }
}
