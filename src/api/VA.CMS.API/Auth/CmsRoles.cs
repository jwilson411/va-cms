namespace VA.CMS.API.Auth;

/// <summary>
/// Canonical RBAC role names and authorization policy names for the VA CMS.
///
/// Six system roles (story #23, BRD FR-USERS-03 / FR-USERS-04):
///   ContentOwner — creates/edits content in their assigned section(s).
///   Editor       — reviews and approves content across all sections.
///   SiteAdmin    — manages navigation, sections, taxonomies, and redirects.
///   Developer    — access to monitoring, webhooks, and diagnostic endpoints.
///   SystemAdmin  — all permissions including user management.
///   ReadOnly     — read-only access to all published content.
///
/// Section-scoped assignments: a ContentOwner may have SectionId set in their
/// UserRoleAssignment, restricting them to that section's slug prefix.
/// Roles with SectionId = null are global (apply to all sections).
/// </summary>
public static class CmsRoles
{
    public const string ContentOwner = "ContentOwner";
    public const string Editor       = "Editor";
    public const string SiteAdmin    = "SiteAdmin";
    public const string Developer    = "Developer";
    public const string SystemAdmin  = "SystemAdmin";
    public const string ReadOnly     = "ReadOnly";

    /// <summary>
    /// Policy names — registered in Program.cs, referenced by [Authorize(Policy = …)].
    /// </summary>
    public static class Policies
    {
        /// <summary>Any authenticated user with at least one CMS role.</summary>
        public const string AnyRole = "cms:any";

        /// <summary>Can read content (all roles).</summary>
        public const string CanRead = "cms:read";

        /// <summary>Can create/edit content. ContentOwner section enforcement is
        /// applied by the service layer, not here.</summary>
        public const string CanWrite = "cms:write";

        /// <summary>Can approve / publish content.</summary>
        public const string CanPublish = "cms:publish";

        /// <summary>Can manage site structure (menus, sections, taxonomies).</summary>
        public const string CanManageSite = "cms:manage_site";

        /// <summary>Can view developer/diagnostic endpoints.</summary>
        public const string CanDevelop = "cms:develop";

        /// <summary>Full system admin access (users, roles, all config).</summary>
        public const string CanAdminSystem = "cms:admin_system";
    }
}
