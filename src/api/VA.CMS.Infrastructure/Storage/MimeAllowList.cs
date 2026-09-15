using Microsoft.AspNetCore.Http;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// MIME type allow-list for file uploads.
/// BRD FR-SECURITY-06: uploads validated against an allow-list of permitted MIME types.
/// </summary>
public static class MimeAllowList
{
    /// <summary>
    /// Permitted MIME types for upload. Grouped by category.
    /// Extension validation is layered on top at the service level.
    /// </summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/svg+xml",
        "image/tiff",
        "image/bmp",

        // Documents
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",

        // Text
        "text/plain",
        "text/csv",

        // Archives (limited; virus scan hook is separate)
        "application/zip",
        "application/x-zip-compressed",
    };

    /// <summary>
    /// Returns true if the MIME type is on the allow-list.
    /// </summary>
    public static bool IsAllowed(string mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) && AllowedMimeTypes.Contains(mimeType);

    /// <summary>
    /// Returns all permitted MIME types (for error messages and API docs).
    /// </summary>
    public static IReadOnlyCollection<string> Allowed => AllowedMimeTypes;
}
