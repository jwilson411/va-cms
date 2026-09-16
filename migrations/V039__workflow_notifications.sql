-- V039__workflow_notifications.sql
-- Issue #38 — Build in-app notification center for workflow events
-- BRD FR-WORKFLOW-02 / FR-WORKFLOW-03
--
-- Creates:
--   Notification table — one row per (recipient, workflow event)
--   usp_Notification_CreateForWorkflowEvent — fan out one workflow event to its recipients
--   usp_Notification_ListForUser            — newest-first inbox for the bell panel
--   usp_Notification_UnreadCount            — badge count
--   usp_Notification_MarkRead               — mark a JSON array of ids read (own rows only)
--   usp_Notification_MarkAllRead            — mark every unread row read
--
-- Recipients per event type:
--   ReviewRequested  → every active user holding a publish-capable role (Editor,
--                      SiteAdmin, SystemAdmin) either globally (SectionId NULL) or
--                      scoped to a section whose SlugPrefix matches the entry slug.
--   ContentApproved  → the entry owner.
--   ContentReturned  → the entry owner.
-- The acting user is never notified about their own action.

-- ── 1. Notification table ────────────────────────────────────────────────────

CREATE TABLE [dbo].[Notification] (
    [Id]              BIGINT IDENTITY(1,1) NOT NULL,
    [RecipientUserId] BIGINT               NOT NULL,
    [EventType]       NVARCHAR(50)         NOT NULL,
    [ContentEntryId]  BIGINT               NOT NULL,
    [ContentTitle]    NVARCHAR(500)        NOT NULL,
    [Message]         NVARCHAR(1000)       NOT NULL,
    [ActorId]         BIGINT               NULL,
    [Comment]         NVARCHAR(2000)       NULL,
    [IsRead]          BIT                  NOT NULL CONSTRAINT [DF_Notification_IsRead]    DEFAULT 0,
    [ReadAt]          DATETIME2            NULL,
    [CreatedAt]       DATETIME2            NOT NULL CONSTRAINT [DF_Notification_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Notification] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Notification_Recipient]    FOREIGN KEY ([RecipientUserId]) REFERENCES [dbo].[User]         ([Id]),
    CONSTRAINT [FK_Notification_ContentEntry] FOREIGN KEY ([ContentEntryId])  REFERENCES [dbo].[ContentEntry] ([Id]),
    CONSTRAINT [FK_Notification_Actor]        FOREIGN KEY ([ActorId])         REFERENCES [dbo].[User]         ([Id]),
    CONSTRAINT [CK_Notification_EventType] CHECK ([EventType] IN ('ReviewRequested', 'ContentApproved', 'ContentReturned'))
);

-- Inbox + badge: everything is keyed by recipient, filtered on IsRead, newest first.
CREATE INDEX [IX_Notification_Recipient_IsRead_CreatedAt]
    ON [dbo].[Notification] ([RecipientUserId], [IsRead], [CreatedAt] DESC);
GO

-- ── 2. usp_Notification_CreateForWorkflowEvent ───────────────────────────────
-- Called by the API right after usp_Workflow_Transition succeeds. Resolves the
-- recipient set for @EventType, snapshots the entry title from its latest
-- version, and inserts one row per recipient. @Count reports how many were sent.

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

    DECLARE @ActorName NVARCHAR(500);
    SELECT @ActorName = [DisplayName] FROM [dbo].[User] WHERE [Id] = @ActorId;
    IF @ActorName IS NULL SET @ActorName = N'Someone';

    DECLARE @Message NVARCHAR(1000) =
        CASE @EventType
            WHEN 'ReviewRequested' THEN @ActorName + N' submitted "' + @Title + N'" for review'
            WHEN 'ContentApproved' THEN @ActorName + N' approved "' + @Title + N'"'
            WHEN 'ContentReturned' THEN @ActorName + N' returned "' + @Title + N'" to draft'
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

    INSERT INTO [dbo].[Notification]
        ([RecipientUserId], [EventType], [ContentEntryId], [ContentTitle], [Message], [ActorId], [Comment], [CreatedAt])
    SELECT [UserId], @EventType, @ContentEntryId, @Title, @Message, @ActorId, @Comment, SYSUTCDATETIME()
    FROM   @Recipients;

    SET @Count = @@ROWCOUNT;
END;
GO

-- ── 3. usp_Notification_ListForUser ──────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Notification_ListForUser]
    @UserId     BIGINT,
    @UnreadOnly BIT = 0,
    @Limit      INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit)
        n.[Id],
        n.[RecipientUserId],
        n.[EventType],
        n.[ContentEntryId],
        n.[ContentTitle],
        n.[Message],
        n.[ActorId],
        a.[DisplayName] AS ActorDisplayName,
        n.[Comment],
        n.[IsRead],
        n.[ReadAt],
        n.[CreatedAt],
        e.[Slug]        AS EntrySlug,
        e.[Status]      AS EntryStatus
    FROM   [dbo].[Notification] n
    JOIN   [dbo].[ContentEntry] e ON e.[Id] = n.[ContentEntryId]
    LEFT JOIN [dbo].[User]      a ON a.[Id] = n.[ActorId]
    WHERE  n.[RecipientUserId] = @UserId
      AND  (@UnreadOnly = 0 OR n.[IsRead] = 0)
    ORDER  BY n.[CreatedAt] DESC, n.[Id] DESC;
END;
GO

-- ── 4. usp_Notification_UnreadCount ──────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Notification_UnreadCount]
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(1) AS UnreadCount
    FROM   [dbo].[Notification]
    WHERE  [RecipientUserId] = @UserId
      AND  [IsRead] = 0;
END;
GO

-- ── 5. usp_Notification_MarkRead ─────────────────────────────────────────────
-- @IdsJson is a JSON array of notification ids, e.g. '[1,2,3]'. Only rows owned
-- by @UserId are touched, so a caller can never mark someone else's inbox.

CREATE OR ALTER PROCEDURE [dbo].[usp_Notification_MarkRead]
    @UserId  BIGINT,
    @IdsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE n
    SET    [IsRead] = 1,
           [ReadAt] = SYSUTCDATETIME()
    FROM   [dbo].[Notification] n
    JOIN   OPENJSON(@IdsJson) WITH ([Id] BIGINT '$') ids ON ids.[Id] = n.[Id]
    WHERE  n.[RecipientUserId] = @UserId
      AND  n.[IsRead] = 0;

    SELECT @@ROWCOUNT AS Updated;
END;
GO

-- ── 6. usp_Notification_MarkAllRead ──────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Notification_MarkAllRead]
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[Notification]
    SET    [IsRead] = 1,
           [ReadAt] = SYSUTCDATETIME()
    WHERE  [RecipientUserId] = @UserId
      AND  [IsRead] = 0;

    SELECT @@ROWCOUNT AS Updated;
END;
GO
