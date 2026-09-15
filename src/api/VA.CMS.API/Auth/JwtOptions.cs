namespace VA.CMS.API.Auth;

/// <summary>
/// Bound from the "Jwt" section of appsettings.
/// SigningKey must be at least 32 characters (256-bit) for HS256.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Secret key used to sign and validate tokens. Set via environment variable in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Token issuer claim (e.g. "va-cms-api").</summary>
    public string Issuer { get; set; } = "va-cms-api";

    /// <summary>Token audience claim (e.g. "va-cms-spa").</summary>
    public string Audience { get; set; } = "va-cms-spa";
}
