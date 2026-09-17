-- V043__audit_log_columns.sql
-- Issue #165 (epic #152) — audit log coverage, part 1: the row shape (NIST AU-3).
--
--   * [AuditLog] gains [CorrelationId] and [Outcome] (Success | Failure). [IpAddress]
--     and [UserAgent] already existed but nothing wrote them.
--   * usp_AuditLog_Write takes the new columns as optional parameters and, when a
--     parameter is NULL, falls back to SESSION_CONTEXT. The API sets ActorId,
--     SourceIp, UserAgent and CorrelationId on every connection it opens for an
--     authenticated request (CmsDatabase.OnConnectionOpened), so a stored procedure
--     that audits inside its own transaction records who/where without a
--     signature change. Explicit parameters always win.
--   * The viewer SPs return the new columns and filter by outcome / IP.
--   * [AuditLogArchive] and usp_Maint_ArchiveAuditLog carry the new columns.

-- ============================================================
-- Columns
-- ============================================================
IF COL_LENGTH('dbo.AuditLog', 'CorrelationId') IS NULL
    ALTER TABLE [dbo].[AuditLog] ADD [CorrelationId] NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.AuditLog', 'Outcome') IS NULL
    ALTER TABLE [dbo].[AuditLog] ADD [Outcome] NVARCHAR(20) NOT NULL CONSTRAINT [DF_AuditLog_Outcome] DEFAULT 'Success';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = 'CK_AuditLog_Outcome')
    ALTER TABLE [dbo].[AuditLog] ADD CONSTRAINT [CK_AuditLog_Outcome] CHECK ([Outcome] IN ('Success', 'Failure'));
GO

IF COL_LENGTH('dbo.AuditLogArchive', 'IpAddress') IS NULL
    ALTER TABLE [dbo].[AuditLogArchive] ADD [IpAddress] NVARCHAR(50) NULL;
GO
IF COL_LENGTH('dbo.AuditLogArchive', 'UserAgent') IS NULL
    ALTER TABLE [dbo].[AuditLogArchive] ADD [UserAgent] NVARCHAR(500) NULL;
GO
IF COL_LENGTH('dbo.AuditLogArchive', 'CorrelationId') IS NULL
    ALTER TABLE [dbo].[AuditLogArchive] ADD [CorrelationId] NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.AuditLogArchive', 'Outcome') IS NULL
    ALTER TABLE [dbo].[AuditLogArchive] ADD [Outcome] NVARCHAR(20) NOT NULL CONSTRAINT [DF_AuditLogArchive_Outcome] DEFAULT 'Success';
GO

-- Failed events are the ones an ISSO searches for; keep them cheap to find.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = 'IX_AuditLog_Outcome_CreatedAt')
    CREATE INDEX [IX_AuditLog_Outcome_CreatedAt] ON [dbo].[AuditLog] ([Outcome], [CreatedAt] DESC) WHERE [Outcome] = 'Failure';
GO

-- ============================================================
-- usp_AuditLog_Write — explicit parameters, else SESSION_CONTEXT
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_Write]
    @ActorId       BIGINT,
    @EntityType    NVARCHAR(100),
    @EntityId      BIGINT,
    @Action        NVARCHAR(50),
    @DiffJson      NVARCHAR(MAX) = NULL,
    @Outcome       NVARCHAR(20)  = NULL,
    @SourceIp      NVARCHAR(50)  = NULL,
    @UserAgent     NVARCHAR(500) = NULL,
    @CorrelationId NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Actor BIGINT = COALESCE(@ActorId, TRY_CAST(SESSION_CONTEXT(N'ActorId') AS BIGINT));
    IF @Actor = 0 SET @Actor = NULL;   -- callers pass 0 when no user is resolved

    DECLARE @ActorEmail NVARCHAR(500);
    SELECT @ActorEmail = [Email] FROM [dbo].[User] WHERE [Id] = @Actor;

    INSERT INTO [dbo].[AuditLog]
        ([ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson],
         [IpAddress], [UserAgent], [CorrelationId], [Outcome], [CreatedAt])
    VALUES
        (@Actor, @ActorEmail, @EntityType, CAST(@EntityId AS NVARCHAR(100)), @Action, @DiffJson,
         COALESCE(@SourceIp,      TRY_CAST(SESSION_CONTEXT(N'SourceIp')      AS NVARCHAR(50))),
         COALESCE(@UserAgent,     TRY_CAST(SESSION_CONTEXT(N'UserAgent')     AS NVARCHAR(500))),
         COALESCE(@CorrelationId, TRY_CAST(SESSION_CONTEXT(N'CorrelationId') AS NVARCHAR(100))),
         CASE WHEN @Outcome = 'Failure' THEN 'Failure' ELSE 'Success' END,
         SYSUTCDATETIME());
END;
GO

-- ============================================================
-- Viewer SPs (issue #57) — new columns and filters
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_ListPaged]
    @ActorId    BIGINT        = NULL,
    @Action     NVARCHAR(50)  = NULL,
    @EntityType NVARCHAR(100) = NULL,
    @FromDate   DATETIME2     = NULL,
    @ToDate     DATETIME2     = NULL,
    @Page       INT           = 1,
    @PageSize   INT           = 50,
    @TotalRows  INT           OUTPUT,
    @Outcome    NVARCHAR(20)  = NULL,
    @IpAddress  NVARCHAR(50)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalRows = COUNT(*)
    FROM   [AuditLog]
    WHERE  (@ActorId    IS NULL OR [ActorId]    = @ActorId)
      AND  (@Action     IS NULL OR [Action]     = @Action)
      AND  (@EntityType IS NULL OR [EntityType] = @EntityType)
      AND  (@FromDate   IS NULL OR [CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR [CreatedAt] <= @ToDate)
      AND  (@Outcome    IS NULL OR [Outcome]    = @Outcome)
      AND  (@IpAddress  IS NULL OR [IpAddress]  = @IpAddress);

    SELECT
        al.[Id],
        al.[ActorId],
        al.[ActorEmail],
        al.[EntityType],
        al.[EntityId],
        al.[Action],
        al.[DiffJson],
        al.[IpAddress],
        al.[UserAgent],
        al.[CorrelationId],
        al.[Outcome],
        al.[CreatedAt],
        ISNULL(u.[DisplayName], al.[ActorEmail]) AS ActorDisplayName
    FROM   [AuditLog] al
    LEFT JOIN [User] u ON u.Id = al.ActorId
    WHERE  (@ActorId    IS NULL OR al.[ActorId]    = @ActorId)
      AND  (@Action     IS NULL OR al.[Action]     = @Action)
      AND  (@EntityType IS NULL OR al.[EntityType] = @EntityType)
      AND  (@FromDate   IS NULL OR al.[CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR al.[CreatedAt] <= @ToDate)
      AND  (@Outcome    IS NULL OR al.[Outcome]    = @Outcome)
      AND  (@IpAddress  IS NULL OR al.[IpAddress]  = @IpAddress)
    ORDER  BY al.[CreatedAt] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_ExportCsv]
    @ActorId    BIGINT        = NULL,
    @Action     NVARCHAR(50)  = NULL,
    @EntityType NVARCHAR(100) = NULL,
    @FromDate   DATETIME2     = NULL,
    @ToDate     DATETIME2     = NULL,
    @Outcome    NVARCHAR(20)  = NULL,
    @IpAddress  NVARCHAR(50)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1000
        al.[Id],
        al.[ActorId],
        al.[ActorEmail],
        ISNULL(u.[DisplayName], al.[ActorEmail]) AS ActorDisplayName,
        al.[EntityType],
        al.[EntityId],
        al.[Action],
        al.[DiffJson],
        al.[IpAddress],
        al.[UserAgent],
        al.[CorrelationId],
        al.[Outcome],
        al.[CreatedAt]
    FROM   [AuditLog] al
    LEFT JOIN [User] u ON u.Id = al.ActorId
    WHERE  (@ActorId    IS NULL OR al.[ActorId]    = @ActorId)
      AND  (@Action     IS NULL OR al.[Action]     = @Action)
      AND  (@EntityType IS NULL OR al.[EntityType] = @EntityType)
      AND  (@FromDate   IS NULL OR al.[CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR al.[CreatedAt] <= @ToDate)
      AND  (@Outcome    IS NULL OR al.[Outcome]    = @Outcome)
      AND  (@IpAddress  IS NULL OR al.[IpAddress]  = @IpAddress)
    ORDER  BY al.[CreatedAt] DESC;
END;
GO

-- ============================================================
-- Archive keeps the full row (§6.3)
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_ArchiveAuditLog]
    @RetentionDays INT = 730
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());

    DECLARE @BatchSize INT = 5000;
    DECLARE @Moved     INT = 1;

    WHILE @Moved > 0
    BEGIN
        INSERT INTO [dbo].[AuditLogArchive]
            ([Id], [ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson],
             [IpAddress], [UserAgent], [CorrelationId], [Outcome], [CreatedAt])
        SELECT TOP (@BatchSize)
            [Id], [ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson],
            [IpAddress], [UserAgent], [CorrelationId], [Outcome], [CreatedAt]
        FROM   [dbo].[AuditLog]
        WHERE  [CreatedAt] < @CutoffDate;

        SET @Moved = @@ROWCOUNT;

        DELETE FROM [dbo].[AuditLog]
        WHERE [Id] IN (
            SELECT TOP (@BatchSize) [Id] FROM [dbo].[AuditLog] WHERE [CreatedAt] < @CutoffDate
        );

        IF @Moved > 0 WAITFOR DELAY '00:00:01';
    END;
END;
GO
