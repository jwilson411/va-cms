using Microsoft.AspNetCore.Http;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Media upload service.
/// Validates the upload, delegates storage to the configured IStorageBackend,
/// then creates a MediaAsset row via the repository.
/// BRD FR-MEDIA-01, FR-MEDIA-07, FR-SECURITY-06.
/// </summary>
public interface IMediaUploadService
{
    /// <summary>
    /// Validate, store, and record a file upload.
    /// Returns the created MediaAsset on success.
    /// Returns an error message string on validation or storage failure.
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

    public MediaUploadService(IStorageBackend storage, IMediaAssetRepository assets)
    {
        _storage = storage;
        _assets  = assets;
    }

    public async Task<(MediaAsset? Asset, string? Error)> UploadAsync(
        IFormFile file,
        long uploadedById,
        CancellationToken ct = default)
    {
        // 1. Validate file is not empty
        if (file.Length == 0)
            return (null, "File must not be empty.");

        // 2. Resolve MIME — prefer declared ContentType but normalise "image/jpg" → "image/jpeg"
        var mimeType = NormaliseMime(file.ContentType);

        // 3. MIME allow-list check (BRD FR-SECURITY-06)
        if (!MimeAllowList.IsAllowed(mimeType))
            return (null, $"File type '{mimeType}' is not permitted. " +
                          $"Allowed types: {string.Join(", ", MimeAllowList.Allowed)}.");

        // 4. Build a safe storage path: yyyy/MM/<guid>.<ext>
        //    Using a GUID prevents enumeration; the original filename is preserved in FileName column.
        var ext        = Path.GetExtension(file.FileName)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
        var safeExt    = string.IsNullOrWhiteSpace(ext) ? "bin" : ext;
        var datePath   = DateTime.UtcNow.ToString("yyyy/MM");
        var uniqueName = $"{Guid.NewGuid():N}.{safeExt}";
        var storagePath = $"{datePath}/{uniqueName}";

        // 5. Read image dimensions (only for image MIME types, best-effort)
        int? width  = null;
        int? height = null;

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            (width, height) = TryReadImageDimensions(file);
        }

        // 6. Save file to backend (BRD FR-SECURITY-06: stored outside web root)
        string savedPath;
        try
        {
            savedPath = await _storage.SaveAsync(file, storagePath, ct);
        }
        catch (Exception ex)
        {
            return (null, $"Storage error: {ex.Message}");
        }

        // 7. Create MediaAsset row via stored procedure (EXEC usp_MediaAsset_Create)
        var asset = new MediaAsset
        {
            FileName       = Path.GetFileName(file.FileName),
            StoragePath    = savedPath,
            StorageBackend = _storage.BackendName,
            MimeType       = mimeType,
            FileSizeBytes  = file.Length,
            Width          = width,
            Height         = height,
            UploadedById   = uploadedById,
        };

        var id = await _assets.CreateAsync(asset);
        asset.Id = id;

        return (asset, null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
