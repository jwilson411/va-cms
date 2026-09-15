namespace VA.CMS.API.Auth;

/// <summary>
/// Configuration section for CMS authentication settings.
/// Bound from appsettings.json "Auth" section.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Determines which auth scheme is active.
    /// Defaults to AzureAd if not specified.
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.AzureAd;

    /// <summary>
    /// Dev-bypass allowed UPNs. Only used when Mode == DevBypass.
    /// </summary>
    public string[] DevBypassAllowedUsers { get; set; } = [];
}
