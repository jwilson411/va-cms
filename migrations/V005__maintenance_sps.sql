-- V005__maintenance_sps.sql
-- Maintenance stored procedures: statistics update and index rebuild.
-- See DATABASE_LAYER.md §6.1 and §6.2

-- ---------------------------------------------------------------------------
-- Statistics maintenance
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
-- Index rebuild / reorganize based on fragmentation
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

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            OBJECT_NAME(ips.object_id)      AS TableName,
            i.name                          AS IndexName,
            ips.avg_fragmentation_in_percent AS Fragmentation
        FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
        JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
        WHERE ips.index_id > 0
          AND ips.avg_fragmentation_in_percent > @FragmentationThresholdReorg
          AND ips.page_count > 100
        ORDER BY ips.avg_fragmentation_in_percent DESC;

    OPEN cur;
    FETCH NEXT FROM cur INTO @TableName, @IndexName, @Frag;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @Frag >= @FragmentationThresholdRebuild
            SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [dbo].[' + @TableName + N'] REBUILD WITH (ONLINE = ON);';
        ELSE
            SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [dbo].[' + @TableName + N'] REORGANIZE;';

        EXEC sp_executesql @SQL;
        FETCH NEXT FROM cur INTO @TableName, @IndexName, @Frag;
    END;

    CLOSE cur;
    DEALLOCATE cur;

    -- Update stats after rebuild
    EXEC [dbo].[usp_Maint_UpdateStatistics];
END;
GO

-- ---------------------------------------------------------------------------
-- Webhook delivery log purge
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_PurgeWebhookDeliveries]
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[WebhookDelivery]
    WHERE [DeliveredAt] < DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME())
      AND [ResponseStatusCode] BETWEEN 200 AND 299;
END;
GO
