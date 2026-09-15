-- V019__scheduler_transition_sps.sql
-- Issue #35: Dedicated stored procedures for the background scheduler to
-- publish or expire content entries. These bypass usp_Workflow_Transition
-- (which requires a known ContentVersionId from a user-driven action) and
-- instead perform the status update + audit log directly. The scheduler
-- runs as a system actor (ActorId = 0), so we insert a system User row if
-- one does not yet exist to satisfy the AuditLog FK.

-- usp_ContentEntry_PublishScheduled
-- Called by the background worker when ScheduledPublishAt has passed.
-- Transitions: Approved → Published, clears ScheduledPublishAt.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_PublishScheduled]
    @Id      BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Only act if the entry is still Approved (idempotent guard)
    IF NOT EXISTS (
        SELECT 1 FROM [ContentEntry]
        WHERE [Id] = @Id AND [Status] = 'Approved'
    )
        RETURN;

    UPDATE [ContentEntry]
    SET    [Status]             = 'Published',
           [ScheduledPublishAt] = NULL,
           [UpdatedAt]          = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    -- Append audit log (direct insert — scheduler bypasses usp_AuditLog_Write
    -- to avoid requiring a resolved actor email for ActorId = 0)
    INSERT INTO [AuditLog]
        ([ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
    VALUES
        (NULL, 'system@scheduler', 'ContentEntry', CAST(@Id AS NVARCHAR(100)),
         'ScheduledPublish', NULL, SYSUTCDATETIME());
END;
GO

-- usp_ContentEntry_ExpireScheduled
-- Called by the background worker when ScheduledExpireAt has passed.
-- Transitions: Published → Approved (unpublish), clears ScheduledExpireAt.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_ExpireScheduled]
    @Id      BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Only act if the entry is still Published (idempotent guard)
    IF NOT EXISTS (
        SELECT 1 FROM [ContentEntry]
        WHERE [Id] = @Id AND [Status] = 'Published'
    )
        RETURN;

    UPDATE [ContentEntry]
    SET    [Status]            = 'Approved',
           [ScheduledExpireAt] = NULL,
           [UpdatedAt]         = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    -- Append audit log
    INSERT INTO [AuditLog]
        ([ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
    VALUES
        (NULL, 'system@scheduler', 'ContentEntry', CAST(@Id AS NVARCHAR(100)),
         'ScheduledExpire', NULL, SYSUTCDATETIME());
END;
GO
