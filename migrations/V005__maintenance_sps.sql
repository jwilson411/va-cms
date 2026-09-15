-- V005__maintenance_sps.sql
-- Maintenance stored procedures.
-- See DATABASE_LAYER.md §6.1, §6.2, §6.4, §6.5

-- ---------------------------------------------------------------------------
-- Statistics maintenance
-- Full scan for small tables; 30% sample for AuditLog (grows large).
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_UpdateStatistics]
AS
BEGIN
    SET NOCOUNT ON;
    -- Full scan for critical tables (fast — data fits in memory)
    UPDATE STATISTICS [dbo].[ContentEntry]   WITH FULLSCAN;
    UPDATE STATISTICS [dbo].[ContentVersion] WITH FULLSCAN;
    UPDATE STATISTICS [dbo].[MediaAsset]     WITH FULLSCAN;
    UPDATE STATISTICS [dbo].[AuditLog]       WITH SAMPLE 30 PERCENT;
    UPDATE STATISTICS [dbo].[SearchQueryLog] WITH SAMPLE 30 PERCENT;
    -- Remaining tables: default sample rate
    UPDATE STATISTICS [dbo].[User];
    UPDATE STATISTICS [dbo].[NavigationItem];
    UPDATE STATISTICS [dbo].[TaxonomyTerm];
    UPDATE STATISTICS [dbo].[ContentEntryTerm];
    UPDATE STATISTICS [dbo].[WorkflowTransition];
    UPDATE STATISTICS [dbo].[Webhook];
    UPDATE STATISTICS [dbo].[WebhookDelivery];
    UPDATE STATISTICS [dbo].[Redirect];
END;
GO

-- ---------------------------------------------------------------------------
-- Index rebuild / reorganize based on fragmentation.
-- >30% frag = REBUILD WITH ONLINE ON; 10-30% = REORGANIZE.
-- Outputs a result set: TableName, IndexName, FragmentationPct, Action taken.
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_RebuildIndexes]
    @FragmentationThresholdRebuild FLOAT = 30.0,
    @FragmentationThresholdReorg   FLOAT = 10.0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TableName NVARCHAR(256);
    DECLARE @IndexName NVARCHAR(256);
    DECLARE @Frag      FLOAT;
    DECLARE @SQL       NVARCHAR(MAX);
    DECLARE @Action    NVARCHAR(20);

    -- Collect results for output
    CREATE TABLE #IndexActions (
        TableName       NVARCHAR(256) NOT NULL,
        IndexName       NVARCHAR(256) NOT NULL,
        FragmentationPct FLOAT        NOT NULL,
        Action          NVARCHAR(20)  NOT NULL
    );

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            OBJECT_NAME(ips.object_id)       AS TableName,
            i.name                           AS IndexName,
            ips.avg_fragmentation_in_percent AS Fragmentation
        FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
        JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
        WHERE ips.index_id > 0                       -- skip heaps
          AND i.name IS NOT NULL                     -- skip unnamed system indexes
          AND ips.avg_fragmentation_in_percent > @FragmentationThresholdReorg
          AND ips.page_count > 100                   -- ignore tiny indexes
        ORDER BY ips.avg_fragmentation_in_percent DESC;

    OPEN cur;
    FETCH NEXT FROM cur INTO @TableName, @IndexName, @Frag;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @Frag >= @FragmentationThresholdRebuild
        BEGIN
            SET @SQL    = N'ALTER INDEX [' + @IndexName + N'] ON [dbo].[' + @TableName + N'] REBUILD WITH (ONLINE = ON);';
            SET @Action = N'REBUILD';
        END
        ELSE
        BEGIN
            SET @SQL    = N'ALTER INDEX [' + @IndexName + N'] ON [dbo].[' + @TableName + N'] REORGANIZE;';
            SET @Action = N'REORGANIZE';
        END;

        EXEC sp_executesql @SQL;

        INSERT INTO #IndexActions (TableName, IndexName, FragmentationPct, Action)
        VALUES (@TableName, @IndexName, @Frag, @Action);

        FETCH NEXT FROM cur INTO @TableName, @IndexName, @Frag;
    END;

    CLOSE cur;
    DEALLOCATE cur;

    -- Return result set showing what was done
    SELECT TableName, IndexName, FragmentationPct, Action FROM #IndexActions ORDER BY FragmentationPct DESC;

    DROP TABLE #IndexActions;

    -- Update stats after rebuild
    EXEC [dbo].[usp_Maint_UpdateStatistics];
END;
GO

-- ---------------------------------------------------------------------------
-- AuditLog batch archival.
-- Defined in V005 for completeness; AuditLogArchive table created in V006.
-- ---------------------------------------------------------------------------
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

-- ---------------------------------------------------------------------------
-- Webhook delivery log purge.
-- Purges successful deliveries older than @RetentionDays (default 30).
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_PurgeWebhookDeliveries]
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[WebhookDelivery]
    WHERE [DeliveredAt] < DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME())
      AND [ResponseStatusCode] BETWEEN 200 AND 299;  -- only purge successful deliveries
END;
GO

-- ---------------------------------------------------------------------------
-- Search query log rollup.
-- Aggregates yesterday's SearchQueryLog rows into SearchQuerySummary,
-- then purges raw logs older than 90 days.
-- Note: SearchQueryLog and SearchQuerySummary tables are created in V007.
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_RollupSearchLogs]
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Yesterday DATE = DATEADD(DAY, -1, CAST(SYSUTCDATETIME() AS DATE));

    INSERT INTO [dbo].[SearchQuerySummary] ([QueryDate], [Query], [SearchCount], [ZeroResults])
    SELECT @Yesterday,
           [Query],
           COUNT(*)                                              AS SearchCount,
           SUM(CASE WHEN [ResultCount] = 0 THEN 1 ELSE 0 END)  AS ZeroResults
    FROM   [dbo].[SearchQueryLog]
    WHERE  CAST([CreatedAt] AS DATE) = @Yesterday
    GROUP  BY [Query];

    DELETE FROM [dbo].[SearchQueryLog]
    WHERE [CreatedAt] < DATEADD(DAY, -90, SYSUTCDATETIME());
END;
GO
