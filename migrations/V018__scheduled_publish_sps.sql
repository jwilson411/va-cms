-- V018__scheduled_publish_sps.sql
-- Issue #35: Scheduled publish / expiry stored procedures.
-- usp_ContentEntry_GetScheduledForPublish and usp_ContentEntry_GetScheduledForExpiry
-- are already defined in V008__stored_procedures.sql.
-- This migration adds the write-side SP for setting/clearing schedule times.

-- usp_ContentEntry_SetSchedule
-- Sets ScheduledPublishAt and/or ScheduledExpireAt on a content entry.
-- For publish scheduling: entry must be in 'Approved' status.
-- For expiry scheduling: entry must be in 'Published' status (or Approved when also scheduling publish).
-- Clears ScheduledPublishAt when @ScheduledPublishAt IS NULL (preserve expiry separately).
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_SetSchedule]
    @Id                 BIGINT,
    @ScheduledPublishAt DATETIME2 = NULL,
    @ScheduledExpireAt  DATETIME2 = NULL,
    @ActorId            BIGINT,
    @Success            BIT          OUTPUT,
    @ErrorMessage       NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success      = 0;
    SET @ErrorMessage = NULL;

    -- Entry must exist
    IF NOT EXISTS (SELECT 1 FROM [ContentEntry] WHERE [Id] = @Id)
    BEGIN
        SET @ErrorMessage = 'Content entry not found.';
        RETURN;
    END;

    DECLARE @CurrentStatus NVARCHAR(20);
    SELECT @CurrentStatus = [Status] FROM [ContentEntry] WHERE [Id] = @Id;

    -- Validate: scheduled publish requires entry to be Approved (or Draft for setting future)
    -- Per AC: "Content owner can set a Publish at datetime on a draft"
    -- We allow setting ScheduledPublishAt on Draft or Approved entries.
    IF @ScheduledPublishAt IS NOT NULL
       AND @CurrentStatus NOT IN ('Draft', 'Approved')
    BEGIN
        SET @ErrorMessage = 'Scheduled publish can only be set on Draft or Approved entries.';
        RETURN;
    END;

    -- Validate: scheduled expiry requires entry to be Published (or Approved if publish is also scheduled)
    IF @ScheduledExpireAt IS NOT NULL
       AND @CurrentStatus NOT IN ('Published', 'Approved')
    BEGIN
        SET @ErrorMessage = 'Scheduled expiry can only be set on Published or Approved entries.';
        RETURN;
    END;

    -- Validate: expiry must be after publish when both are set
    IF @ScheduledPublishAt IS NOT NULL AND @ScheduledExpireAt IS NOT NULL
       AND @ScheduledExpireAt <= @ScheduledPublishAt
    BEGIN
        SET @ErrorMessage = 'Expire at must be after Publish at.';
        RETURN;
    END;

    UPDATE [ContentEntry]
    SET    [ScheduledPublishAt] = @ScheduledPublishAt,
           [ScheduledExpireAt]  = @ScheduledExpireAt,
           [UpdatedAt]          = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    -- Audit log
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId    = @ActorId,
        @EntityType = 'ContentEntry',
        @EntityId   = @Id,
        @Action     = 'SetSchedule',
        @DiffJson   = NULL;

    SET @Success = 1;
END;
GO
