-- V041__workflow_notification_email.sql
-- Issue #39 — Implement email notifications for workflow events via SMTP
-- BRD FR-WORKFLOW-02
--
-- Changes:
--   * ContentPublished joins the Notification event types (→ the entry owner), so
--     "content published" reaches the author in-app and by email.
--   * usp_Notification_CreateForWorkflowEvent now returns the rows it created,
--     joined to the recipient's Email and DisplayName, so the API can send one
--     email per recipient from the very same recipient set that fills the inbox.
--     @Count OUTPUT is kept for callers that only want the number.
--   * ActorId 0 (the scheduled-publish worker's system actor) is stored as NULL —
--     it has no User row — and gets its own wording.

-- ── 1. Allow ContentPublished ────────────────────────────────────────────────

ALTER TABLE [dbo].[Notification] DROP CONSTRAINT [CK_Notification_EventType];
ALTER TABLE [dbo].[Notification] ADD CONSTRAINT [CK_Notification_EventType]
    CHECK ([EventType] IN ('ReviewRequested', 'ContentApproved', 'ContentReturned', 'ContentPublished'));
GO

-- ── 2. usp_Notification_CreateForWorkflowEvent ───────────────────────────────
-- Recipients per event type:
--   ReviewRequested  → every active user holding a publish-capable role (Editor,
--                      SiteAdmin, SystemAdmin) either globally (SectionId NULL) or
--                      scoped to a section whose SlugPrefix matches the entry slug.
--   ContentApproved  → the entry owner.
--   ContentReturned  → the entry owner.
--   ContentPublished → the entry owner.
-- The acting user is never notified about their own action.
--
-- Result set: one row per notification created —
--   Id, RecipientUserId, RecipientEmail, RecipientDisplayName, EventType,
--   ContentEntryId, ContentTitle, Message, ActorDisplayName, Comment

CREATE OR ALTER PROCEDURE [dbo].[usp_Notification_CreateForWorkflowEvent]
    @ContentEntryId BIGINT,
    @EventType      NVARCHAR(50),
    @ActorId        BIGINT,
    @Comment        NVARCHAR(2000) = NULL,
    @Count          INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Count = 0;

    DECLARE @Slug    NVARCHAR(500);
    DECLARE @OwnerId BIGINT;
    SELECT @Slug = [Slug], @OwnerId = [OwnerId]
    FROM   [dbo].[ContentEntry]
    WHERE  [Id] = @ContentEntryId;

    IF @Slug IS NULL RETURN;   -- unknown entry: nothing to notify

    -- Title snapshot: latest version's $.title, falling back to the slug.
    DECLARE @Title NVARCHAR(500);
    SELECT TOP (1) @Title = JSON_VALUE(v.[FieldsJson], '$.title')
    FROM   [dbo].[ContentVersion] v
    WHERE  v.[ContentEntryId] = @ContentEntryId
    ORDER  BY v.[VersionNumber] DESC;
    IF @Title IS NULL OR LEN(@Title) = 0 SET @Title = @Slug;

    -- Actor 0 is the scheduler (no User row); store NULL and word the message accordingly.
    DECLARE @StoredActorId BIGINT = NULLIF(@ActorId, 0);
    DECLARE @ActorName NVARCHAR(500);
    SELECT @ActorName = [DisplayName] FROM [dbo].[User] WHERE [Id] = @StoredActorId;
    IF @ActorName IS NULL SET @ActorName = N'Someone';

    DECLARE @Message NVARCHAR(1000) =
        CASE @EventType
            WHEN 'ReviewRequested'  THEN @ActorName + N' submitted "' + @Title + N'" for review'
            WHEN 'ContentApproved'  THEN @ActorName + N' approved "' + @Title + N'"'
            WHEN 'ContentReturned'  THEN @ActorName + N' returned "' + @Title + N'" to draft'
            WHEN 'ContentPublished' THEN
                CASE WHEN @StoredActorId IS NULL
                     THEN N'"' + @Title + N'" was published as scheduled'
                     ELSE @ActorName + N' published "' + @Title + N'"'
                END
        END;
    IF @Message IS NULL RETURN;   -- unknown event type

    DECLARE @Recipients TABLE ([UserId] BIGINT PRIMARY KEY);

    IF @EventType = 'ReviewRequested'
    BEGIN
        INSERT INTO @Recipients ([UserId])
        SELECT DISTINCT ur.[UserId]
        FROM   [dbo].[UserRole] ur
        JOIN   [dbo].[Role]     r  ON r.[Id]  = ur.[RoleId]
        JOIN   [dbo].[User]     u  ON u.[Id]  = ur.[UserId]
        LEFT JOIN [dbo].[ContentSection] cs ON cs.[Id] = ur.[SectionId]
        WHERE  r.[Name] IN ('Editor', 'SiteAdmin', 'SystemAdmin')
          AND  u.[IsActive] = 1
          AND  ur.[UserId] <> @ActorId
          AND  (
                 ur.[SectionId] IS NULL
              OR LEFT(@Slug, LEN(cs.[SlugPrefix])) = cs.[SlugPrefix]
               );
    END
    ELSE
    BEGIN
        INSERT INTO @Recipients ([UserId])
        SELECT u.[Id]
        FROM   [dbo].[User] u
        WHERE  u.[Id] = @OwnerId
          AND  u.[IsActive] = 1
          AND  u.[Id] <> @ActorId;
    END;

    DECLARE @Inserted TABLE ([Id] BIGINT PRIMARY KEY, [RecipientUserId] BIGINT);

    INSERT INTO [dbo].[Notification]
        ([RecipientUserId], [EventType], [ContentEntryId], [ContentTitle], [Message], [ActorId], [Comment], [CreatedAt])
    OUTPUT INSERTED.[Id], INSERTED.[RecipientUserId] INTO @Inserted ([Id], [RecipientUserId])
    SELECT [UserId], @EventType, @ContentEntryId, @Title, @Message, @StoredActorId, @Comment, SYSUTCDATETIME()
    FROM   @Recipients;

    SET @Count = @@ROWCOUNT;

    SELECT i.[Id],
           i.[RecipientUserId],
           u.[Email]           AS RecipientEmail,
           u.[DisplayName]     AS RecipientDisplayName,
           @EventType          AS EventType,
           @ContentEntryId     AS ContentEntryId,
           @Title              AS ContentTitle,
           @Message            AS Message,
           CASE WHEN @StoredActorId IS NULL THEN NULL ELSE @ActorName END AS ActorDisplayName,
           @Comment            AS Comment
    FROM   @Inserted i
    JOIN   [dbo].[User] u ON u.[Id] = i.[RecipientUserId]
    ORDER  BY i.[Id];
END;
GO
