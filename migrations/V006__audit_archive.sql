-- V006__audit_archive.sql
-- AuditLog archive table and batch-archival stored procedure.
-- See DATABASE_LAYER.md §6.3

CREATE TABLE [dbo].[AuditLogArchive] (
    [Id]         BIGINT        NOT NULL,
    [ActorId]    BIGINT        NULL,
    [ActorEmail] NVARCHAR(500) NULL,
    [EntityType] NVARCHAR(100) NOT NULL,
    [EntityId]   NVARCHAR(100) NOT NULL,
    [Action]     NVARCHAR(50)  NOT NULL,
    [DiffJson]   NVARCHAR(MAX) NULL,
    [CreatedAt]  DATETIME2     NOT NULL,
    [ArchivedAt] DATETIME2     NOT NULL CONSTRAINT [DF_AuditLogArchive_ArchivedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AuditLogArchive] PRIMARY KEY NONCLUSTERED ([Id] ASC)
);
CREATE CLUSTERED INDEX [CX_AuditLogArchive_CreatedAt] ON [dbo].[AuditLogArchive] ([CreatedAt]);
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_ArchiveAuditLog]
    @RetentionDays INT = 730   -- 2 years live; archived rows kept per DBA policy
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());

    DECLARE @BatchSize INT = 5000;
    DECLARE @Moved     INT = 1;

    WHILE @Moved > 0
    BEGIN
        INSERT INTO [dbo].[AuditLogArchive]
            ([Id], [ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
        SELECT TOP (@BatchSize)
            [Id], [ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt]
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
