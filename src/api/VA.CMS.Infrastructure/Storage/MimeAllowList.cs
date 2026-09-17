using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// MIME type allow-list for file uploads.
/// BRD FR-SECURITY-06: uploads validated against an allow-list of permitted MIME types.
///
/// The list itself is the <c>media.allowedMimeTypes</c> site setting (issue #145); the code
/// default lives in <see cref="SiteSettingDefinitions.DefaultAllowedMimeTypes"/>. The static
/// overloads evaluate against that default for callers that have no settings service.
/// </summary>
public static class MimeAllowList
{
    private static readonly HashSet<string> DefaultAllowed =
        new(SiteSettingDefinitions.DefaultAllowedMimeTypes, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns true if the MIME type is on the default allow-list.</summary>
    public static bool IsAllowed(string mimeType) => IsAllowed(mimeType, DefaultAllowed);

    /// <summary>Returns true if the MIME type is in <paramref name="allowed"/> (case-insensitive).</summary>
    public static bool IsAllowed(string mimeType, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return false;
        return allowed is HashSet<string> set && set.Comparer == StringComparer.OrdinalIgnoreCase
            ? set.Contains(mimeType)
            : allowed.Contains(mimeType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The default permitted MIME types (for error messages and API docs).</summary>
    public static IReadOnlyCollection<string> Allowed => DefaultAllowed;
}
