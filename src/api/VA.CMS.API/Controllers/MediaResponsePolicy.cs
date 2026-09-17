namespace VA.CMS.API.Controllers;

/// <summary>
/// How a stored MIME type is delivered by GET /api/v1/media/serve (#158).
/// </summary>
/// <param name="Inline">Render in the browser (Content-Disposition: inline) or force a download.</param>
/// <param name="Csp">Content-Security-Policy to attach, or null.</param>
public sealed record MediaResponsePolicy(bool Inline, string? Csp)
{
    public const string SandboxCsp    = "sandbox";
    public const string SvgCsp        = "sandbox; default-src 'none'";

    public static MediaResponsePolicy For(string mimeType)
    {
        if (string.Equals(mimeType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            return new(Inline: true, Csp: SvgCsp);

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return new(Inline: true, Csp: SandboxCsp);

        // Chromium and Firefox will not render a PDF whose response carries CSP sandbox —
        // they fall back to a download — so PDF is inline without it. PDF scripting runs
        // inside the viewer, never in the API origin's DOM; nosniff still applies.
        if (string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return new(Inline: true, Csp: null);

        return new(Inline: false, Csp: SandboxCsp);
    }
}
