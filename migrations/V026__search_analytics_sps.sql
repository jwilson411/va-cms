-- V026__search_analytics_sps.sql
-- Issue #51 — Build search analytics dashboard widget in admin.
-- BRD FR-SEARCH-06.
--
-- Adds:
--   1. SearchResultClick table — click-through tracking per search result.
--   2. usp_Search_LogClick — log a click event.
--   3. usp_Search_GetTopQueries — top N queries over the last 30 days.
--   4. usp_Search_GetZeroResultQueries — top N zero-result queries (last 30 days).
--   5. usp_Search_GetAnalyticsFull — full paginated analytics table.

-- ── 1. SearchResultClick table ────────────────────────────────────────────────

CREATE TABLE [dbo].[SearchResultClick] (
    [Id]            BIGINT IDENTITY(1,1) NOT NULL,
    [Query]         NVARCHAR(500)        NOT NULL,
    [ClickedSlug]   NVARCHAR(500)        NOT NULL,
    [ResultRank]    INT                  NOT NULL DEFAULT 0,
    [UserId]        BIGINT               NULL,
    [CreatedAt]     DATETIME2            NOT NULL CONSTRAINT [DF_SearchResultClick_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SearchResultClick] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE INDEX [IX_SearchResultClick_Query_CreatedAt]
    ON [dbo].[SearchResultClick] ([Query], [CreatedAt] DESC);
GO

-- ── 2. usp_Search_LogClick ────────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_LogClick]
    @Query       NVARCHAR(500),
    @ClickedSlug NVARCHAR(500),
    @ResultRank  INT  = 0,
    @UserId      BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[SearchResultClick]
        ([Query], [ClickedSlug], [ResultRank], [UserId], [CreatedAt])
    VALUES
        (@Query, @ClickedSlug, @ResultRank, @UserId, SYSUTCDATETIME());
END;
GO

-- ── 3. usp_Search_GetTopQueries ──────────────────────────────────────────────
-- Returns top N search queries (by search count) over the last 30 days.
-- Joins SearchQueryLog (raw, last 90 days) and SearchQuerySummary (rolled-up historical).
-- For the last 30 days we prefer live SearchQueryLog rows for recency.

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_GetTopQueries]
    @TopN        INT = 10,
    @DaysBack    INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysBack, SYSUTCDATETIME());

    SELECT TOP (@TopN)
        [Query],
        COUNT(*)                                              AS SearchCount,
        SUM(CASE WHEN [ResultCount] = 0 THEN 1 ELSE 0 END)  AS ZeroResultCount,
        CAST(AVG(CAST([ResultCount] AS FLOAT)) AS DECIMAL(10,2)) AS AvgResultCount,
        MAX([CreatedAt])                                     AS LastSearchedAt
    FROM   [dbo].[SearchQueryLog]
    WHERE  [CreatedAt] >= @CutoffDate
    GROUP  BY [Query]
    ORDER  BY SearchCount DESC;
END;
GO

-- ── 4. usp_Search_GetZeroResultQueries ───────────────────────────────────────
-- Returns top N queries that returned zero results over the last 30 days.

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_GetZeroResultQueries]
    @TopN        INT = 10,
    @DaysBack    INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysBack, SYSUTCDATETIME());

    SELECT TOP (@TopN)
        [Query],
        COUNT(*) AS ZeroResultCount,
        MAX([CreatedAt]) AS LastSearchedAt
    FROM   [dbo].[SearchQueryLog]
    WHERE  [CreatedAt] >= @CutoffDate
      AND  [ResultCount] = 0
    GROUP  BY [Query]
    ORDER  BY ZeroResultCount DESC;
END;
GO

-- ── 5. usp_Search_GetAnalyticsFull ───────────────────────────────────────────
-- Full paginated analytics table: all queries with counts, zero-result flag, CTR.
-- Used by /admin/search/analytics full page.

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_GetAnalyticsFull]
    @DaysBack    INT = 30,
    @Page        INT = 1,
    @PageSize    INT = 50,
    @TotalRows   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysBack, SYSUTCDATETIME());

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
    )
    SELECT *
    FROM   Combined
    ORDER  BY SearchCount DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO
