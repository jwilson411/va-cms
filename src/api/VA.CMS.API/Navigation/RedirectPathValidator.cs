namespace VA.CMS.API.Navigation;

/// <summary>
/// Validates redirect rules before they are stored (#155).
///
/// A redirect is served from a *.va.gov origin, so an unconstrained ToPath is an
/// open redirect and therefore a phishing primitive. ToPath must be site-relative
/// or an absolute https URL whose host is in the redirects.allowedExternalHosts
/// site setting. FromPath must be site-relative so a rule can only ever shadow a
/// path on this site.
/// </summary>
public static class RedirectPathValidator
{
    /// <summary>Returns an error message, or null when <paramref name="fromPath"/> is acceptable.</summary>
    public static string? ValidateFromPath(string fromPath)
    {
        var path = fromPath.Trim();
        if (!IsSiteRelative(path))
            return "FromPath must be a site-relative path starting with a single '/'.";
        if (path.Contains('?') || path.Contains('#'))
            return "FromPath must not contain a query string or fragment.";
        return null;
    }

    /// <summary>Returns an error message, or null when <paramref name="toPath"/> is acceptable.</summary>
    public static string? ValidateToPath(string toPath, IEnumerable<string> allowedExternalHosts)
    {
        var target = toPath.Trim();
        if (IsSiteRelative(target))
            return null;

        const string shape = "ToPath must be a site-relative path starting with '/' or an absolute https URL.";

        // Uri.TryCreate accepts "javascript:…" and, on Unix, "//host" as absolute; only http(s) count here.
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return shape;

        if (uri.Scheme == Uri.UriSchemeHttp)
            return "External redirect targets must use https.";

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "External redirect targets must not embed credentials.";

        if (!HostAllowed(uri.Host, allowedExternalHosts))
            return $"Host '{uri.Host}' is not in the redirects.allowedExternalHosts site setting.";

        return null;
    }

    /// <summary>
    /// Where the public site serves a content type (src/public/app/**/[...slug]) — the same
    /// mapping usp_ContentEntry_UpdateSlug uses to store a slug-change redirect (#169).
    /// </summary>
    public static string PublicPathPrefix(string? contentTypeName)
        => string.Equals(contentTypeName, "news_article", StringComparison.OrdinalIgnoreCase) ? "/news/" : "/pages/";

    /// <summary>
    /// Slugs a site-relative FromPath could shadow. The public site serves standard
    /// pages at /pages/{slug}, so "/pages/a/b" competes with the slug "a/b" as well
    /// as with a literal "pages/a/b".
    /// </summary>
    public static IReadOnlyList<string> SlugCandidates(string fromPath)
    {
        var trimmed = fromPath.Trim().Trim('/');
        if (trimmed.Length == 0)
            return Array.Empty<string>();

        var candidates = new List<string> { trimmed };
        if (trimmed.StartsWith("pages/", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 6)
            candidates.Add(trimmed["pages/".Length..]);
        return candidates;
    }

    /// <summary>Starts with exactly one '/' — "//host" and "/\host" are scheme-relative in browsers.</summary>
    public static bool IsSiteRelative(string path)
        => path.Length > 0
        && path[0] == '/'
        && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'))
        && !path.Any(char.IsWhiteSpace)
        && !path.Any(char.IsControl);

    private static bool HostAllowed(string host, IEnumerable<string> allowed)
    {
        foreach (var raw in allowed)
        {
            var pattern = raw.Trim();
            if (pattern.Length == 0) continue;

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..]; // ".va.gov"
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
                    return true;
            }
            else if (string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
