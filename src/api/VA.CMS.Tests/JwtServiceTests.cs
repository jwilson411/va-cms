using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Tests;

/// <summary>
/// Unit tests for JwtService — token issuance and validation.
/// No database required.
/// </summary>
public class JwtServiceTests
{
    private static JwtService BuildService(string? signingKey = null) =>
        new JwtService(new JwtOptions
        {
            SigningKey = signingKey ?? "test-signing-key-32-chars-minimum!",
            Issuer    = "test-issuer",
            Audience  = "test-audience",
        });

    private static User SampleUser(bool isActive = true) => new User
    {
        Id          = 42,
        ExternalId  = "aad-oid-abc123",
        Email       = "alice@va.gov",
        DisplayName = "Alice Smith",
        IsActive    = isActive,
    };

    private static List<UserRoleAssignment> SampleRoles() =>
    [
        new UserRoleAssignment { RoleId = 1, RoleName = "Editor",  SectionId = null },
        new UserRoleAssignment { RoleId = 2, RoleName = "ContentOwner", SectionId = 7, SectionSlugPrefix = "hr/" },
    ];

    // ── IssueAccessToken ────────────────────────────────────────────────

    [Fact]
    public void IssueAccessToken_Returns_Valid_Jwt_String()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), SampleRoles());

        Assert.NotEmpty(token);
        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(token), "token must be parseable");
    }

    [Fact]
    public void IssueAccessToken_Has_Correct_Claims()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), SampleRoles());

        var principal = svc.ValidateToken(token);
        Assert.NotNull(principal);

        // JwtSecurityTokenHandler maps "sub" → ClaimTypes.NameIdentifier
        var sub = principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        Assert.Equal("aad-oid-abc123", sub);
        Assert.Equal("alice@va.gov",   principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                                    ?? principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("42",             principal.FindFirst("cms_user_id")?.Value);
    }

    [Fact]
    public void IssueAccessToken_Includes_Global_Role_Claim()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), SampleRoles());

        var principal = svc.ValidateToken(token);
        Assert.NotNull(principal);

        var roles = principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains("Editor", roles);
    }

    [Fact]
    public void IssueAccessToken_Includes_Section_Scoped_Role_Claim()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), SampleRoles());

        var principal = svc.ValidateToken(token);
        Assert.NotNull(principal);

        var roles = principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        // New claim format includes slug prefix: "ContentOwner:section:7:prefix:hr/"
        Assert.Contains(roles, r => r.StartsWith("ContentOwner:section:7:prefix:"));
    }

    [Fact]
    public void IssueAccessToken_Expires_In_15_Minutes()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), []);

        var handler  = new JwtSecurityTokenHandler();
        var parsed   = handler.ReadJwtToken(token);
        var lifetime = parsed.ValidTo - parsed.ValidFrom;

        Assert.InRange(lifetime.TotalSeconds, 895, 905); // ~15 min ± 5s
    }

    [Fact]
    public void IssueAccessToken_Uses_HS256()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), []);

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, parsed.Header.Alg);
    }

    // ── ValidateToken ───────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_Returns_Principal_For_Valid_Token()
    {
        var svc     = BuildService();
        var token   = svc.IssueAccessToken(SampleUser(), []);
        var result  = svc.ValidateToken(token);

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateToken_Returns_Null_For_Tampered_Token()
    {
        var svc   = BuildService();
        var token = svc.IssueAccessToken(SampleUser(), []);
        var bad   = token[..^5] + "AAAAA"; // corrupt the signature

        Assert.Null(svc.ValidateToken(bad));
    }

    [Fact]
    public void ValidateToken_Returns_Null_For_Wrong_Key()
    {
        var svc1  = BuildService("signing-key-number-one-32-chars-!");
        var svc2  = BuildService("signing-key-number-two-32-chars-!");
        var token = svc1.IssueAccessToken(SampleUser(), []);

        Assert.Null(svc2.ValidateToken(token));
    }
}
