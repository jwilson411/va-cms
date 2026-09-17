using Microsoft.AspNetCore.Authorization;

namespace VA.CMS.API.Auth;

/// <summary>
/// Registers the CMS authorization policies and the role handler. Shared by
/// Program.cs and by tests that build a Hot Chocolate executor outside the host.
/// </summary>
public static class CmsAuthorizationExtensions
{
    public static IServiceCollection AddCmsAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, CmsRoleHandler>();

        services.AddAuthorization(options =>
        {
            var allRoles = new[]
            {
                CmsRoles.ContentOwner,
                CmsRoles.Editor,
                CmsRoles.SiteAdmin,
                CmsRoles.Developer,
                CmsRoles.SystemAdmin,
                CmsRoles.ReadOnly,
            };

            // Default deny (#155): a principal with no CMS role gets 403 everywhere. The
            // default policy covers bare [Authorize]; the fallback covers endpoints with no
            // attribute at all. Anonymous endpoints opt out with [AllowAnonymous]; login
            // endpoints that must accept a role-less external identity use AuthenticatedOnly.
            var anyRole = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CmsRoleRequirement(allRoles))
                .Build();
            options.DefaultPolicy  = anyRole;
            options.FallbackPolicy = anyRole;

            options.AddPolicy(CmsRoles.Policies.AuthenticatedOnly, p =>
                p.RequireAuthenticatedUser());

            options.AddPolicy(CmsRoles.Policies.AnyRole, anyRole);

            options.AddPolicy(CmsRoles.Policies.CanRead, p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new CmsRoleRequirement(allRoles)));

            options.AddPolicy(CmsRoles.Policies.CanWrite, p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new CmsRoleRequirement(
                     CmsRoles.ContentOwner,
                     CmsRoles.Editor,
                     CmsRoles.SiteAdmin,
                     CmsRoles.SystemAdmin)));

            options.AddPolicy(CmsRoles.Policies.CanPublish, p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new CmsRoleRequirement(
                     CmsRoles.Editor,
                     CmsRoles.SiteAdmin,
                     CmsRoles.SystemAdmin)));

            options.AddPolicy(CmsRoles.Policies.CanManageSite, p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new CmsRoleRequirement(
                     CmsRoles.SiteAdmin,
                     CmsRoles.SystemAdmin)));

            options.AddPolicy(CmsRoles.Policies.CanDevelop, p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new CmsRoleRequirement(
                     CmsRoles.Developer,
                     CmsRoles.SystemAdmin)));

            options.AddPolicy(CmsRoles.Policies.CanAdminSystem, p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new CmsRoleRequirement(
                     CmsRoles.SystemAdmin)));
        });
        return services;
    }
}
