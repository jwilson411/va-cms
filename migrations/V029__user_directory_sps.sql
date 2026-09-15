-- V029__user_directory_sps.sql
-- Issue #56: User directory and role assignment admin UI (BRD FR-USERS-03, FR-USERS-04).
-- Adds: usp_Role_List, usp_User_GetDetail, usp_ContentSection_List
-- All user management SPs (usp_User_List, usp_User_AssignRole, usp_User_RevokeRole,
-- usp_User_Deactivate, usp_User_GetRoles) already exist in V008.

-- ── usp_Role_List ──────────────────────────────────────────────────────────────
-- Returns all roles so the UI can populate role assignment dropdowns.
CREATE OR ALTER PROCEDURE [dbo].[usp_Role_List]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Name], [DisplayName], [IsSystemRole]
    FROM   [Role]
    ORDER  BY [Name];
END;
GO

-- ── usp_ContentSection_List ────────────────────────────────────────────────────
-- Returns all content sections so the UI can scope role assignments.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentSection_List]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Name], [SlugPrefix], [ParentSectionId]
    FROM   [ContentSection]
    ORDER  BY [Name];
END;
GO

-- ── usp_User_GetDetail ─────────────────────────────────────────────────────────
-- Returns a single user row plus their role assignments as a JSON column.
-- Used by GET /admin/users/{id} to populate the user detail panel.
CREATE OR ALTER PROCEDURE [dbo].[usp_User_GetDetail]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- User row
    SELECT u.Id, u.ExternalId, u.Email, u.DisplayName, u.IsActive,
           u.LastLoginAt, u.CreatedAt, u.UpdatedAt,
           (
               SELECT ur.RoleId,
                      r.[Name]          AS RoleName,
                      r.DisplayName     AS RoleDisplayName,
                      ur.SectionId,
                      s.Name            AS SectionName,
                      s.SlugPrefix      AS SectionSlugPrefix
               FROM   [UserRole] ur
               JOIN   [Role] r              ON r.Id  = ur.RoleId
               LEFT JOIN [ContentSection] s ON s.Id  = ur.SectionId
               WHERE  ur.UserId = u.Id
               FOR JSON PATH
           ) AS RolesJson
    FROM   [User] u
    WHERE  u.Id = @Id;
END;
GO
