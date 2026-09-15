namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Configuration for the storage backend.
/// Bound from appsettings.json section "Storage".
/// BRD FR-MEDIA-07, FR-SECURITY-06.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Selected backend. One of: "local", "unc", "azure_blob".
    /// Defaults to "local".
    /// </summary>
    public string Backend { get; set; } = "local";

    /// <summary>
    /// Root path on disk for LocalStorageBackend.
    /// MUST be outside the web root. Default: /var/va-cms-uploads (Linux) or
    /// C:\va-cms-uploads (Windows). Set via env var Storage__LocalRootPath.
    /// BRD FR-SECURITY-06: stored outside web root.
    /// </summary>
    public string LocalRootPath { get; set; } = "/var/va-cms-uploads";

    /// <summary>
    /// UNC path root for UncStorageBackend (e.g. \\\\server\\share\\va-cms).
    /// Set via env var Storage__UncRootPath.
    /// </summary>
    public string UncRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Azure Blob Storage connection string for AzureBlobStorageBackend.
    /// Set via env var Storage__AzureBlobConnectionString (never in appsettings.json).
    /// </summary>
    public string AzureBlobConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Azure Blob container name. Default: "va-cms-media".
    /// </summary>
    public string AzureBlobContainerName { get; set; } = "va-cms-media";
}
