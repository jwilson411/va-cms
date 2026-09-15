-- V013__ad_group_role_mappings.sql
-- Story #67 — AD Group → CMS Role mapping table, stored procedures, and indexes.
-- All application DB access is via stored procedures. The app service account has
-- EXECUTE only; no direct DML from the application layer.

-- ── Table ─────────────────────────────────────────────────────────────────────

CREATE TABLE [AdGroupRoleMapping] (
    [Id]        BIGINT IDENTITY(1,1) NOT NULL,
    [AdGroup]   NVARCHAR(500)        NOT NULL,   -- AD group name, e.g. "VA-CMS-Editors"
    [RoleId]    BIGINT               NOT NULL REFERENCES [Role]([Id]),
    [CreatedById] BIGINT             NOT NULL REFERENCES [User]([Id]),
    [CreatedAt] DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AdGroupRoleMapping] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Unique: one (group, role) pair — multiple groups may map to the same role,
-- but the same group cannot map to the same role twice.
CREATE UNIQUE INDEX [UX_AdGroupRoleMapping_AdGroup_RoleId]
    ON [AdGroupRoleMapping] ([AdGroup], [RoleId]);
GO

-- Fast lookup by group name (used at every login to resolve claims → roles).
CREATE INDEX [IX_AdGroupRoleMapping_AdGroup]
    ON [AdGroupRoleMapping] ([AdGroup])
    INCLUDE ([RoleId]);
GO

-- ── Stored Procedures ─────────────────────────────────────────────────────────

-- List all mappings (for admin UI display)
CREATE OR ALTER PROCEDURE [dbo].[usp_AdGroupMapping_List]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT m.Id, m.AdGroup, m.RoleId, r.[Name] AS RoleName,
           m.CreatedById, m.CreatedAt, m.UpdatedAt
    FROM   [AdGroupRoleMapping] m
    JOIN   [Role] r ON r.Id = m.RoleId
    ORDER  BY m.AdGroup, r.[Name];
END;
GO

-- Upsert: if (AdGroup, RoleId) already exists, update UpdatedAt; otherwise insert.
-- Returns the Id via OUTPUT param.
CREATE OR ALTER PROCEDURE [dbo].[usp_AdGroupMapping_Upsert]
    @AdGroup    NVARCHAR(500),
    @RoleId     BIGINT,
    @CreatedById BIGINT,
    @NewId      BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Check for existing mapping
    SELECT @NewId = Id
    FROM   [AdGroupRoleMapping]
    WHERE  [AdGroup] = @AdGroup AND [RoleId] = @RoleId;

    IF @NewId IS NOT NULL
    BEGIN
        -- Already exists — touch UpdatedAt so the audit trail shows admin awareness
        UPDATE [AdGroupRoleMapping]
        SET    [UpdatedAt] = SYSUTCDATETIME()
        WHERE  [Id] = @NewId;
        RETURN;
    END;

    INSERT INTO [AdGroupRoleMapping] ([AdGroup], [RoleId], [CreatedById], [CreatedAt], [UpdatedAt])
    VALUES (@AdGroup, @RoleId, @CreatedById, SYSUTCDATETIME(), SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- Delete a single mapping by Id.
CREATE OR ALTER PROCEDURE [dbo].[usp_AdGroupMapping_Delete]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [AdGroupRoleMapping] WHERE [Id] = @Id;
END;
GO

-- Resolve: given a JSON array of AD group names, return all matching role names.
-- Called at login/refresh time to resolve claims → CMS roles.
-- Input: JSON array like N'["VA-CMS-Editors","VA-CMS-SiteAdmins"]'
-- Output: rows of (RoleName) for every matched mapping.
CREATE OR ALTER PROCEDURE [dbo].[usp_AdGroupMapping_ResolveRoles]
    @GroupsJson NVARCHAR(MAX)   -- JSON array of group name strings
AS
BEGIN
    SET NOCOUNT ON;

    -- Parse the JSON array and join against mappings.
    SELECT DISTINCT r.[Name] AS RoleName, m.RoleId
    FROM   [AdGroupRoleMapping] m
    JOIN   [Role] r ON r.Id = m.RoleId
    JOIN   OPENJSON(@GroupsJson) AS g ON g.[value] = m.[AdGroup];
END;
GO
