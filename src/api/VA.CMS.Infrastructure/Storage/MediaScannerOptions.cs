using System.ComponentModel.DataAnnotations;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>Which engine <see cref="IVirusScanService"/> talks to.</summary>
public enum MediaScannerMode
{
    /// <summary>No scanning (development only; refused in Production).</summary>
    Disabled,
    /// <summary>ICAP RESPMOD — Trend Micro, McAfee/Trellix, Symantec Protection Engine, etc.</summary>
    Icap,
    /// <summary>ClamAV clamd INSTREAM — local dev and CI.</summary>
    ClamAv,
}

/// <summary>
/// Bound from the "Media:Scanner" configuration section (#159). Host, port and
/// mode are deployment wiring, like the SMTP relay, so they stay in configuration.
/// </summary>
public sealed class MediaScannerOptions
{
    public const string SectionName = "Media:Scanner";

    public MediaScannerMode Mode { get; set; } = MediaScannerMode.Disabled;

    [Required, MinLength(1)]
    public string Host { get; set; } = "localhost";

    /// <summary>Defaults to 1344 (ICAP) or 3310 (clamd) when zero.</summary>
    [Range(0, 65535)]
    public int Port { get; set; }

    /// <summary>ICAP service path, e.g. "/avscan" or "/reqmod"; ignored for ClamAV.</summary>
    public string ServicePath { get; set; } = "/avscan";

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When the engine is unreachable: true rejects the upload (503) and stores nothing;
    /// false stores the file and records IsVirusScanPassed = NULL. Null resolves to
    /// true everywhere except Development.
    /// </summary>
    public bool? FailClosed { get; set; }

    public int EffectivePort => Port > 0 ? Port : Mode == MediaScannerMode.ClamAv ? 3310 : 1344;

    public bool ResolveFailClosed(bool isDevelopment) => FailClosed ?? !isDevelopment;

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Max(1, TimeoutSeconds));
}
