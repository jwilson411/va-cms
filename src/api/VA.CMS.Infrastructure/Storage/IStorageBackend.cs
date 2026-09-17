using Microsoft.AspNetCore.Http;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Abstraction for the file storage backend.
/// Implementations: LocalStorageBackend, UncStorageBackend, AzureBlobStorageBackend.
/// Selected at startup by StorageOptions.Backend.
/// BRD FR-MEDIA-07, FR-SECURITY-06: file stored outside web root, backend configurable.
/// </summary>
public interface IStorageBackend
{
    /// <summary>
    /// Backend identifier stored in MediaAsset.StorageBackend.
    /// One of: "local", "unc".
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// Saves the uploaded file stream to storage.
    /// Returns the relative storage path recorded in MediaAsset.StoragePath.
    /// </summary>
    Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Saves a raw byte array to storage (used for generated WebP variants).
    /// Returns the relative storage path.
    /// Issue #41 — FR-MEDIA-02.
    /// </summary>
    Task<string> SaveBytesAsync(byte[] bytes, string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a previously saved file from storage.
    /// Called when a virus scan flags the file so it is not retained on disk.
    /// No-op if the file does not exist.
    /// Issue #45 — FR-MEDIA-04.
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Opens the stored file for reading so the API can stream it to clients
    /// (GET /api/v1/media/serve/{id}) — files live outside the web root and are
    /// never served as static content (BRD FR-SECURITY-06). Returns null when the
    /// file does not exist. Default: not readable (backends override).
    /// </summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
        => Task.FromResult<Stream?>(null);
}
