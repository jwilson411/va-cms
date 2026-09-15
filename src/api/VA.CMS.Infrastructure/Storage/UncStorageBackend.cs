using Microsoft.AspNetCore.Http;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// UNC network share storage backend.
/// Writes to StorageOptions.UncRootPath + relative storagePath.
/// The UNC root must be outside the web root. BRD FR-MEDIA-07, FR-SECURITY-06.
/// On Windows the path separator is backslash; we normalise using Path.Combine
/// which respects the host OS.
/// </summary>
public class UncStorageBackend : IStorageBackend
{
    private readonly StorageOptions _options;

    public UncStorageBackend(StorageOptions options)
    {
        _options = options;
    }

    public string BackendName => "unc";

    public async Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storagePath);
        EnsureDirectory(fullPath);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(fs, ct);

        return storagePath;
    }

    /// <inheritdoc />
    public async Task<string> SaveBytesAsync(byte[] bytes, string storagePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storagePath);
        EnsureDirectory(fullPath);

        await File.WriteAllBytesAsync(fullPath, bytes, ct);
        return storagePath;
    }

    private string ResolvePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(_options.UncRootPath))
            throw new InvalidOperationException(
                "Storage:UncRootPath must be configured when Backend=unc.");
        return Path.Combine(_options.UncRootPath, storagePath);
    }

    private static void EnsureDirectory(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for path: {fullPath}");
        Directory.CreateDirectory(dir);
    }
}
