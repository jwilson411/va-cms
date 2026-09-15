-- V011__missing_lookup_sps.sql
-- Add lookup SPs required for story #78: all repository methods must call EXEC usp_*
-- These three lookup paths previously used raw SELECT DML in the repository layer.

-- ---------------------------------------------------------------------------
-- usp_MediaAsset_GetById
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM   [MediaAsset]
    WHERE  [Id] = @Id;
END;
GO

-- ---------------------------------------------------------------------------
-- usp_User_GetByExternalId
-- AAD Object ID / UPN lookup used during login to map to internal UserId.
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_User_GetByExternalId]
    @ExternalId NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM   [User]
    WHERE  [ExternalId] = @ExternalId;
END;
GO

-- ---------------------------------------------------------------------------
-- usp_NavigationMenu_GetByHandle
-- Fetches the NavigationMenu row by its handle slug.
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_NavigationMenu_GetByHandle]
    @Handle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM   [NavigationMenu]
    WHERE  [Handle] = @Handle;
END;
GO
