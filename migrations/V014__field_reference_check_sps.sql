-- V014__field_reference_check_sps.sql
-- Stored procedures used by DbFieldReferenceResolver to verify that
-- MediaReference and SingleRelation/MultiRelation field IDs exist.
-- Required by FR-SCHEMA-02 (issue #25).

-- usp_MediaAsset_ExistsById
-- Returns 1 if the asset exists, 0 otherwise.
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_ExistsById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM [MediaAsset] WHERE [Id] = @Id)
        SELECT 1 AS [Exists];
    ELSE
        SELECT 0 AS [Exists];
END;
GO

-- usp_ContentEntry_ExistsById
-- Returns 1 if the content entry exists, 0 otherwise.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_ExistsById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM [ContentEntry] WHERE [Id] = @Id)
        SELECT 1 AS [Exists];
    ELSE
        SELECT 0 AS [Exists];
END;
GO
