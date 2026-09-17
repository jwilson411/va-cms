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
public interface IMediaUploadService
{
    /// <summary>
    /// Validate, store, scan, and record a file upload.
    /// Returns the created MediaAsset on success.
    /// Returns an error message string on validation, storage, or scan failure.
    /// </summary>
    Task<(MediaAsset? Asset, string? Error)> UploadAsync(
        IFormFile file,
        long uploadedById,
        CancellationToken ct = default);
}

public class MediaUploadService : IMediaUploadService
{
    private readonly IStorageBackend _storage;
    private readonly IMediaAssetRepository _assets;
    private readonly IImageProcessingService _imaging;
    private readonly IVirusScanService _virusScan;
    private readonly IMediaExtendedRepository _mediaExtended;
    private readonly ISiteSettingsService _settings;

    public MediaUploadService(
        IStorageBackend storage,
        IMediaAssetRepository assets,
        IImageProcessingService imaging,
        IVirusScanService virusScan,
        IMediaExtendedRepository mediaExtended,
        ISiteSettingsService? settings = null)
    {
        _storage       = storage;
        _assets        = assets;
        _imaging       = imaging;
        _virusScan     = virusScan;
        _mediaExtended = mediaExtended;
        // Limits and the MIME allow-list are site settings (issue #145); code defaults when not supplied.
        _settings      = settings ?? StaticSiteSettings.Defaults;
    }

    public async Task<(MediaAsset? Asset, string? Error)> UploadAsync(
        IFormFile file,
        long uploadedById,
        CancellationToken ct = default)
    {
        // 1. Validate file is not empty and within the configured size limit (media.maxUploadBytes)
        if (file.Length == 0)
            return (null, "File must not be empty.");

        var maxBytes = _settings.GetLong(SiteSettingKeys.MediaMaxUploadBytes);
        if (maxBytes > 0 && file.Length > maxBytes)
            return (null, $"File is {file.Length:N0} bytes; the maximum upload size is {maxBytes:N0} bytes.");

        // 2. Resolve MIME — the declared ContentType is a claim, normalised ("image/jpg" → "image/jpeg")
        var mimeType = NormaliseMime(file.ContentType);

        // 3. MIME allow-list check (BRD FR-SECURITY-06) against media.allowedMimeTypes
        var allowed = _settings.GetStringList(SiteSettingKeys.MediaAllowedMimeTypes);
        if (!MimeAllowList.IsAllowed(mimeType, allowed))
            return (null, $"File type '{mimeType}' is not permitted. " +
                          $"Allowed types: {string.Join(", ", allowed)}.");

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
            return (null, $"File content does not match the declared type '{mimeType}' (content looks like {seen}).");
        }
        if (string.IsNullOrEmpty(ext) || !MediaContentSniffer.ExtensionMatches(mimeType, ext))
            return (null, $"File extension '.{ext}' does not match the declared type '{mimeType}'.");

        // 3b. SVG is only reachable when an administrator has added it to the allow-list;
        //     even then active content is stripped before the file is stored.
        byte[]? replacementBytes = null;
        if (mimeType == "image/svg+xml")
        {
            using var svgStream = file.OpenReadStream();
            var (sanitized, svgError) = SvgSanitizer.Sanitize(svgStream);
            if (sanitized is null)
                return (null, svgError);
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
            return (null, $"Storage error: {ex.Message}");
        }

        // 7. Virus scan (BRD FR-MEDIA-04 — Issue #45)
        //    Scan the saved file from the storage backend by re-opening the upload stream.
        //    If the scan fails (virus detected): record IsVirusScanPassed=0, delete from storage, reject.
        bool scanPassed;
        try
        {
            using var scanStream = file.OpenReadStream();
            scanPassed = await _virusScan.ScanAsync(scanStream, ct);
        }
        catch (Exception ex)
        {
            // Scan itself threw — treat as scan failure for safety
            await _storage.DeleteAsync(savedPath, ct);
            return (null, $"Virus scan error: {ex.Message}");
        }

        if (!scanPassed)
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

            return (null, "File rejected: virus scan detected a threat. The file has not been stored.");
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

        // 9. Record scan passed
        await _mediaExtended.SetVirusScanResultAsync(id, true);
        asset.IsVirusScanPassed = true;

        // 10. Image processing: resize + WebP conversion (BRD FR-MEDIA-02)
        //     Non-image files skip this step silently; features.webpVariants turns it off entirely.
        if (_settings.GetBool(SiteSettingKeys.FeatureWebpVariants) && _imaging.ShouldProcess(mimeType))
        {
            await ProcessImageAsync(file, asset, guid, datePath, ct);
        }

        return (asset, null);
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
