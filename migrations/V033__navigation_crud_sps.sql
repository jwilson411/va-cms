-- V033__navigation_crud_sps.sql
-- Issue #46 — Navigation menu CRUD API and admin drag-and-drop editor
-- BRD FR-NAV-01, FR-NAV-03
--
-- Adds:
--   usp_Navigation_GetMenu              — get single menu by handle
--   usp_Navigation_ListMenus            — list all menus
--   usp_Navigation_CreateMenu           — create menu
--   usp_Navigation_UpdateMenu           — rename menu
--   usp_Navigation_DeleteMenu           — delete menu (cascades items)
--   usp_Navigation_DeleteItem           — delete single nav item
--   usp_Navigation_GetMenuTreeAdmin     — full tree including hidden items (for admin editor)
--   usp_Navigation_GetItem              — get single item by id
--   usp_Navigation_BulkReorder          — apply SortOrder/ParentItemId changes from drag-and-drop

-- ── Menu CRUD ─────────────────────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_GetMenu]
    @Handle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Handle, CreatedAt, UpdatedAt
    FROM [NavigationMenu]
    WHERE [Handle] = @Handle;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_ListMenus]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Handle, CreatedAt, UpdatedAt
    FROM [NavigationMenu]
    ORDER BY Name;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_CreateMenu]
    @Name   NVARCHAR(200),
    @Handle NVARCHAR(100),
    @NewId  BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [NavigationMenu] ([Name], [Handle], [CreatedAt], [UpdatedAt])
    VALUES (@Name, @Handle, SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_UpdateMenu]
    @Id     BIGINT,
    @Name   NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [NavigationMenu]
    SET [Name] = @Name, [UpdatedAt] = SYSUTCDATETIME()
    WHERE [Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_DeleteMenu]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    -- Cascade: delete all items in this menu first
    DELETE FROM [NavigationItem] WHERE [MenuId] = @Id;
    DELETE FROM [NavigationMenu] WHERE [Id] = @Id;
END;
GO

-- ── Item CRUD ─────────────────────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_DeleteItem]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    -- Recursively delete children first (simple iterative approach for up to 3 levels)
    -- Level 3 children of this item
    DELETE FROM [NavigationItem]
    WHERE [ParentItemId] IN (
        SELECT ni2.Id FROM [NavigationItem] ni2
        WHERE ni2.[ParentItemId] IN (
            SELECT ni3.Id FROM [NavigationItem] ni3
            WHERE ni3.[ParentItemId] = @Id
        )
    );
    -- Level 2 children of this item
    DELETE FROM [NavigationItem]
    WHERE [ParentItemId] IN (
        SELECT ni2.Id FROM [NavigationItem] ni2
        WHERE ni2.[ParentItemId] = @Id
    );
    -- Level 1 children of this item
    DELETE FROM [NavigationItem] WHERE [ParentItemId] = @Id;
    -- The item itself
    DELETE FROM [NavigationItem] WHERE [Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_GetItem]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, MenuId, ParentItemId, Label, Url, ContentEntryId, Target, SortOrder, IsVisible
    FROM [NavigationItem]
    WHERE [Id] = @Id;
END;
GO

-- ── Admin tree (includes hidden items) ────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_GetMenuTreeAdmin]
    @Handle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    -- Same CTE as usp_Navigation_GetMenuTree but without the IsVisible=1 filter,
    -- so admin can see and toggle hidden items.
    WITH MenuTree AS (
        SELECT ni.Id, ni.MenuId, ni.ParentItemId, ni.Label, ni.Url, ni.Target,
               ni.ContentEntryId, ni.SortOrder, ni.IsVisible, 0 AS Depth
        FROM   [NavigationItem] ni
        JOIN   [NavigationMenu] nm ON nm.Id = ni.MenuId
        WHERE  nm.[Handle] = @Handle
          AND  ni.ParentItemId IS NULL

        UNION ALL

        SELECT ni.Id, ni.MenuId, ni.ParentItemId, ni.Label, ni.Url, ni.Target,
               ni.ContentEntryId, ni.SortOrder, ni.IsVisible, mt.Depth + 1
        FROM   [NavigationItem] ni
        JOIN   MenuTree mt ON mt.Id = ni.ParentItemId
        WHERE  mt.Depth < 3  -- max 3 levels deep
    )
    SELECT * FROM MenuTree
    ORDER BY ParentItemId, SortOrder;
END;
GO

-- ── Bulk reorder (drag-and-drop save) ─────────────────────────────────────────
-- Accepts a JSON array: [{"id": 1, "parentItemId": null, "sortOrder": 0}, ...]
-- Applies each update; depth enforcement is validated in the API layer.

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_BulkReorder]
    @MenuId      BIGINT,
    @ItemsJson   NVARCHAR(MAX)   -- JSON: [{id, parentItemId, sortOrder}]
AS
BEGIN
    SET NOCOUNT ON;

    -- Parse the JSON payload into a temp table
    CREATE TABLE #ReorderItems (
        Id           BIGINT,
        ParentItemId BIGINT NULL,
        SortOrder    INT
    );

    INSERT INTO #ReorderItems (Id, ParentItemId, SortOrder)
    SELECT
        CAST(j.[Id]           AS BIGINT),
        CAST(j.[ParentItemId] AS BIGINT),
        CAST(j.[SortOrder]    AS INT)
    FROM OPENJSON(@ItemsJson)
    WITH (
        Id           BIGINT       '$.id',
        ParentItemId BIGINT       '$.parentItemId',
        SortOrder    INT          '$.sortOrder'
    ) j;

    -- Apply updates — only items belonging to the specified menu
    UPDATE ni
    SET ni.[ParentItemId] = r.ParentItemId,
        ni.[SortOrder]    = r.SortOrder
    FROM [NavigationItem] ni
    JOIN #ReorderItems r ON r.Id = ni.Id
    WHERE ni.[MenuId] = @MenuId;

    DROP TABLE #ReorderItems;

    -- Update menu UpdatedAt
    UPDATE [NavigationMenu]
    SET [UpdatedAt] = SYSUTCDATETIME()
    WHERE [Id] = @MenuId;
END;
GO
