using System.Security.Claims;

namespace VA.CMS.API.Auth;

/// <summary>
/// Section-scoped RBAC enforcement (story #23 AC3):
/// A ContentOwner whose UserRoleAssignment has a SectionId can only act on content
/// whose slug falls under that section's SlugPrefix.
///
/// JWT claim format for scoped roles (set by JwtService):
///   "ContentOwner:section:7"  — role ContentOwner, scoped to section id 7
///   "Editor"                  — global Editor (no section scope)
///
/// Global roles (Editor, SiteAdmin, Developer, SystemAdmin, ReadOnly) are never
/// section-scoped; they apply to all content regardless of slug.
/// </summary>
public interface IRbacService
{
    /// <summary>Returns true if the principal has at least one of the specified roles globally.</summary>
    bool HasGlobalRole(ClaimsPrincipal user, params string[] roles);

    /// <summary>
    /// Returns true if the principal may act on content at the given slug.
    /// For global roles: always true.
    /// For ContentOwner with a section scope: true iff the section's slug prefix
    /// matches the start of <paramref name="contentSlug"/>.
    /// </summary>
    bool IsAuthorizedForSlug(ClaimsPrincipal user, string contentSlug, params string[] requiredRoles);

    /// <summary>Returns the CMS user id from the JWT claim, or null.</summary>
    long? GetUserId(ClaimsPrincipal user);
}

/// <inheritdoc />
public sealed class RbacService : IRbacService
{
    public bool HasGlobalRole(ClaimsPrincipal user, params string[] roles)
    {
        // A global role claim has no ":section:" suffix.
        var roleClaims = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return roles.Any(r => roleClaims.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    public bool IsAuthorizedForSlug(ClaimsPrincipal user, string contentSlug, params string[] requiredRoles)
    {
        var roleClaims = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        foreach (var requiredRole in requiredRoles)
        {
            foreach (var claim in roleClaims)
            {
                // Global role match (no section scope): always permitted.
                if (string.Equals(claim, requiredRole, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Scoped claim format: "RoleName:section:{sectionSlugPrefix}"
                // JwtService encodes: $"{r.RoleName}:section:{r.SectionId}"
                // but we need to check slug prefix for ContentOwner enforcement.
                // The claim stores SectionId; the slug-prefix check is done here
                // using the SectionSlugPrefix embedded at token issuance.
                if (claim.StartsWith($"{requiredRole}:section:", StringComparison.OrdinalIgnoreCase))
                {
                    // The slug prefix is embedded after "section:" in the claim value.
                    // Format: "{RoleName}:section:{SectionId}:prefix:{SlugPrefix}"
                    var prefixMarker = ":prefix:";
                    var prefixIdx = claim.IndexOf(prefixMarker, StringComparison.OrdinalIgnoreCase);
                    if (prefixIdx >= 0)
                    {
                        var slugPrefix = claim[(prefixIdx + prefixMarker.Length)..];
                        if (!string.IsNullOrEmpty(slugPrefix) &&
                            contentSlug.StartsWith(slugPrefix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    else
                    {
                        // Legacy/simple format: "RoleName:section:{SectionId}" — no slug prefix available.
                        // Cannot validate section scope without the prefix; deny for safety.
                        // (This path is hit in tests that stub roles without prefix data.)
                    }
                }
            }
        }

        return false;
    }

    public long? GetUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("cms_user_id");
        return long.TryParse(claim, out var id) ? id : null;
    }
}
