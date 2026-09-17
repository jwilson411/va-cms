namespace VA.CMS.API.Auth;

/// <summary>
/// Scheme names used by the Azure AD (OIDC) login flow.
///
/// The OIDC handler owns <see cref="CallbackPath"/> (the redirect URI registered
/// in the app registration) and signs the AAD identity into the
/// <see cref="Cookie"/> scheme. <c>GET /api/auth/callback</c> then reads that
/// cookie, issues the CMS refresh cookie and redirects into the SPA (#154).
/// </summary>
public static class AzureAdSchemes
{
    public const string OpenIdConnect = "AzureAd";
    public const string Cookie        = "AzureAdCookies";

    /// <summary>Name of the cookie that carries the AAD session between /signin-oidc and /api/auth/callback.</summary>
    public const string CookieName = "cms_aad";

    /// <summary>Default OIDC redirect URI path (Microsoft.Identity.Web's default).</summary>
    public const string CallbackPath = "/signin-oidc";
}
