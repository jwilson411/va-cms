namespace VA.CMS.API.Auth;

/// <summary>
/// Shared constants and helpers for the refresh-token cookie.
/// httpOnly + Secure + SameSite=Strict per acceptance criteria.
/// </summary>
public static class AuthCookieHelper
{
    public const string RefreshTokenCookieName = "cms_rt";

    /// <summary>Code default; the live value is auth.refreshTokenHours (issue #146).</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    /// <param name="isProduction">Marks the cookie Secure; in Development it must still work over http://localhost.</param>
    /// <param name="lifetime">Cookie Max-Age; should match the refresh token lifetime. Defaults to 8 h.</param>
    public static CookieOptions BuildCookieOptions(bool isProduction, TimeSpan? lifetime = null) => new()
    {
        HttpOnly  = true,
        Secure    = isProduction,  // in dev we may run over HTTP
        SameSite  = SameSiteMode.Strict,
        Path      = "/api/auth",   // scoped so only auth endpoints receive the cookie
        MaxAge    = lifetime ?? DefaultLifetime,
        IsEssential = true,
    };

    public static CookieOptions BuildExpiryCookieOptions(bool isProduction) => new()
    {
        HttpOnly  = true,
        Secure    = isProduction,
        SameSite  = SameSiteMode.Strict,
        Path      = "/api/auth",
        MaxAge    = TimeSpan.Zero,
        Expires   = DateTimeOffset.UnixEpoch,
        IsEssential = true,
    };
}
