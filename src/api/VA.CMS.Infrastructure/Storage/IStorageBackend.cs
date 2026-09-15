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
    /// One of: "local", "unc", "azure_blob".
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// Saves the uploaded file stream to storage.
    /// Returns the relative storage path recorded in MediaAsset.StoragePath.
    /// </summary>
    Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default);
}
