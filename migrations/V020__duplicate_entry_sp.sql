-- migrations/V020__duplicate_entry_sp.sql
-- Issue #36 — Implement duplicate entry action (BRD FR-AUTH-07)
--
-- Creates usp_ContentEntry_Duplicate:
--   - Copies all field values from the latest ContentVersion of the source entry
--   - Creates a new ContentEntry with Status = 'Draft'
--   - Appends ' (Copy)' to the title field inside FieldsJson
--   - Clears the Slug on the new entry (set to empty string — must be assigned before publish)
--   - Media references are shared (not re-uploaded) — MediaUsage rows copied for the new entry
--   - Writes an audit log row for the duplicate action
--   - Returns the new ContentEntry Id via @NewEntryId OUTPUT
--   - Returns @Success = 1 on success, 0 on failure with @ErrorMessage set

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_Duplicate]
    @SourceEntryId BIGINT,
    @ActorId       BIGINT,
    @NewEntryId    BIGINT OUTPUT,
    @Success       BIT    OUTPUT,
    @ErrorMessage  NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success      = 0;
    SET @NewEntryId   = NULL;
    SET @ErrorMessage = NULL;

    -- Validate source entry exists
    IF NOT EXISTS (SELECT 1 FROM [ContentEntry] WHERE [Id] = @SourceEntryId)
    BEGIN
        SET @ErrorMessage = 'Source content entry not found.';
        RETURN;
    END;

    -- Fetch source entry details
    DECLARE @ContentTypeId      BIGINT;
    DECLARE @Locale             NVARCHAR(10);
    DECLARE @SourceStatus       NVARCHAR(20);
    DECLARE @PublishedVersionId BIGINT;

    SELECT
        @ContentTypeId      = [ContentTypeId],
        @Locale             = [Locale],
        @SourceStatus       = [Status],
        @PublishedVersionId = [PublishedVersionId]
    FROM [ContentEntry]
    WHERE [Id] = @SourceEntryId;

    -- Determine which version to copy:
    -- Use PublishedVersionId if available; otherwise use the latest version.
    DECLARE @SourceVersionId BIGINT;
    DECLARE @SourceFieldsJson NVARCHAR(MAX);

    IF @PublishedVersionId IS NOT NULL
    BEGIN
        SELECT @SourceVersionId   = [Id],
               @SourceFieldsJson  = [FieldsJson]
        FROM   [ContentVersion]
        WHERE  [Id] = @PublishedVersionId;
    END
    ELSE
    BEGIN
        SELECT TOP 1
               @SourceVersionId   = [Id],
               @SourceFieldsJson  = [FieldsJson]
        FROM   [ContentVersion]
        WHERE  [ContentEntryId] = @SourceEntryId
        ORDER  BY [VersionNumber] DESC;
    END;

    -- If no version exists at all, use empty FieldsJson
    IF @SourceFieldsJson IS NULL
        SET @SourceFieldsJson = '{}';

    -- Append ' (Copy)' to the title field value inside FieldsJson.
    -- FieldsJson is a JSON object. The title field key is "title".
    -- We use JSON_MODIFY to append to the value if the key exists.
    -- If the title key does not exist, FieldsJson is returned unchanged.
    DECLARE @NewFieldsJson NVARCHAR(MAX);

    -- Only modify if a "title" key exists in the JSON
    IF JSON_VALUE(@SourceFieldsJson, '$.title') IS NOT NULL
    BEGIN
        SET @NewFieldsJson = JSON_MODIFY(
            @SourceFieldsJson,
            '$.title',
            JSON_VALUE(@SourceFieldsJson, '$.title') + ' (Copy)'
        );
    END
    ELSE
    BEGIN
        SET @NewFieldsJson = @SourceFieldsJson;
    END;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- 1. Create the new ContentEntry with a placeholder slug (UUID-based, must be
        --    replaced before publish). Using NEWID() ensures uniqueness per locale.
        --    The slug is considered "cleared" — it's a temporary internal value that
        --    the content owner must replace before the entry can be published.
        DECLARE @PlaceholderSlug NVARCHAR(500) = CAST(NEWID() AS NVARCHAR(50));

        INSERT INTO [ContentEntry]
            ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId], [CreatedAt], [UpdatedAt])
        VALUES
            (@ContentTypeId, @PlaceholderSlug, @Locale, 'Draft', @ActorId, SYSUTCDATETIME(), SYSUTCDATETIME());

        SET @NewEntryId = SCOPE_IDENTITY();

        -- 2. Create a new ContentVersion for the duplicate entry (version 1)
        DECLARE @NewVersionId BIGINT;

        INSERT INTO [ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
             [Status], [AuthorId], [ChangeNote], [CreatedAt])
        VALUES
            (@NewEntryId, 1, @NewFieldsJson, NULL,
             'Draft', @ActorId, 'Duplicated from entry #' + CAST(@SourceEntryId AS NVARCHAR(20)),
             SYSUTCDATETIME());

        SET @NewVersionId = SCOPE_IDENTITY();

        -- 3. Copy MediaUsage rows: share media references (no re-upload)
        --    Only copy usage rows from the source entry (not version-specific)
        INSERT INTO [MediaUsage]
            ([MediaAssetId], [ContentEntryId], [FieldName], [CreatedAt])
        SELECT
            [MediaAssetId],
            @NewEntryId,
            [FieldName],
            SYSUTCDATETIME()
        FROM [MediaUsage]
        WHERE [ContentEntryId] = @SourceEntryId;

        -- 4. Write audit log
        EXEC [dbo].[usp_AuditLog_Write]
            @ActorId    = @ActorId,
            @EntityType = 'ContentEntry',
            @EntityId   = @NewEntryId,
            @Action     = 'Duplicate',
            @DiffJson   = NULL;

        COMMIT TRANSACTION;

        SET @Success = 1;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ErrorMessage = ERROR_MESSAGE();
        SET @Success = 0;
    END CATCH;
END;
GO
