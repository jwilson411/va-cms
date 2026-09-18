using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Media upload service.
/// Validates the upload, delegates storage to the configured IStorageBackend,
/// runs the virus scan hook, then creates a MediaAsset row via the repository.
/// For image files (JPEG, PNG, WebP), also resizes to max 1920px wide and
/// generates a WebP variant stored alongside the original.
/// BRD FR-MEDIA-01, FR-MEDIA-02, FR-MEDIA-04, FR-MEDIA-07, FR-SECURITY-06.
/// </summary>
/// <summary>Why an upload was refused; lets the controller pick the status code (#159).</summary>
public enum MediaUploadFailure
{
    /// <summary>Bad input: type, size, content mismatch — 400.</summary>
    Validation,
    /// <summary>The virus scanner found a threat — 400 (file not stored).</summary>
    Infected,
    /// <summary>The virus scanner could not be reached and the deployment fails closed — 503 (file not stored).</summary>
    ScannerUnavailable,
    /// <summary>Storage backend error — 400 as before.</summary>
    Storage,
}

/// <summary>
/// Outcome of <see cref="IMediaUploadService.UploadAsync"/>. Deconstructs to the
/// historical (Asset, Error) pair so existing callers and tests are unchanged.
/// </summary>
public sealed record MediaUploadOutcome(MediaAsset? Asset, string? Error, MediaUploadFailure? Failure)
{
    public static MediaUploadOutcome Success(MediaAsset asset) => new(asset, null, null);
    public static MediaUploadOutcome Fail(MediaUploadFailure failure, string error) => new(null, error, failure);

    public void Deconstruct(out MediaAsset? asset, out string? error)
    {
        asset = Asset;
        error = Error;
    }
}

public interface IMediaUploadService
{
    /// <summary>
    /// Validate, store, scan, and record a file upload.
    /// Returns the created MediaAsset on success.
    /// Returns an error message (and failure kind) on validation, storage, or scan failure.
    /// </summary>
    Task<MediaUploadOutcome> UploadAsync(
        IFormFile file,
        long uploadedById,
        CancellationToken ct = default);
}

public class MediaUploadService : IMediaUploadService
{
    private readonly ILogger<MediaUploadService>? _logger;
    private readonly IStorageBackend _storage;
    private readonly IMediaAssetRepository _assets;
    private readonly IImageProcessingService _imaging;
    private readonly IVirusScanService _virusScan;
    private readonly IMediaExtendedRepository _mediaExtended;
    private readonly ISiteSettingsService _settings;
    private readonly bool _failClosed;
    private readonly IAuditLogRepository? _audit;

    public MediaUploadService(
        IStorageBackend storage,
        IMediaAssetRepository assets,
        IImageProcessingService imaging,
        IVirusScanService virusScan,
        IMediaExtendedRepository mediaExtended,
        ISiteSettingsService? settings = null,
        bool failClosed = true,
        IAuditLogRepository? audit = null,
        ILogger<MediaUploadService>? logger = null)
    {
        _logger        = logger;
        _storage       = storage;
        _assets        = assets;
        _imaging       = imaging;
        _virusScan     = virusScan;
        _mediaExtended = mediaExtended;
        // Limits and the MIME allow-list are site settings (issue #145); code defaults when not supplied.
        _settings      = settings ?? StaticSiteSettings.Defaults;
        // Media:Scanner:FailClosed (#159): reject uploads when the engine cannot be reached.
        _failClosed    = failClosed;
        _audit         = audit;
    }

    public async Task<MediaUploadOutcome> UploadAsync(
        IFormFile file,
        long uploadedById,
        CancellationToken ct = default)
    {
        // 1. Validate file is not empty and within the configured size limit (media.maxUploadBytes)
        if (file.Length == 0)
            return MediaUploadOutcome.Fail(MediaUploadFailure.Validation, "File must not be empty.");

        var maxBytes = _settings.GetLong(SiteSettingKeys.MediaMaxUploadBytes);
        if (maxBytes > 0 && file.Length > maxBytes)
            return MediaUploadOutcome.Fail(MediaUploadFailure.Validation, $"File is {file.Length:N0} bytes; the maximum upload size is {maxBytes:N0} bytes.");

        // 2. Resolve MIME — the declared ContentType is a claim, normalised ("image/jpg" → "image/jpeg")
        var mimeType = NormaliseMime(file.ContentType);

        // 3. MIME allow-list check (BRD FR-SECURITY-06) against media.allowedMimeTypes
        var allowed = _settings.GetStringList(SiteSettingKeys.MediaAllowedMimeTypes);
        if (!MimeAllowList.IsAllowed(mimeType, allowed))
            return MediaUploadOutcome.Fail(MediaUploadFailure.Validation,
                $"File type '{mimeType}' is not permitted. Allowed types: {string.Join(", ", allowed)}.");

        // 3a. The bytes must agree with the claim (#158): sniff the content and check the
        //     extension, so a script-bearing SVG or HTML cannot arrive as "image/png".
        var safeFileName = SanitizeFileName(file.FileName);
        var ext          = Path.GetExtension(safeFileName).TrimStart('.').ToLowerInvariant();
        IReadOnlyList<string> detected;
        using (var sniff = file.OpenReadStream())
            detected = MediaContentSniffer.Detect(sniff);

        if (!detected.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
        {
            var seen = detected.Count == 0 ? "an unrecognised format" : $"'{detected[0]}'";
            return MediaUploadOutcome.Fail(MediaUploadFailure.Validation, $"File content does not match the declared type '{mimeType}' (content looks like {seen}).");
        }
        if (string.IsNullOrEmpty(ext) || !MediaContentSniffer.ExtensionMatches(mimeType, ext))
            return MediaUploadOutcome.Fail(MediaUploadFailure.Validation, $"File extension '.{ext}' does not match the declared type '{mimeType}'.");

        // 3b. SVG is only reachable when an administrator has added it to the allow-list;
        //     even then active content is stripped before the file is stored.
        byte[]? replacementBytes = null;
        if (mimeType == "image/svg+xml")
        {
            using var svgStream = file.OpenReadStream();
            var (sanitized, svgError) = SvgSanitizer.Sanitize(svgStream);
            if (sanitized is null)
                return MediaUploadOutcome.Fail(MediaUploadFailure.Validation, svgError!);
            replacementBytes = sanitized;
        }

        // 4. Build a safe storage path: yyyy/MM/<guid>.<ext>
        //    Using a GUID prevents enumeration; the original filename is preserved in FileName column.
        var safeExt    = ext;
        var datePath   = DateTime.UtcNow.ToString("yyyy/MM");
        var guid       = Guid.NewGuid().ToString("N");
        var uniqueName = $"{guid}.{safeExt}";
        var storagePath = $"{datePath}/{uniqueName}";

        // 5. Read image dimensions (only for image MIME types, best-effort)
        int? width  = null;
        int? height = null;

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            (width, height) = TryReadImageDimensions(file);
        }

        // 6. Save original file to backend (BRD FR-SECURITY-06: stored outside web root)
        string savedPath;
        try
        {
            savedPath = replacementBytes is null
                ? await _storage.SaveAsync(file, storagePath, ct)
                : await _storage.SaveBytesAsync(replacementBytes, storagePath, ct);
        }
        catch (Exception ex)
        {
            // #166: the exception (path, share name, Win32 error) goes to the log under the
            // request's correlation id; the client gets a fixed message.
            _logger?.LogError(ex, "Storage backend failed to save upload {FileName} ({MimeType}, {Bytes} bytes).", safeFileName, mimeType, file.Length);
            return MediaUploadOutcome.Fail(MediaUploadFailure.Storage, "The file could not be stored. The error has been logged; try again or contact the administrator.");
        }

        // 7. Virus scan (BRD FR-MEDIA-04 — Issue #45; fail-closed per #159 / NIST SI-3)
        //    Scan the upload stream. Infected: delete from storage, keep an auditable
        //    tombstone row with IsVirusScanPassed=0, reject. Engine unreachable: reject
        //    with 503 and store nothing when failing closed; otherwise store with
        //    IsVirusScanPassed left NULL ("not scanned") so it can be re-scanned later.
        VirusScanResult scan;
        try
        {
            using var scanStream = file.OpenReadStream();
            scan = await _virusScan.ScanDetailedAsync(scanStream, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await _storage.DeleteAsync(savedPath, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            scan = VirusScanResult.Unavailable(ex.Message);
        }

        if (scan.Verdict == VirusScanVerdict.Unavailable && _failClosed)
        {
            await _storage.DeleteAsync(savedPath, ct);
            await AuditAsync(uploadedById, "VirusScanUnavailable", new { fileName = safeFileName, mimeType, detail = scan.Detail });
            return MediaUploadOutcome.Fail(MediaUploadFailure.ScannerUnavailable,
                "The file could not be scanned for malware because the scanning service is unavailable. Nothing was stored; try again later.");
        }

        if (scan.Verdict == VirusScanVerdict.Infected)
        {
            // File is infected: delete from storage, create the DB row with IsVirusScanPassed=0
            // so the rejection is auditable, then return error.
            await _storage.DeleteAsync(savedPath, ct);

            // Create a tombstone asset row so the rejection is auditable (IsVirusScanPassed=0).
            var rejectedAsset = new MediaAsset
            {
                FileName       = safeFileName,
                StoragePath    = savedPath,      // path that was deleted
                StorageBackend = _storage.BackendName,
                MimeType       = mimeType,
                FileSizeBytes  = replacementBytes?.LongLength ?? file.Length,
                Width          = width,
                Height         = height,
                UploadedById   = uploadedById,
            };
            var rejectedId = await _assets.CreateAsync(rejectedAsset);
            rejectedAsset.Id = rejectedId;
            await _mediaExtended.SetVirusScanResultAsync(rejectedId, false);
            rejectedAsset.IsVirusScanPassed = false;
            await AuditAsync(uploadedById, "VirusDetected", new { mediaAssetId = rejectedId, fileName = safeFileName, threat = scan.ThreatName });

            return MediaUploadOutcome.Fail(MediaUploadFailure.Infected,
                "File rejected: virus scan detected a threat. The file has not been stored.");
        }

        // 8. Create MediaAsset row via stored procedure (EXEC usp_MediaAsset_Create)
        var asset = new MediaAsset
        {
            FileName       = safeFileName,
            StoragePath    = savedPath,
            StorageBackend = _storage.BackendName,
            MimeType       = mimeType,
            FileSizeBytes  = replacementBytes?.LongLength ?? file.Length,
            Width          = width,
            Height         = height,
            UploadedById   = uploadedById,
        };

        var id = await _assets.CreateAsync(asset);
        asset.Id = id;

        // 9. Record the scan verdict: passed, or NULL when the engine was unavailable and
        //    the deployment chose to fail open (Development).
        if (scan.IsClean)
        {
            await _mediaExtended.SetVirusScanResultAsync(id, true);
            asset.IsVirusScanPassed = true;
        }
        else
        {
            await AuditAsync(uploadedById, "VirusScanSkipped", new { mediaAssetId = id, fileName = safeFileName, detail = scan.Detail });
        }

        // 10. Image processing: resize + WebP conversion (BRD FR-MEDIA-02)
        //     Non-image files skip this step silently; features.webpVariants turns it off entirely.
        if (_settings.GetBool(SiteSettingKeys.FeatureWebpVariants) && _imaging.ShouldProcess(mimeType))
        {
            await ProcessImageAsync(file, asset, guid, datePath, ct);
        }

        return MediaUploadOutcome.Success(asset);
    }

    private async Task AuditAsync(long actorId, string action, object detail)
    {
        if (_audit is null) return;
        try
        {
            await _audit.WriteAsync(actorId == 0 ? null : actorId, "MediaAsset", 0, action,
                System.Text.Json.JsonSerializer.Serialize(detail));
        }
        catch
        {
            // Auditing must never turn a rejected upload into a 500.
        }
    }

    // ── Image processing ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the upload into memory, generates a WebP variant, saves it alongside
    /// the original, and updates the MediaAsset row with the WebP path.
    ///
    /// Failures are non-fatal: the original upload already succeeded.
    /// WebP path stays NULL if processing fails.
    /// </summary>
    private async Task ProcessImageAsync(
        IFormFile file,
        MediaAsset asset,
        string guid,
        string datePath,
        CancellationToken ct)
    {
        try
        {
            // Read the upload into a byte array (we need it twice: once for ImageSharp, done)
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                imageBytes = ms.ToArray();
            }

            // Generate WebP (also applies resize if width > 1920px)
            var webPBytes = await _imaging.GenerateWebPAsync(imageBytes, ct);

            // Store the WebP variant: same directory as original, .webp extension
            var webPPath = $"{datePath}/{guid}.webp";
            await _storage.SaveBytesAsync(webPBytes, webPPath, ct);

            // Record WebP path in DB via SP
            await _assets.UpdateWebPPathAsync(asset.Id, webPPath);
            asset.WebPStoragePath = webPPath;
        }
        catch
        {
            // Non-fatal: original upload succeeded, WebP generation failed.
            // WebPStoragePath remains NULL; serve layer falls back to original.
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Longest stored file name; the column allows 500 but 255 is what file systems and browsers handle.</summary>
    public const int MaxFileNameLength = 255;

    /// <summary>
    /// The client's file name is display-only, but it is echoed in Content-Disposition
    /// and the admin UI: drop any path, control characters and quotes, and cap the length
    /// while keeping the extension (#158).
    /// </summary>
    public static string SanitizeFileName(string? raw)
    {
        var name = (raw ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        name = new string(name.Where(c => !char.IsControl(c) && c != '"' && c != ';').ToArray());
        if (name.Length == 0 || name == "." || name == "..")
            return "upload";

        if (name.Length > MaxFileNameLength)
        {
            var ext  = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            name = stem[..Math.Max(1, MaxFileNameLength - ext.Length)] + ext;
        }
        return name;
    }

    private static string NormaliseMime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        // Strip charset params: "text/plain; charset=utf-8" → "text/plain"
        var semi = raw.IndexOf(';');
        var mime = semi >= 0 ? raw[..semi].Trim() : raw.Trim();
        // Normalise non-standard alias
        if (mime.Equals("image/jpg", StringComparison.OrdinalIgnoreCase))
            mime = "image/jpeg";
        return mime.ToLowerInvariant();
    }

    /// <summary>
    /// Read image pixel dimensions without external dependencies.
    /// Supports JPEG, PNG, GIF, WebP headers — returns (null, null) on any failure.
    /// </summary>
    private static (int? Width, int? Height) TryReadImageDimensions(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var header = new byte[24];
            var read = stream.Read(header, 0, header.Length);
            if (read < 8) return (null, null);

            // PNG: 8-byte magic + 4-byte IHDR length + "IHDR" + 4w + 4h
            if (IsPng(header))
            {
                if (read < 24) return (null, null);
                var w = ReadBigEndianInt32(header, 16);
                var h = ReadBigEndianInt32(header, 20);
                return (w, h);
            }

            // GIF: "GIF8" at 0, width/height at bytes 6-9 (little-endian)
            if (IsGif(header))
            {
                var w = header[6] | (header[7] << 8);
                var h = header[8] | (header[9] << 8);
                return (w, h);
            }

            // JPEG: scan for SOF0/SOF2 marker
            if (IsJpeg(header))
            {
                return ReadJpegDimensions(file);
            }

            // WebP: "RIFF" at 0, "WEBP" at 8, "VP8 " / "VP8L" / "VP8X" at 12
            if (IsWebP(header))
            {
                return ReadWebPDimensions(header);
            }

            return (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    private static bool IsPng(byte[] h) =>
        h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47;

    private static bool IsGif(byte[] h) =>
        h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8';

    private static bool IsJpeg(byte[] h) =>
        h[0] == 0xFF && h[1] == 0xD8;

    private static bool IsWebP(byte[] h) =>
        h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' &&
        h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P';

    private static int ReadBigEndianInt32(byte[] buf, int offset) =>
        (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];

    private static (int? Width, int? Height) ReadJpegDimensions(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new BinaryReader(stream);
            // Skip SOI
            reader.ReadBytes(2);
            while (stream.Position < stream.Length)
            {
                if (reader.ReadByte() != 0xFF) break;
                var marker = reader.ReadByte();
                // SOF0, SOF1, SOF2
                if (marker is 0xC0 or 0xC1 or 0xC2)
                {
                    reader.ReadBytes(3); // length(2) + precision(1)
                    var h = (reader.ReadByte() << 8) | reader.ReadByte();
                    var w = (reader.ReadByte() << 8) | reader.ReadByte();
                    return (w, h);
                }
                // Skip this segment
                var segLen = (reader.ReadByte() << 8) | reader.ReadByte();
                if (segLen < 2) break;
                reader.ReadBytes(segLen - 2);
            }
        }
        catch { /* best-effort */ }
        return (null, null);
    }

    private static (int? Width, int? Height) ReadWebPDimensions(byte[] header)
    {
        try
        {
            // VP8 lossy: chunk "VP8 " at offset 12, width/height at 26-29
            if (header[12] == 'V' && header[13] == 'P' && header[14] == '8' && header[15] == ' ')
            {
                // Not enough in 24-byte header for VP8 dims; return unknown
                return (null, null);
            }
            // VP8L lossless: width/height packed into 4 bytes at offset 21
            if (header[12] == 'V' && header[13] == 'P' && header[14] == '8' && header[15] == 'L')
            {
                return (null, null); // packed bits, skip for now
            }
        }
        catch { /* best-effort */ }
        return (null, null);
    }
}
