using System.ComponentModel.DataAnnotations;

namespace VA.CMS.API.Auth;

/// <summary>
/// Bound from the "Jwt" section of appsettings.
/// SigningKey must be at least 32 bytes (256-bit) for HS256 and must not be an example
/// placeholder — enforced at startup by <see cref="StartupValidation.ValidateSigningKey"/> (#173).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Secret key used to sign and validate tokens. Set via environment variable in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Token issuer claim (e.g. "va-cms-api").</summary>
    [Required, MinLength(1)]
    public string Issuer { get; set; } = "va-cms-api";

    /// <summary>Token audience claim (e.g. "va-cms-spa").</summary>
    [Required, MinLength(1)]
    public string Audience { get; set; } = "va-cms-spa";
}
