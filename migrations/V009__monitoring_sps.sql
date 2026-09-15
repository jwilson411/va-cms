-- V009__monitoring_sps.sql
-- Monitoring stored procedures exposed as health dashboard endpoints.
-- See DATABASE_LAYER.md §6.7

CREATE OR ALTER PROCEDURE [dbo].[usp_Monitor_IndexFragmentation]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        OBJECT_NAME(ips.object_id)      AS TableName,
        i.name                          AS IndexName,
        ips.avg_fragmentation_in_percent AS FragmentationPct,
        ips.page_count                  AS PageCount
    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
    JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
    WHERE ips.index_id > 0
      AND ips.page_count > 50
    ORDER BY ips.avg_fragmentation_in_percent DESC;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Monitor_TableSizes]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        t.name                         AS TableName,
        p.rows                         AS [RowCount],
        SUM(a.total_pages) * 8 / 1024  AS TotalSizeMB,
        SUM(a.used_pages)  * 8 / 1024  AS UsedSizeMB
    FROM sys.tables t
    JOIN sys.indexes i       ON t.object_id = i.object_id
    JOIN sys.partitions p    ON i.object_id = p.object_id AND i.index_id = p.index_id
    JOIN sys.allocation_units a ON p.partition_id = a.container_id
    WHERE t.is_ms_shipped = 0 AND i.index_id IN (0, 1)
    GROUP BY t.name, p.rows
    ORDER BY SUM(a.total_pages) DESC;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Monitor_LongRunningQueries]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 20
        r.session_id,
        r.status,
        r.start_time,
        DATEDIFF(SECOND, r.start_time, SYSUTCDATETIME()) AS DurationSec,
        r.command,
        SUBSTRING(st.text, (r.statement_start_offset/2)+1,
            ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(st.text)
              ELSE r.statement_end_offset END - r.statement_start_offset)/2)+1) AS QueryText,
        r.wait_type,
        r.blocking_session_id
    FROM sys.dm_exec_requests r
    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) st
    WHERE DATEDIFF(SECOND, r.start_time, SYSUTCDATETIME()) > 5
      AND r.session_id <> @@SPID
    ORDER BY DurationSec DESC;
END;
GO
