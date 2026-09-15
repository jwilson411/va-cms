using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.API.Auth;

/// <summary>
/// Issues HS256 JWT access tokens (15-minute lifetime) from CMS user/role data.
/// Does NOT depend on Azure AD — the AD side is handled by Microsoft.Identity.Web
/// in AuthController. This service only handles JWT minting and validation.
/// </summary>
public interface IJwtService
{
    /// <summary>Issues a signed JWT for the given user and roles.</summary>
    string IssueAccessToken(User user, IEnumerable<UserRoleAssignment> roles);

    /// <summary>Validates a JWT and returns the principal, or null if invalid.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>Returns the token validation parameters used for middleware and manual validation.</summary>
    TokenValidationParameters GetValidationParameters();
}

public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtService(JwtOptions options)
    {
        _options = options;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
    }

    public string IssueAccessToken(User user, IEnumerable<UserRoleAssignment> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.ExternalId),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("cms_user_id",                 user.Id.ToString()),
            new Claim("display_name",                user.DisplayName),
        };

        foreach (var r in roles)
        {
            // Emit role claims; include section scope when present.
            // Format for scoped roles: "{RoleName}:section:{SectionId}:prefix:{SlugPrefix}"
            // The slug prefix is needed by RbacService to enforce section-scoped ContentOwner access.
            string roleClaim;
            if (r.SectionId.HasValue)
            {
                var prefix = r.SectionSlugPrefix ?? string.Empty;
                roleClaim = $"{r.RoleName}:section:{r.SectionId}:prefix:{prefix}";
            }
            else
            {
                roleClaim = r.RoleName;
            }
            claims.Add(new Claim(ClaimTypes.Role, roleClaim));
        }

        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:   _options.Issuer,
            audience: _options.Audience,
            claims:   claims,
            notBefore: DateTime.UtcNow,
            expires:   DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(token, GetValidationParameters(), out _);
        }
        catch
        {
            return null;
        }
    }

    public TokenValidationParameters GetValidationParameters() => new()
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = _options.Issuer,
        ValidAudience            = _options.Audience,
        IssuerSigningKey         = _signingKey,
        ClockSkew                = TimeSpan.FromSeconds(30),
    };
}
