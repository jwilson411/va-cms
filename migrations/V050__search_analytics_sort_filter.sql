-- V050__search_analytics_sort_filter.sql
-- Adds server-side column sorting and a query-text filter to the full search
-- analytics table (admin page /admin/search/analytics), so it stays usable as
-- SearchQueryLog/SearchResultClick grow. Previously hardcoded to
-- ORDER BY SearchCount DESC with no filter.
--
-- Follows the same safe dynamic-sort idiom as usp_ContentEntry_ListAdmin
-- (V015): whitelist @SortBy/@SortDir, then OR them into CASE WHEN ORDER BY
-- terms — never string-concatenated into dynamic SQL.

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_GetAnalyticsFull]
    @DaysBack     INT           = 30,
    @Page         INT           = 1,
    @PageSize     INT           = 50,
    @SortBy       NVARCHAR(20)  = 'SearchCount',  -- 'Query' | 'SearchCount' | 'ZeroResultCount' |
                                                   -- 'AvgResultCount' | 'ClickCount' |
                                                   -- 'ClickThroughRate' | 'LastSearchedAt'
    @SortDir      NVARCHAR(4)   = 'DESC',         -- 'ASC' | 'DESC'
    @QueryFilter  NVARCHAR(200) = NULL,           -- substring match against Query
    @TotalRows    INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysBack, SYSUTCDATETIME());

    -- Clamp to safe sort column to avoid injection via ORDER BY
    IF @SortBy NOT IN ('Query', 'SearchCount', 'ZeroResultCount', 'AvgResultCount',
                       'ClickCount', 'ClickThroughRate', 'LastSearchedAt')
        SET @SortBy = 'SearchCount';
    IF @SortDir NOT IN ('ASC', 'DESC')
        SET @SortDir = 'DESC';

    -- Aggregate raw query log
    WITH Agg AS (
        SELECT
            sql_log.[Query],
            COUNT(*)                                              AS SearchCount,
            SUM(CASE WHEN sql_log.[ResultCount] = 0 THEN 1 ELSE 0 END) AS ZeroResultCount,
            CAST(AVG(CAST(sql_log.[ResultCount] AS FLOAT)) AS DECIMAL(10,2)) AS AvgResultCount,
            MAX(sql_log.[CreatedAt])                             AS LastSearchedAt
        FROM   [dbo].[SearchQueryLog] sql_log
        WHERE  sql_log.[CreatedAt] >= @CutoffDate
        GROUP  BY sql_log.[Query]
    ),
    -- Click data for same period
    Clicks AS (
        SELECT
            src.[Query],
            COUNT(*) AS ClickCount
        FROM   [dbo].[SearchResultClick] src
        WHERE  src.[CreatedAt] >= @CutoffDate
        GROUP  BY src.[Query]
    ),
    Combined AS (
        SELECT
            a.[Query],
            a.SearchCount,
            a.ZeroResultCount,
            a.AvgResultCount,
            a.LastSearchedAt,
            ISNULL(c.ClickCount, 0) AS ClickCount,
            -- CTR: clicks / total searches, as percentage
            CASE WHEN a.SearchCount > 0
                 THEN CAST(ISNULL(c.ClickCount, 0) * 100.0 / a.SearchCount AS DECIMAL(5,2))
                 ELSE 0.00
            END AS ClickThroughRate
        FROM   Agg a
        LEFT JOIN Clicks c ON c.[Query] = a.[Query]
        WHERE  @QueryFilter IS NULL OR a.[Query] LIKE '%' + @QueryFilter + '%'
    )
    SELECT @TotalRows = COUNT(*) FROM Combined;

    WITH Agg AS (
        SELECT
            sql_log.[Query],
            COUNT(*)                                              AS SearchCount,
            SUM(CASE WHEN sql_log.[ResultCount] = 0 THEN 1 ELSE 0 END) AS ZeroResultCount,
            CAST(AVG(CAST(sql_log.[ResultCount] AS FLOAT)) AS DECIMAL(10,2)) AS AvgResultCount,
            MAX(sql_log.[CreatedAt])                             AS LastSearchedAt
        FROM   [dbo].[SearchQueryLog] sql_log
        WHERE  sql_log.[CreatedAt] >= @CutoffDate
        GROUP  BY sql_log.[Query]
    ),
    Clicks AS (
        SELECT
            src.[Query],
            COUNT(*) AS ClickCount
        FROM   [dbo].[SearchResultClick] src
        WHERE  src.[CreatedAt] >= @CutoffDate
        GROUP  BY src.[Query]
    ),
    Combined AS (
        SELECT
            a.[Query],
            a.SearchCount,
            a.ZeroResultCount,
            a.AvgResultCount,
            a.LastSearchedAt,
            ISNULL(c.ClickCount, 0) AS ClickCount,
            CASE WHEN a.SearchCount > 0
                 THEN CAST(ISNULL(c.ClickCount, 0) * 100.0 / a.SearchCount AS DECIMAL(5,2))
                 ELSE 0.00
            END AS ClickThroughRate
        FROM   Agg a
        LEFT JOIN Clicks c ON c.[Query] = a.[Query]
        WHERE  @QueryFilter IS NULL OR a.[Query] LIKE '%' + @QueryFilter + '%'
    )
    SELECT *
    FROM   Combined
    ORDER  BY
        CASE WHEN @SortBy = 'Query'             AND @SortDir = 'ASC'  THEN [Query]            END ASC,
        CASE WHEN @SortBy = 'Query'             AND @SortDir = 'DESC' THEN [Query]            END DESC,
        CASE WHEN @SortBy = 'SearchCount'       AND @SortDir = 'ASC'  THEN SearchCount        END ASC,
        CASE WHEN @SortBy = 'SearchCount'       AND @SortDir = 'DESC' THEN SearchCount        END DESC,
        CASE WHEN @SortBy = 'ZeroResultCount'   AND @SortDir = 'ASC'  THEN ZeroResultCount    END ASC,
        CASE WHEN @SortBy = 'ZeroResultCount'   AND @SortDir = 'DESC' THEN ZeroResultCount    END DESC,
        CASE WHEN @SortBy = 'AvgResultCount'    AND @SortDir = 'ASC'  THEN AvgResultCount     END ASC,
        CASE WHEN @SortBy = 'AvgResultCount'    AND @SortDir = 'DESC' THEN AvgResultCount     END DESC,
        CASE WHEN @SortBy = 'ClickCount'        AND @SortDir = 'ASC'  THEN ClickCount         END ASC,
        CASE WHEN @SortBy = 'ClickCount'        AND @SortDir = 'DESC' THEN ClickCount         END DESC,
        CASE WHEN @SortBy = 'ClickThroughRate'  AND @SortDir = 'ASC'  THEN ClickThroughRate   END ASC,
        CASE WHEN @SortBy = 'ClickThroughRate'  AND @SortDir = 'DESC' THEN ClickThroughRate   END DESC,
        CASE WHEN @SortBy = 'LastSearchedAt'    AND @SortDir = 'ASC'  THEN LastSearchedAt     END ASC,
        CASE WHEN @SortBy = 'LastSearchedAt'    AND @SortDir = 'DESC' THEN LastSearchedAt     END DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO
