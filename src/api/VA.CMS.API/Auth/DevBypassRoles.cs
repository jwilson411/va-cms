using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.API.Auth;

/// <summary>
/// Role set granted to DevBypass users.
///
/// Shared by DevBypassMiddleware, DevBypassController and AuthController.Refresh
/// so a dev user holds the same roles regardless of which path issued the JWT.
/// SystemAdmin satisfies every CMS policy (read/write/publish/manage/admin) and
/// Developer covers the diagnostic endpoints — together they unlock the whole
/// admin SPA for local development without seeding UserRole rows.
/// </summary>
public static class DevBypassRoles
{
    /// <summary>ExternalId prefix used when upserting DevBypass users.</summary>
    public const string ExternalIdPrefix = "devbypass:";

    public static UserRoleAssignment[] Build() =>
    [
        new UserRoleAssignment
        {
            RoleId            = 0,
            RoleName          = CmsRoles.SystemAdmin,
            SectionId         = null,
            SectionSlugPrefix = null,
        },
        new UserRoleAssignment
        {
            RoleId            = 0,
            RoleName          = CmsRoles.Developer,
            SectionId         = null,
            SectionSlugPrefix = null,
        },
    ];
}
