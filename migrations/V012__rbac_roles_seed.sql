-- V012__rbac_roles_seed.sql
-- Story #23: Implement RBAC — canonicalize the six required roles.
-- Safe to run multiple times (IF NOT EXISTS guards).
--
-- Required roles per epic #2 / story #23 AC:
--   ContentOwner | Editor | SiteAdmin | Developer | SystemAdmin | ReadOnly

SET NOCOUNT ON;

-- Remove legacy roles that are not in the canonical six.
-- Guard: only delete roles that have no UserRole assignments (won't break existing data).
DELETE FROM [dbo].[Role]
WHERE [Name] IN ('SuperAdmin', 'Admin', 'Reviewer')
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[UserRole] ur WHERE ur.[RoleId] = [dbo].[Role].[Id]
  );
GO

-- Upsert the six canonical system roles.
-- MERGE so this migration is idempotent on re-runs.
MERGE [dbo].[Role] AS target
USING (
    VALUES
        ('ContentOwner', 'Content Owner',     1),
        ('Editor',       'Editor',            1),
        ('SiteAdmin',    'Site Administrator',1),
        ('Developer',    'Developer',         1),
        ('SystemAdmin',  'System Administrator', 1),
        ('ReadOnly',     'Read Only',         1)
) AS src([Name], [DisplayName], [IsSystemRole])
ON target.[Name] = src.[Name]
WHEN MATCHED THEN
    UPDATE SET [DisplayName]  = src.[DisplayName],
               [IsSystemRole] = src.[IsSystemRole]
WHEN NOT MATCHED THEN
    INSERT ([Name], [DisplayName], [IsSystemRole])
    VALUES (src.[Name], src.[DisplayName], src.[IsSystemRole]);
GO

-- usp_Role_List: list all roles (used by admin UI and tests)
CREATE OR ALTER PROCEDURE [dbo].[usp_Role_List]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Name], [DisplayName], [IsSystemRole]
    FROM   [dbo].[Role]
    ORDER  BY [Name];
END;
GO

-- usp_ContentSection_GetForSlug: given a slug, return the deepest ContentSection
-- whose SlugPrefix matches the start of that slug. Used for section-scope checks.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentSection_GetForSlug]
    @Slug NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    -- Return the section whose SlugPrefix is the longest prefix of @Slug.
    SELECT TOP 1
        [Id], [Name], [SlugPrefix], [ParentSectionId]
    FROM   [dbo].[ContentSection]
    WHERE  @Slug LIKE [SlugPrefix] + '%'
    ORDER  BY LEN([SlugPrefix]) DESC;
END;
GO
