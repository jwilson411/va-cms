-- V042__migration_status_sp.sql
-- Issue #157: the API runs as vacms_app (EXECUTE only, SELECT denied), yet at
-- startup it must confirm the database is at the expected migration level
-- before serving traffic. DbUp's journal (dbo.SchemaVersions) is a plain table,
-- so expose it through a stored procedure the service account may execute.
CREATE OR ALTER PROCEDURE [dbo].[usp_Migrations_ListApplied]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [ScriptName], [Applied]
    FROM   [dbo].[SchemaVersions]
    ORDER BY [Id];
END;
GO
