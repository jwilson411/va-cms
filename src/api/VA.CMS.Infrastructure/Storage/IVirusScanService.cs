namespace VA.CMS.Infrastructure.Storage;

/// <summary>What the scanner concluded about a stream (#159).</summary>
public enum VirusScanVerdict
{
    /// <summary>No signature matched.</summary>
    Clean,
    /// <summary>Malware detected; <see cref="VirusScanResult.ThreatName"/> names it when the engine says.</summary>
    Infected,
    /// <summary>The engine could not be reached or answered with an error — the file was NOT scanned.</summary>
    Unavailable,
}

/// <summary>Outcome of a scan, with enough detail to audit it.</summary>
public sealed record VirusScanResult(VirusScanVerdict Verdict, string? ThreatName = null, string? Detail = null)
{
    public static readonly VirusScanResult Clean = new(VirusScanVerdict.Clean);
    public static VirusScanResult Infected(string? threat)    => new(VirusScanVerdict.Infected, threat);
    public static VirusScanResult Unavailable(string detail)  => new(VirusScanVerdict.Unavailable, Detail: detail);
    public bool IsClean => Verdict == VirusScanVerdict.Clean;
}

/// <summary>
/// Virus scan integration for media uploads (BRD FR-MEDIA-04, NIST SI-3).
///
/// Implementations: <see cref="IcapVirusScanService"/> (enterprise scanners over
/// ICAP RESPMOD), <see cref="ClamAvVirusScanService"/> (clamd INSTREAM for local
/// dev and CI) and <see cref="NoOpVirusScanService"/> (Mode = Disabled; refused
/// in Production). Selected by the Media:Scanner configuration section (#159).
/// </summary>
public interface IVirusScanService
{
    /// <summary>
    /// Scans the provided stream for malware.
    /// </summary>
    /// <param name="stream">
    /// Readable stream containing the file content to scan.
    /// Implementations must not dispose the stream.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the file is clean;
    /// <see langword="false"/> if a virus / malware signature was detected.
    /// An unreachable engine surfaces as an exception here; prefer <see cref="ScanDetailedAsync"/>.
    /// </returns>
    Task<bool> ScanAsync(Stream stream, CancellationToken ct = default);

    /// <summary>
    /// Scans the stream and reports Clean / Infected / Unavailable so the caller can
    /// fail closed without confusing "engine down" with "malware found". The default
    /// maps <see cref="ScanAsync"/> and treats an exception as Unavailable.
    /// </summary>
    async Task<VirusScanResult> ScanDetailedAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            return await ScanAsync(stream, ct) ? VirusScanResult.Clean : VirusScanResult.Infected(null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return VirusScanResult.Unavailable(ex.Message);
        }
    }
}

/// <summary>
/// Scanner for Media:Scanner:Mode = Disabled. Always reports the file as clean.
/// Only legitimate where uploads are not user-supplied risk (local development);
/// Program.cs refuses to start with it in Production (#159).
/// </summary>
public class NoOpVirusScanService : IVirusScanService
{
    /// <inheritdoc />
    public Task<bool> ScanAsync(Stream stream, CancellationToken ct = default)
        => Task.FromResult(true);
}
