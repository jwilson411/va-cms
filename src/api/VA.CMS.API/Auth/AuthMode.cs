namespace VA.CMS.API.Auth;

/// <summary>
/// Defines which authentication scheme the CMS API uses.
///
/// Configured via Auth:Mode in appsettings.json / environment variables.
///
///   AzureAd     — default; OIDC via Microsoft.Identity.Web
///   WindowsAuth — Negotiate (Kerberos/NTLM) for IIS intranet deployments
///   DevBypass   — development-only header bypass (never in Production)
/// </summary>
public enum AuthMode
{
    /// <summary>Azure AD OIDC. The default for cloud/internet deployments.</summary>
    AzureAd,

    /// <summary>
    /// Windows Integrated Authentication (Negotiate).
    /// Used when the server is on an AD-joined Windows host behind IIS with
    /// Windows Authentication enabled in IIS. The client browser supplies
    /// Kerberos/NTLM credentials automatically on the intranet.
    /// </summary>
    WindowsAuth,

    /// <summary>Development bypass: accepts X-Dev-User header as UPN. Never production.</summary>
    DevBypass,
}
