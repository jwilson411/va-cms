namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Virus scan integration hook for media uploads.
/// BRD FR-MEDIA-04 — Issue #45.
///
/// VA teams inject their AV tool (ClamAV, Windows Defender ATP, etc.) by
/// registering a concrete implementation of this interface via DI.
/// Deployments without AV tooling use <see cref="NoOpVirusScanService"/>,
/// which always returns clean.
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
    /// </returns>
    Task<bool> ScanAsync(Stream stream, CancellationToken ct = default);
}

/// <summary>
/// Default no-op implementation of <see cref="IVirusScanService"/>.
/// Always reports the file as clean.
/// Used by deployments that do not have an AV tool configured.
/// Replace via DI with a real implementation to enable scanning.
/// BRD FR-MEDIA-04 — Issue #45.
/// </summary>
public class NoOpVirusScanService : IVirusScanService
{
    /// <inheritdoc />
    public Task<bool> ScanAsync(Stream stream, CancellationToken ct = default)
        => Task.FromResult(true);
}
