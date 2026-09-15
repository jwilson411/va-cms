namespace VA.CMS.API.Auth;

/// <summary>
/// Shared constants and helpers for the refresh-token cookie.
/// httpOnly + Secure + SameSite=Strict per acceptance criteria.
/// </summary>
public static class AuthCookieHelper
{
    public const string RefreshTokenCookieName = "cms_rt";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public static CookieOptions BuildCookieOptions(bool isProduction) => new()
    {
        HttpOnly  = true,
        Secure    = isProduction,  // in dev we may run over HTTP
        SameSite  = SameSiteMode.Strict,
        Path      = "/api/auth",   // scoped so only auth endpoints receive the cookie
        MaxAge    = Lifetime,
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
