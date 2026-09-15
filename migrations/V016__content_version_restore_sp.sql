-- V016__content_version_restore_sp.sql
-- Issue #32: Content versioning — restore endpoint.
-- usp_ContentVersion_List and usp_ContentVersion_GetById already exist in V008.
-- This migration adds the restore SP: copies the target version's FieldsJson into a NEW
-- ContentVersion row (never mutates history) and returns the new version id.

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_Restore]
    @ContentEntryId   BIGINT,
    @TargetVersionId  BIGINT,
    @ActorId          BIGINT,
    @ChangeNote       NVARCHAR(1000) = NULL,
    @NewVersionId     BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Verify the target version belongs to this entry
    IF NOT EXISTS (
        SELECT 1 FROM [ContentVersion]
        WHERE  [Id] = @TargetVersionId
          AND  [ContentEntryId] = @ContentEntryId
    )
    BEGIN
        RAISERROR('Target version not found for this content entry.', 16, 1);
        RETURN;
    END;

    -- Read the source FieldsJson from the target version
    DECLARE @SourceFields NVARCHAR(MAX);
    SELECT @SourceFields = [FieldsJson]
    FROM   [ContentVersion]
    WHERE  [Id] = @TargetVersionId;

    -- Build an auto-generated change note if none supplied
    DECLARE @Note NVARCHAR(1000) = ISNULL(@ChangeNote,
        'Restored from version ' + CAST(
            (SELECT [VersionNumber] FROM [ContentVersion] WHERE [Id] = @TargetVersionId)
        AS NVARCHAR(20)));

    -- Create the new version (auto-increments VersionNumber within the entry)
    DECLARE @NextVersion INT;
    SELECT @NextVersion = ISNULL(MAX([VersionNumber]), 0) + 1
    FROM   [ContentVersion]
    WHERE  [ContentEntryId] = @ContentEntryId;

    -- Determine current entry status for the snapshot
    DECLARE @EntryStatus NVARCHAR(20);
    SELECT @EntryStatus = [Status]
    FROM   [ContentEntry]
    WHERE  [Id] = @ContentEntryId;

    INSERT INTO [ContentVersion]
        ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
         [Status], [AuthorId], [ChangeNote], [CreatedAt])
    VALUES
        (@ContentEntryId, @NextVersion, @SourceFields, NULL,
         @EntryStatus, @ActorId, @Note, SYSUTCDATETIME());

    SET @NewVersionId = SCOPE_IDENTITY();

    -- Audit
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId,
        'ContentVersion',
        @ContentEntryId,
        'Restore',
        NULL;
END;
GO
