-- V034__redirect_admin_sps.sql
-- Issue #48 — Build redirect management table in admin (BRD FR-NAV-06)
-- Adds stored procedures for admin-level redirect management:
--   usp_Redirect_List       — paginated list with From, To, StatusCode, CreatedBy
--   usp_Redirect_Update     — edit FromPath / ToPath / StatusCode on an existing redirect
--   usp_Redirect_Deactivate — soft-deactivate (sets IsActive = 0)

-- ── usp_Redirect_List ────────────────────────────────────────────────────────
-- Returns all redirects (active and inactive) for admin management UI.
-- Supports optional IsActive filter (NULL = all, 1 = active only, 0 = inactive only).
-- Returns CreatedByEmail / CreatedByDisplayName from the User table.
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_List]
    @IsActive  BIT  = NULL,
    @Page      INT  = 1,
    @PageSize  INT  = 50,
    @TotalRows INT  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalRows = COUNT(*)
    FROM   [Redirect] r
    WHERE  (@IsActive IS NULL OR r.[IsActive] = @IsActive);

    SELECT
        r.[Id],
        r.[FromPath],
        r.[ToPath],
        r.[StatusCode],
        r.[IsActive],
        r.[CreatedById],
        u.[Email]        AS CreatedByEmail,
        u.[DisplayName]  AS CreatedByDisplayName,
        r.[CreatedAt]
    FROM   [Redirect] r
    LEFT JOIN [User] u ON u.[Id] = r.[CreatedById]
    WHERE  (@IsActive IS NULL OR r.[IsActive] = @IsActive)
    ORDER  BY r.[CreatedAt] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- ── usp_Redirect_GetById ─────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        r.[Id],
        r.[FromPath],
        r.[ToPath],
        r.[StatusCode],
        r.[IsActive],
        r.[CreatedById],
        u.[Email]        AS CreatedByEmail,
        u.[DisplayName]  AS CreatedByDisplayName,
        r.[CreatedAt]
    FROM   [Redirect] r
    LEFT JOIN [User] u ON u.[Id] = r.[CreatedById]
    WHERE  r.[Id] = @Id;
END;
GO

-- ── usp_Redirect_Update ──────────────────────────────────────────────────────
-- Edits an existing redirect (any IsActive state).
-- Deactivates any OTHER active redirect that already claims the new FromPath
-- before updating, so there are never two active rows for the same path.
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Update]
    @Id         BIGINT,
    @FromPath   NVARCHAR(2000),
    @ToPath     NVARCHAR(2000),
    @StatusCode INT = 301
AS
BEGIN
    SET NOCOUNT ON;

    -- Deactivate any other active redirect for the (new) FromPath
    UPDATE [Redirect]
    SET    [IsActive] = 0
    WHERE  [FromPath] = @FromPath
      AND  [IsActive] = 1
      AND  [Id] <> @Id;

    UPDATE [Redirect]
    SET    [FromPath]   = @FromPath,
           [ToPath]     = @ToPath,
           [StatusCode] = @StatusCode
    WHERE  [Id] = @Id;
END;
GO

-- ── usp_Redirect_Deactivate ──────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Deactivate]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [Redirect]
    SET    [IsActive] = 0
    WHERE  [Id] = @Id;
END;
GO
