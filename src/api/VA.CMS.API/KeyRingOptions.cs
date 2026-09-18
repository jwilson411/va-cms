using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;

namespace VA.CMS.API;

/// <summary>
/// Where the ASP.NET Data Protection key ring lives (#168). The key ring encrypts webhook
/// secrets at rest; without a persisted, shared location a second node — or the same
/// node after an app-pool identity change — cannot decrypt what the first one wrote.
///
///   DataProtection:KeysPath            directory (local or UNC) every node can read and write.
///                                      Required outside Development.
///   DataProtection:DpapiNgDescriptor   optional Windows CNG DPAPI-NG protection rule, e.g.
///                                      "SID=S-1-5-21-…" for the gMSA the app pools run as, so
///                                      the key files on the share are unreadable to anyone else.
///                                      Ignored off Windows.
///
/// On-prem only: no cloud key vault (see docs/DEPLOYMENT.md "Data Protection key ring").
/// </summary>
public sealed class KeyRingOptions
{
    public const string SectionName = "DataProtection";
    public const string ApplicationName = "va-cms";

    [MaxLength(1024)]
    public string? KeysPath { get; set; }

    [MaxLength(512)]
    public string? DpapiNgDescriptor { get; set; }

    public string? Validate(bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(DpapiNgDescriptor) && !OperatingSystem.IsWindows())
            return "DataProtection:DpapiNgDescriptor is set but DPAPI-NG is Windows-only; clear it on this host.";

        if (isDevelopment) return null;

        if (string.IsNullOrWhiteSpace(KeysPath))
            return "DataProtection:KeysPath is required outside Development so the key ring that protects webhook " +
                   "secrets survives restarts and is shared by every node (a local directory or a UNC share).";

        if (KeysPath.Any(char.IsControl))
            return "DataProtection:KeysPath contains control characters.";

        return null;
    }

    /// <summary>Applies the options to the Data Protection builder. Called once from Program.cs.</summary>
    public void Apply(IDataProtectionBuilder builder)
    {
        builder.SetApplicationName(ApplicationName);

        if (string.IsNullOrWhiteSpace(KeysPath)) return;   // Development: framework default location

        var dir = new DirectoryInfo(KeysPath);
        dir.Create();
        builder.PersistKeysToFileSystem(dir);

        if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrWhiteSpace(DpapiNgDescriptor))
                builder.ProtectKeysWithDpapiNG(DpapiNgDescriptor, Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiNGProtectionDescriptorFlags.None);
            else
                builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
    }
}
