namespace VA.CMS.API.Auth;

/// <summary>
/// Shared constants and helpers for the refresh-token cookie.
/// httpOnly + Secure + SameSite=Strict per acceptance criteria.
///
/// CSRF (BRD FR-SECURITY-05): the access token lives only in SPA memory and is sent
/// as a bearer header, which a cross-site form cannot set. This cookie is the only
/// ambient credential; it is SameSite=Strict, scoped to /api/auth, and the endpoints
/// that read it (POST /api/auth/refresh, POST /api/auth/logout) only ever return a
/// short-lived token to the same-origin script that called them — a cross-site
/// request cannot read the response. No anti-forgery token is needed.
///
/// #164: Secure is on everywhere except Development. Staging/UAT over HTTPS gets a
/// Secure cookie; a non-Development host reachable only over HTTP will never receive
/// the cookie back, which is the intended failure.
/// </summary>
public static class AuthCookieHelper
{
    public const string RefreshTokenCookieName = "cms_rt";

    /// <summary>Code default; the live value is auth.refreshTokenHours (issue #146).</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    /// <summary>Secure unless the host runs in the Development environment (http://localhost).</summary>
    public static bool SecureFor(IHostEnvironment env) => !env.IsDevelopment();

    /// <param name="secure">Marks the cookie Secure; only Development may pass false (see <see cref="SecureFor"/>).</param>
    /// <param name="lifetime">Cookie Max-Age; should match the refresh token lifetime. Defaults to 8 h.</param>
    public static CookieOptions BuildCookieOptions(bool secure, TimeSpan? lifetime = null) => new()
    {
        HttpOnly  = true,
        Secure    = secure,
        SameSite  = SameSiteMode.Strict,
        Path      = "/api/auth",   // scoped so only auth endpoints receive the cookie
        MaxAge    = lifetime ?? DefaultLifetime,
        IsEssential = true,
    };

    public static CookieOptions BuildExpiryCookieOptions(bool secure) => new()
    {
        HttpOnly  = true,
        Secure    = secure,
        SameSite  = SameSiteMode.Strict,
        Path      = "/api/auth",
        MaxAge    = TimeSpan.Zero,
        Expires   = DateTimeOffset.UnixEpoch,
        IsEssential = true,
    };
}
