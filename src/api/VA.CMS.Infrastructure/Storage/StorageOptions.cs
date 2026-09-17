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
    /// Selected backend: "local" (default) or "unc". The deployment is on-prem only;
    /// "azure_blob" and any other value are rejected by <see cref="Validate"/> at startup.
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

    /// <summary>Backend names this on-prem deployment supports.</summary>
    public static readonly string[] SupportedBackends = ["local", "unc"];

    /// <summary>
    /// Fail-fast validation for startup (#170): the backend must be supported and its
    /// root path must be set. Pass <paramref name="contentRootPath"/> outside Development to
    /// also reject a local root inside the web root (BRD FR-SECURITY-06; the dev convention
    /// is a gitignored ./.uploads folder). Returns an error message or null.
    /// </summary>
    public string? Validate(string? contentRootPath = null)
    {
        var backend = (Backend ?? string.Empty).Trim().ToLowerInvariant();
        if (!SupportedBackends.Contains(backend))
        {
            return $"Storage:Backend '{Backend}' is not supported. This deployment is on-prem only: use 'local' " +
                   $"(Storage:LocalRootPath) or 'unc' (Storage:UncRootPath).";
        }

        if (backend == "unc")
        {
            if (string.IsNullOrWhiteSpace(UncRootPath))
                return "Storage:UncRootPath is required when Storage:Backend=unc.";
            if (!UncRootPath.StartsWith(@"\\", StringComparison.Ordinal) && !UncRootPath.StartsWith("//", StringComparison.Ordinal))
                return $"Storage:UncRootPath '{UncRootPath}' is not a UNC path (expected \\\\server\\share\\…).";
            return null;
        }

        if (string.IsNullOrWhiteSpace(LocalRootPath))
            return "Storage:LocalRootPath is required when Storage:Backend=local.";

        if (contentRootPath is not null)
        {
            var root = Path.GetFullPath(LocalRootPath);
            var web  = Path.GetFullPath(contentRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((root + Path.DirectorySeparatorChar).StartsWith(web, StringComparison.OrdinalIgnoreCase))
            {
                return $"Storage:LocalRootPath '{LocalRootPath}' is inside the application root '{contentRootPath}'; " +
                       "uploads must live outside the web root (BRD FR-SECURITY-06).";
            }
        }

        return null;
    }
}
