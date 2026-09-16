-- V037__content_type_lookup_sps.sql
-- Content type lookup/upsert used when the admin creates an entry by content
-- type *name* (the in-code registry has no DB id). The ContentType row is the
-- FK target for ContentEntry.ContentTypeId, so the API ensures a row exists for
-- every registered type on first use instead of relying on the demo seed.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentType_GetByName]
    @Name NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Name], [DisplayName], [Description], [TemplateId],
           [IsSystemType], [AllowWorkflow], [FieldSchemaJson], [CreatedAt], [UpdatedAt]
    FROM   [dbo].[ContentType]
    WHERE  [Name] = @Name;
END;
GO

-- Insert-or-update by Name. Registry-defined types are marked IsSystemType = 1.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentType_Upsert]
    @Name            NVARCHAR(100),
    @DisplayName     NVARCHAR(200),
    @Description     NVARCHAR(1000) = NULL,
    @TemplateId      NVARCHAR(200)  = NULL,
    @AllowWorkflow   BIT            = 1,
    @FieldSchemaJson NVARCHAR(MAX)  = '[]',
    @Id              BIGINT         OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @Id = [Id] FROM [dbo].[ContentType] WHERE [Name] = @Name;

    IF @Id IS NULL
    BEGIN
        INSERT INTO [dbo].[ContentType]
            ([Name], [DisplayName], [Description], [TemplateId], [IsSystemType], [AllowWorkflow], [FieldSchemaJson])
        VALUES
            (@Name, @DisplayName, @Description, @TemplateId, 1, @AllowWorkflow, @FieldSchemaJson);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE [dbo].[ContentType]
        SET    [DisplayName]     = @DisplayName,
               [Description]     = @Description,
               [TemplateId]      = @TemplateId,
               [AllowWorkflow]   = @AllowWorkflow,
               [FieldSchemaJson] = @FieldSchemaJson,
               [UpdatedAt]       = SYSUTCDATETIME()
        WHERE  [Id] = @Id;
    END
END;
GO

-- usp_ContentEntry_GetById now also returns the content type's machine name so the
-- admin editor can load the right field schema for an entry (it only has the id).
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.*, v.[FieldsJson], v.[RenderedFieldsJson], v.[VersionNumber],
           ct.[Name] AS ContentTypeName
    FROM   [dbo].[ContentEntry] e
    LEFT JOIN [dbo].[ContentVersion] v  ON v.[Id]  = e.[PublishedVersionId]
    LEFT JOIN [dbo].[ContentType]    ct ON ct.[Id] = e.[ContentTypeId]
    WHERE  e.[Id] = @Id;
END;
GO
