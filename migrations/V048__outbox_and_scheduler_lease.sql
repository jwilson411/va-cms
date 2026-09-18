-- V048__outbox_and_scheduler_lease.sql
-- Issue #171 (epic #152): multi-instance safety (BRD NFR-OPS-04).
--
--   * [OutboundEvent] is the transactional outbox: every webhook delivery and every
--     workflow email is a row written by the same connection that made the domain
--     change, and delivered later by the OutboxDispatcherWorker that runs on every
--     API node. A node claims a batch with UPDLOCK/READPAST so two nodes never take
--     the same row; a claim expires (LockedAt + lease) so a row locked by a node that
--     died is picked up again. Work survives an app-pool recycle.
--   * usp_ContentEntry_ClaimScheduledForPublish / _ClaimScheduledForExpiry replace the
--     scheduler's select-then-update pair: the transition, its audit rows and the
--     content.published / content.unpublished outbox rows happen in one transaction
--     against rows read WITH (UPDLOCK, READPAST), so with N nodes each due entry is
--     published exactly once and its webhooks fire exactly once.
--   * usp_SiteSetting_GetChangeStamp lets each node notice a settings write made on
--     another node without reloading the whole table (settings cache bust).

-- ── 1. OutboundEvent ─────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.OutboundEvent', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OutboundEvent] (
        [Id]            BIGINT IDENTITY(1,1) NOT NULL,
        [Type]          NVARCHAR(50)         NOT NULL,   -- webhook | email
        [EventName]     NVARCHAR(100)        NULL,       -- webhook: CMS event name (content.published …)
        [WebhookId]     BIGINT               NULL,       -- webhook: the subscriber this row delivers to
        [PayloadJson]   NVARCHAR(MAX)        NOT NULL,   -- webhook: signed body; email: EmailMessage[]
        [Status]        NVARCHAR(20)         NOT NULL CONSTRAINT [DF_OutboundEvent_Status] DEFAULT 'Pending',
        [Attempts]      INT                  NOT NULL CONSTRAINT [DF_OutboundEvent_Attempts] DEFAULT 0,
        [NextAttemptAt] DATETIME2            NOT NULL CONSTRAINT [DF_OutboundEvent_NextAttemptAt] DEFAULT SYSUTCDATETIME(),
        [LockedBy]      NVARCHAR(100)        NULL,       -- machine:pid:instance of the node that claimed it
        [LockedAt]      DATETIME2            NULL,
        [LastError]     NVARCHAR(2000)       NULL,
        [CreatedAt]     DATETIME2            NOT NULL CONSTRAINT [DF_OutboundEvent_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [CompletedAt]   DATETIME2            NULL,
        CONSTRAINT [PK_OutboundEvent] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_OutboundEvent_Webhook] FOREIGN KEY ([WebhookId]) REFERENCES [dbo].[Webhook] ([Id]),
        CONSTRAINT [CK_OutboundEvent_Type]   CHECK ([Type]   IN ('webhook', 'email')),
        CONSTRAINT [CK_OutboundEvent_Status] CHECK ([Status] IN ('Pending', 'Succeeded', 'Failed'))
    );

    -- The claim scan: pending rows in due order. Filtered so the completed history
    -- never widens it.
    CREATE NONCLUSTERED INDEX [IX_OutboundEvent_Pending]
        ON [dbo].[OutboundEvent] ([NextAttemptAt] ASC, [Id] ASC)
        INCLUDE ([LockedAt])
        WHERE [Status] = 'Pending';

    -- Per-webhook history for the admin delivery log and the purge.
    CREATE NONCLUSTERED INDEX [IX_OutboundEvent_Webhook_CreatedAt]
        ON [dbo].[OutboundEvent] ([WebhookId], [CreatedAt] DESC)
        WHERE [WebhookId] IS NOT NULL;

    CREATE NONCLUSTERED INDEX [IX_OutboundEvent_Status_CompletedAt]
        ON [dbo].[OutboundEvent] ([Status], [CompletedAt])
        WHERE [Status] <> 'Pending';
END;
GO

-- ── 2. usp_OutboundEvent_Enqueue ─────────────────────────────────────────────
-- @Type = 'webhook': one row per active webhook subscribed to @EventName (the fan-out
-- happens here, inside the caller's transaction, so a subscriber added later does not
-- receive the event — the same rule the in-process dispatcher had). Nothing is written
-- when no webhook subscribes. @Type = 'email': exactly one row.
-- @Count is the number of rows inserted.
CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Enqueue]
    @Type        NVARCHAR(50),
    @EventName   NVARCHAR(100)  = NULL,
    @PayloadJson NVARCHAR(MAX),
    @Count       INT            OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Count = 0;

    IF @Type = 'webhook'
    BEGIN
        IF @EventName IS NULL
            THROW 50000, 'usp_OutboundEvent_Enqueue: @EventName is required for webhook rows.', 1;

        INSERT INTO [dbo].[OutboundEvent] ([Type], [EventName], [WebhookId], [PayloadJson])
        SELECT 'webhook', @EventName, w.[Id], @PayloadJson
        FROM   [dbo].[Webhook] w
        WHERE  w.[IsActive] = 1
          AND  w.[EventsJson] LIKE '%"' + @EventName + '"%';
        SET @Count = @@ROWCOUNT;
        RETURN;
    END;

    INSERT INTO [dbo].[OutboundEvent] ([Type], [EventName], [WebhookId], [PayloadJson])
    VALUES (@Type, @EventName, NULL, @PayloadJson);
    SET @Count = @@ROWCOUNT;
END;
GO

-- ── 3. usp_OutboundEvent_Claim ───────────────────────────────────────────────
-- Take up to @BatchSize due rows for @LockedBy. One UPDATE with OUTPUT is atomic;
-- READPAST skips rows another node is claiming at the same instant, and a row whose
-- lease (@LeaseSeconds since LockedAt) has expired is claimable again — that is how
-- work owned by a node that was recycled mid-delivery is recovered. Attempts is
-- counted at claim time so a crash during delivery still consumes an attempt.
CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Claim]
    @LockedBy     NVARCHAR(100),
    @BatchSize    INT = 20,
    @LeaseSeconds INT = 300
AS
BEGIN
    SET NOCOUNT ON;
    IF @BatchSize < 1 SET @BatchSize = 1;
    IF @BatchSize > 500 SET @BatchSize = 500;
    IF @LeaseSeconds < 10 SET @LeaseSeconds = 10;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    WITH due AS (
        SELECT TOP (@BatchSize) *
        FROM   [dbo].[OutboundEvent] WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE  [Status]        = 'Pending'
          AND  [NextAttemptAt] <= @Now
          AND  ([LockedAt] IS NULL OR [LockedAt] <= DATEADD(SECOND, -@LeaseSeconds, @Now))
        ORDER  BY [NextAttemptAt] ASC, [Id] ASC
    )
    UPDATE due
    SET    [LockedBy] = @LockedBy,
           [LockedAt] = @Now,
           [Attempts] = [Attempts] + 1
    OUTPUT inserted.[Id], inserted.[Type], inserted.[EventName], inserted.[WebhookId],
           inserted.[PayloadJson], inserted.[Status], inserted.[Attempts], inserted.[NextAttemptAt],
           inserted.[LockedBy], inserted.[LockedAt], inserted.[LastError], inserted.[CreatedAt],
           inserted.[CompletedAt];
END;
GO

-- ── 4. usp_OutboundEvent_Complete / _Reschedule / _Fail ──────────────────────
-- Each checks @LockedBy so a node whose lease expired and was taken over cannot
-- overwrite the new owner's result.
CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Complete]
    @Id       BIGINT,
    @LockedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[OutboundEvent]
    SET    [Status]      = 'Succeeded',
           [CompletedAt] = SYSUTCDATETIME(),
           [LastError]   = NULL,
           [LockedBy]    = NULL,
           [LockedAt]    = NULL
    WHERE  [Id] = @Id AND [Status] = 'Pending' AND [LockedBy] = @LockedBy;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Reschedule]
    @Id            BIGINT,
    @LockedBy      NVARCHAR(100),
    @NextAttemptAt DATETIME2,
    @LastError     NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[OutboundEvent]
    SET    [NextAttemptAt] = @NextAttemptAt,
           [LastError]     = @LastError,
           [LockedBy]      = NULL,
           [LockedAt]      = NULL
    WHERE  [Id] = @Id AND [Status] = 'Pending' AND [LockedBy] = @LockedBy;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Fail]
    @Id        BIGINT,
    @LockedBy  NVARCHAR(100),
    @LastError NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[OutboundEvent]
    SET    [Status]      = 'Failed',
           [CompletedAt] = SYSUTCDATETIME(),
           [LastError]   = @LastError,
           [LockedBy]    = NULL,
           [LockedAt]    = NULL
    WHERE  [Id] = @Id AND [Status] = 'Pending' AND [LockedBy] = @LockedBy;
END;
GO

-- ── 5. usp_OutboundEvent_Purge ───────────────────────────────────────────────
-- Delete completed rows (either outcome) older than @OlderThanDays, in chunks so the
-- purge never holds a long lock. Pending rows are never purged.
CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Purge]
    @OlderThanDays INT = 14,
    @Deleted       INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @OlderThanDays < 1 SET @OlderThanDays = 1;
    SET @Deleted = 0;

    DECLARE @Cutoff DATETIME2 = DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());
    DECLARE @Batch INT = 1;
    WHILE @Batch > 0
    BEGIN
        DELETE TOP (1000) FROM [dbo].[OutboundEvent]
        WHERE  [Status] <> 'Pending' AND [CompletedAt] < @Cutoff;
        SET @Batch = @@ROWCOUNT;
        SET @Deleted = @Deleted + @Batch;
    END;
END;
GO

-- ── 6. usp_OutboundEvent_Stats ───────────────────────────────────────────────
-- Backlog by status plus the age of the oldest due row; readiness reports Degraded
-- when the backlog is stale.
CREATE OR ALTER PROCEDURE [dbo].[usp_OutboundEvent_Stats]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        [Pending]   = SUM(CASE WHEN [Status] = 'Pending'   THEN 1 ELSE 0 END),
        [Succeeded] = SUM(CASE WHEN [Status] = 'Succeeded' THEN 1 ELSE 0 END),
        [Failed]    = SUM(CASE WHEN [Status] = 'Failed'    THEN 1 ELSE 0 END),
        [OldestPendingAt] = MIN(CASE WHEN [Status] = 'Pending' THEN [NextAttemptAt] END)
    FROM [dbo].[OutboundEvent];
END;
GO

-- ── 7. Scheduler: claim-and-transition in one transaction ────────────────────
-- Replaces the usp_ContentEntry_GetScheduledForPublish → usp_ContentEntry_PublishScheduled
-- pair the worker used to call. Rows are read WITH (UPDLOCK, READPAST, ROWLOCK): a
-- second node sweeping at the same moment skips the rows this one holds, and after the
-- commit they are no longer Approved, so each due entry transitions on exactly one node.
-- Within the same transaction: the audit rows (system@scheduler, as V019 wrote them) and
-- the content.published outbox rows for every subscribed webhook — the payload shape is
-- the one ContentController sends for a manual publish.
-- PublishedVersionId is set to the entry's latest version when it has none yet, so a
-- first-time scheduled publish is visible to the public site.
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_ClaimScheduledForPublish]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Claimed TABLE (
        [Id]            BIGINT        NOT NULL,
        [ContentTypeId] BIGINT        NOT NULL,
        [Slug]          NVARCHAR(200) NOT NULL,
        [Locale]        NVARCHAR(10)  NOT NULL
    );

    BEGIN TRANSACTION;

    UPDATE e
    SET    [Status]             = 'Published',
           [ScheduledPublishAt] = NULL,
           [PublishedVersionId] = COALESCE(e.[PublishedVersionId],
                                    (SELECT TOP 1 v.[Id] FROM [dbo].[ContentVersion] v
                                     WHERE v.[ContentEntryId] = e.[Id]
                                     ORDER BY v.[VersionNumber] DESC)),
           [UpdatedAt]          = SYSUTCDATETIME()
    OUTPUT inserted.[Id], inserted.[ContentTypeId], inserted.[Slug], inserted.[Locale] INTO @Claimed
    FROM   [dbo].[ContentEntry] e WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE  e.[Status]              = 'Approved'
      AND  e.[ScheduledPublishAt] IS NOT NULL
      AND  e.[ScheduledPublishAt] <= SYSUTCDATETIME();

    INSERT INTO [dbo].[AuditLog]
        ([ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
    SELECT NULL, 'system@scheduler', 'ContentEntry', CAST(c.[Id] AS NVARCHAR(100)),
           'ScheduledPublish', NULL, SYSUTCDATETIME()
    FROM   @Claimed c;

    INSERT INTO [dbo].[OutboundEvent] ([Type], [EventName], [WebhookId], [PayloadJson])
    SELECT 'webhook', 'content.published', w.[Id],
           (SELECT c.[Id] AS [id], c.[Slug] AS [slug], c.[Locale] AS [locale],
                   ct.[Name] AS [contentTypeName], 'Published' AS [status]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    FROM   @Claimed c
    JOIN   [dbo].[ContentType] ct ON ct.[Id] = c.[ContentTypeId]
    CROSS JOIN [dbo].[Webhook] w
    WHERE  w.[IsActive] = 1
      AND  w.[EventsJson] LIKE '%"content.published"%';

    COMMIT TRANSACTION;

    SELECT [Id], [ContentTypeId], [Slug], [Locale] FROM @Claimed ORDER BY [Id];
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_ClaimScheduledForExpiry]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Claimed TABLE (
        [Id]            BIGINT        NOT NULL,
        [ContentTypeId] BIGINT        NOT NULL,
        [Slug]          NVARCHAR(200) NOT NULL,
        [Locale]        NVARCHAR(10)  NOT NULL
    );

    BEGIN TRANSACTION;

    UPDATE e
    SET    [Status]            = 'Approved',
           [ScheduledExpireAt] = NULL,
           [UpdatedAt]         = SYSUTCDATETIME()
    OUTPUT inserted.[Id], inserted.[ContentTypeId], inserted.[Slug], inserted.[Locale] INTO @Claimed
    FROM   [dbo].[ContentEntry] e WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE  e.[Status]             = 'Published'
      AND  e.[ScheduledExpireAt] IS NOT NULL
      AND  e.[ScheduledExpireAt] <= SYSUTCDATETIME();

    INSERT INTO [dbo].[AuditLog]
        ([ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
    SELECT NULL, 'system@scheduler', 'ContentEntry', CAST(c.[Id] AS NVARCHAR(100)),
           'ScheduledExpire', NULL, SYSUTCDATETIME()
    FROM   @Claimed c;

    INSERT INTO [dbo].[OutboundEvent] ([Type], [EventName], [WebhookId], [PayloadJson])
    SELECT 'webhook', 'content.unpublished', w.[Id],
           (SELECT c.[Id] AS [id], c.[Slug] AS [slug], c.[Locale] AS [locale],
                   ct.[Name] AS [contentTypeName], 'Approved' AS [status]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    FROM   @Claimed c
    JOIN   [dbo].[ContentType] ct ON ct.[Id] = c.[ContentTypeId]
    CROSS JOIN [dbo].[Webhook] w
    WHERE  w.[IsActive] = 1
      AND  w.[EventsJson] LIKE '%"content.unpublished"%';

    COMMIT TRANSACTION;

    SELECT [Id], [ContentTypeId], [Slug], [Locale] FROM @Claimed ORDER BY [Id];
END;
GO

-- ── 8. Settings cache bust ───────────────────────────────────────────────────
-- One cheap row: the newest UpdatedAt and the row count. A node compares it with the
-- stamp of its snapshot every few seconds and reloads only when it moved, so a write
-- on node A is live on node B within the poll interval instead of the full refresh.
CREATE OR ALTER PROCEDURE [dbo].[usp_SiteSetting_GetChangeStamp]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UpdatedAt] = MAX([UpdatedAt]), [RowCount] = COUNT_BIG(1)
    FROM   [dbo].[SiteSetting];
END;
GO
