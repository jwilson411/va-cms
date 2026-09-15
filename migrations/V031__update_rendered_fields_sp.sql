-- V031__update_rendered_fields_sp.sql
-- Issue #66: Add usp_ContentVersion_UpdateRenderedFields for regenerating RenderedFieldsJson
-- on publish and version restore (BRD FR-AUTH-02a/02b).

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_UpdateRenderedFields]
    @Id                 BIGINT,
    @RenderedFieldsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [ContentVersion]
    SET [RenderedFieldsJson] = @RenderedFieldsJson
    WHERE [Id] = @Id;
END;
GO
