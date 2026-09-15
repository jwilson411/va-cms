-- V023__primary_nav_seed.sql
-- Issue #47: Seed the primary navigation menu and initial items.
-- The 'primary' handle is the canonical handle used by /api/v1/navigation/primary
-- and consumed by the public site USWDS header.
-- Idempotent: uses IF NOT EXISTS guards so repeat runs are safe.

SET NOCOUNT ON;

-- Insert the primary NavigationMenu if it does not already exist.
IF NOT EXISTS (SELECT 1 FROM [dbo].[NavigationMenu] WHERE [Handle] = 'primary')
BEGIN
    INSERT INTO [dbo].[NavigationMenu] ([Name], [Handle], [CreatedAt], [UpdatedAt])
    VALUES ('Primary Navigation', 'primary', SYSUTCDATETIME(), SYSUTCDATETIME());
END

-- Seed three top-level items so the public header has content out of the box.
-- Admins may reorder or replace these at any time without a code deploy.
DECLARE @MenuId BIGINT;
SELECT @MenuId = [Id] FROM [dbo].[NavigationMenu] WHERE [Handle] = 'primary';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NavigationItem] WHERE [MenuId] = @MenuId AND [Label] = 'Home')
BEGIN
    INSERT INTO [dbo].[NavigationItem]
        ([MenuId], [ParentItemId], [Label], [Url], [ContentEntryId], [Target], [SortOrder], [IsVisible])
    VALUES
        (@MenuId, NULL, 'Home', '/', NULL, '_self', 1, 1);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[NavigationItem] WHERE [MenuId] = @MenuId AND [Label] = 'News')
BEGIN
    INSERT INTO [dbo].[NavigationItem]
        ([MenuId], [ParentItemId], [Label], [Url], [ContentEntryId], [Target], [SortOrder], [IsVisible])
    VALUES
        (@MenuId, NULL, 'News', '/news', NULL, '_self', 2, 1);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[NavigationItem] WHERE [MenuId] = @MenuId AND [Label] = 'About')
BEGIN
    INSERT INTO [dbo].[NavigationItem]
        ([MenuId], [ParentItemId], [Label], [Url], [ContentEntryId], [Target], [SortOrder], [IsVisible])
    VALUES
        (@MenuId, NULL, 'About', '/about', NULL, '_self', 3, 1);
END
GO
