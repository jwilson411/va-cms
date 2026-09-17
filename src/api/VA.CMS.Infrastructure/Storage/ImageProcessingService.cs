using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Processes image uploads: resizes to media.imageMaxWidthPx wide (default 1920, preserving aspect ratio)
/// and encodes a WebP variant.
///
/// Issue #41 — BRD FR-MEDIA-02.
///
/// AC:
///   - Uploaded images are resized to max 1920px wide (preserving aspect ratio).
///   - A WebP version is generated and stored alongside the original.
///   - JPEG/PNG originals are retained for download; WebP served to browsers.
///   - Non-image files skip the resize step.
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Returns true if this MIME type is an image that should be processed.
    /// WebP uploads are supported inputs (we still generate a fresh WebP from any image).
    /// GIF is excluded — animated GIFs would lose animation; callers serve originals.
    /// </summary>
    bool ShouldProcess(string mimeType);

    /// <summary>
    /// Processes an image byte array:
    ///   1. Decodes the image.
    ///   2. If width > 1920px, resizes down preserving aspect ratio.
    ///   3. Encodes the (possibly resized) image as WebP.
    /// Returns the WebP bytes.
    /// Throws if the input cannot be decoded as an image.
    /// </summary>
    Task<byte[]> GenerateWebPAsync(byte[] imageBytes, CancellationToken ct = default);
}

/// <inheritdoc />
public class ImageProcessingService : IImageProcessingService
{
    private readonly ISiteSettingsService _settings;

    /// <param name="settings">Source of media.imageMaxWidthPx / media.webpQuality (issue #145); code defaults when null.</param>
    public ImageProcessingService(ISiteSettingsService? settings = null)
        => _settings = settings ?? StaticSiteSettings.Defaults;

    /// <inheritdoc />
    public bool ShouldProcess(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return false;
        var lower = mimeType.ToLowerInvariant();
        // Process JPEG, PNG, WebP. Skip GIF (animated) and non-image MIME types.
        return lower is "image/jpeg" or "image/png" or "image/webp";
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateWebPAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        using var image = Image.Load(imageBytes);

        var maxWidthPx = Math.Max(1, _settings.GetInt(SiteSettingKeys.MediaImageMaxWidthPx));
        var quality    = Math.Clamp(_settings.GetInt(SiteSettingKeys.MediaWebpQuality), 1, 100);

        // Resize if the width exceeds the maximum. Height is computed automatically
        // by ImageSharp to preserve aspect ratio when only Width is supplied.
        if (image.Width > maxWidthPx)
        {
            image.Mutate(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidthPx, 0),   // 0 height = auto-calculate
                    Mode = ResizeMode.Max,
                }));
        }

        // Encode as lossy WebP at the configured quality (default 80 — good balance for web assets)
        var encoder = new WebpEncoder
        {
            Quality = quality,
            FileFormat = WebpFileFormatType.Lossy,
        };

        using var ms = new MemoryStream();
        await image.SaveAsync(ms, encoder, ct);
        return ms.ToArray();
    }
}
