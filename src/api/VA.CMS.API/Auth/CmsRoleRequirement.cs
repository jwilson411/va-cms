using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace VA.CMS.API.Auth;

/// <summary>
/// Authorization requirement that accepts both global and section-scoped role claims.
///
/// JwtService emits two claim formats:
///   Global:  "Editor"                              → matches RequireCmsRole("Editor")
///   Scoped:  "ContentOwner:section:7:prefix:hr/"  → matches RequireCmsRole("ContentOwner")
///
/// ASP.NET's built-in RequireRole("ContentOwner") only does an exact match, so it rejects
/// scoped claims. This requirement handles both.
/// </summary>
public sealed class CmsRoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedRoles { get; }

    public CmsRoleRequirement(params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}

/// <inheritdoc cref="CmsRoleRequirement"/>
public sealed class CmsRoleHandler : AuthorizationHandler<CmsRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CmsRoleRequirement requirement)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Task.CompletedTask;

        var roleClaims = context.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        foreach (var allowed in requirement.AllowedRoles)
        {
            foreach (var claim in roleClaims)
            {
                // Exact match (global role): "Editor" == "Editor"
                if (string.Equals(claim, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }

                // Prefix match (scoped role): "ContentOwner:section:7:prefix:hr/" starts with "ContentOwner:"
                if (claim.StartsWith(allowed + ":", StringComparison.OrdinalIgnoreCase))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
        }

        // Requirement not met — ASP.NET will return 403 automatically.
        return Task.CompletedTask;
    }
}
