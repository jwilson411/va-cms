using Microsoft.AspNetCore.Http;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage backend (hook/stub).
/// Full implementation requires Azure.Storage.Blobs NuGet which is out-of-scope
/// for this story. This class provides the correct interface contract so that
/// Program.cs can register it and the service layer can call it.
/// A follow-up story will fill in the real Azure SDK calls.
/// BRD FR-MEDIA-07.
/// </summary>
public class AzureBlobStorageBackend : IStorageBackend
{
    private readonly StorageOptions _options;

    public AzureBlobStorageBackend(StorageOptions options)
    {
        _options = options;
    }

    public string BackendName => "azure_blob";

    public Task<string> SaveAsync(IFormFile file, string storagePath, CancellationToken ct = default)
    {
        // Azure.Storage.Blobs integration is deferred to a later story.
        // Throw a clear error in dev; replace with real BlobClient upload when the
        // Azure SDK package is added.
        throw new NotImplementedException(
            "Azure Blob Storage backend is not yet implemented. " +
            "Set Storage:Backend to 'local' or 'unc' for now.");
    }

    /// <inheritdoc />
    public Task<string> SaveBytesAsync(byte[] bytes, string storagePath, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "Azure Blob Storage backend is not yet implemented. " +
            "Set Storage:Backend to 'local' or 'unc' for now.");
    }
}
