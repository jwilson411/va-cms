-- V049__search_analytics_pii.sql
-- Issue #175 (epic #152): search analytics is not a PII store.
--
--   * usp_Search_LogQuery / usp_Search_LogClick — the API redacts query text before it
--     is queued (SearchQueryRedactor, search.analytics.redactionPatterns). The
--     procedures keep a last-line T-SQL scrub (fn_Search_LooksLikeIdentifier) for the
--     two shapes a LIKE can catch — ###-##-#### and any 9-digit run — so a row written
--     by any other client is still never a bare SSN.
--   * usp_Maint_RollupSearchLogs — retention comes from the site setting
--     search.analytics.retentionDays (default 90) instead of a literal; the rollup is
--     idempotent (every day before today that has no summary row yet), and
--     SearchResultClick is purged on the same schedule (it never was).
--   * vacms_readonly loses SELECT on the two raw tables. SearchQuerySummary stays
--     readable: it is aggregated, redacted and carries no timestamp finer than a day.
--   * One-time scrub of rows written before redaction existed.
--
-- Neither raw table has ever stored a client IP address or session key (V007 / V026);
-- click-through is joined on query text alone. Issue175AcceptanceTests asserts that.

-- ── 0. The shapes T-SQL can recognise ────────────────────────────────────────
-- ###-##-#### and a run of exactly nine digits, each standing as its own word: the
-- LIKE equivalent of \b\d{3}-\d{2}-\d{4}\b and \b\d{9}\b in the API's regex set.
-- Padding supplies the boundary at the start and end of the value.

CREATE OR ALTER FUNCTION [dbo].[fn_Search_LooksLikeIdentifier] (@Query NVARCHAR(500))
RETURNS BIT
WITH SCHEMABINDING
AS
BEGIN
    IF @Query IS NULL RETURN 0;
    DECLARE @Padded NVARCHAR(502) = N' ' + @Query + N' ';
    IF @Padded LIKE '%[^0-9A-Za-z_][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9][0-9][0-9][^0-9A-Za-z_]%'
    OR @Padded LIKE '%[^0-9A-Za-z_][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][^0-9A-Za-z_]%'
        RETURN 1;
    RETURN 0;
END;
GO

-- ── 1. Logging procedures: defensive scrub ───────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_LogQuery]
    @Query       NVARCHAR(500),
    @ResultCount INT,
    @UserId      BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- The API has already run search.analytics.redactionPatterns; this only catches a
    -- writer that bypassed it. Whole-value replacement is deliberate: a LIKE cannot
    -- tell where the match starts, and a partial query is not worth the risk.
    IF dbo.fn_Search_LooksLikeIdentifier(@Query) = 1
        SET @Query = N'[redacted]';

    INSERT INTO [dbo].[SearchQueryLog] ([Query], [ResultCount], [UserId], [CreatedAt])
    VALUES (@Query, @ResultCount, @UserId, SYSUTCDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_LogClick]
    @Query       NVARCHAR(500),
    @ClickedSlug NVARCHAR(500),
    @ResultRank  INT  = 0,
    @UserId      BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF dbo.fn_Search_LooksLikeIdentifier(@Query) = 1
        SET @Query = N'[redacted]';

    INSERT INTO [dbo].[SearchResultClick]
        ([Query], [ClickedSlug], [ResultRank], [UserId], [CreatedAt])
    VALUES
        (@Query, @ClickedSlug, @ResultRank, @UserId, SYSUTCDATETIME());
END;
GO

-- ── 2. Rollup: setting-driven retention, idempotent, covers clicks ───────────

CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_RollupSearchLogs]
AS
BEGIN
    SET NOCOUNT ON;

    -- search.analytics.retentionDays: Value when an admin has set it, else DefaultValue
    -- (refreshed from code at API start-up), else 90. Clamped to the range the setting
    -- documents so a typo cannot purge everything or keep it forever.
    DECLARE @RetentionDays INT;
    SELECT @RetentionDays = TRY_CAST(COALESCE(NULLIF(LTRIM(RTRIM([Value])), N''), [DefaultValue]) AS INT)
    FROM   [dbo].[SiteSetting]
    WHERE  [Key] = N'search.analytics.retentionDays';
    SET @RetentionDays = ISNULL(@RetentionDays, 90);
    IF @RetentionDays < 1    SET @RetentionDays = 1;
    IF @RetentionDays > 3650 SET @RetentionDays = 3650;

    DECLARE @Today  DATE      = CAST(SYSUTCDATETIME() AS DATE);
    DECLARE @Cutoff DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());

    -- Aggregate every completed day that has no summary row yet. Re-running after a
    -- missed night (or twice in one night) adds nothing twice.
    INSERT INTO [dbo].[SearchQuerySummary] ([QueryDate], [Query], [SearchCount], [ZeroResults])
    SELECT d.[QueryDate],
           d.[Query],
           COUNT(*)                                            AS SearchCount,
           SUM(CASE WHEN d.[ResultCount] = 0 THEN 1 ELSE 0 END) AS ZeroResults
    FROM  (SELECT CAST([CreatedAt] AS DATE) AS QueryDate, [Query], [ResultCount]
           FROM   [dbo].[SearchQueryLog]
           WHERE  [CreatedAt] < @Today) d
    WHERE NOT EXISTS (SELECT 1 FROM [dbo].[SearchQuerySummary] s
                      WHERE s.[QueryDate] = d.[QueryDate] AND s.[Query] = d.[Query])
    GROUP BY d.[QueryDate], d.[Query];

    -- Raw rows: only ones already aggregated can be older than the cutoff (>= 1 day).
    DELETE FROM [dbo].[SearchQueryLog]    WHERE [CreatedAt] < @Cutoff;
    DELETE FROM [dbo].[SearchResultClick] WHERE [CreatedAt] < @Cutoff;
END;
GO

-- ── 3. Reporting login: no raw query text ────────────────────────────────────

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_readonly')
BEGIN
    DENY SELECT ON OBJECT::[dbo].[SearchQueryLog]    TO [vacms_readonly];
    DENY SELECT ON OBJECT::[dbo].[SearchResultClick] TO [vacms_readonly];
END
ELSE
    PRINT N'V049: login vacms_readonly not provisioned — SearchQueryLog / SearchResultClick DENY will be applied by infra/sql/provision-logins.sql.';
GO

-- ── 4. Rows written before redaction existed ─────────────────────────────────
-- Best effort with the shapes T-SQL can recognise; the API's regex set is the real
-- control from here on. Summary rows are removed rather than rewritten because
-- (QueryDate, Query) is unique and several PII queries per day would collide.

UPDATE [dbo].[SearchQueryLog]
SET    [Query] = N'[redacted]'
WHERE  dbo.fn_Search_LooksLikeIdentifier([Query]) = 1
   OR  N' ' + [Query] + N' ' LIKE '%[^0-9A-Za-z_][0-9][0-9][0-9][-. ][0-9][0-9][0-9][-. ][0-9][0-9][0-9][0-9][^0-9A-Za-z_]%'
   OR  [Query] LIKE '%[A-Za-z0-9._%+-]@[A-Za-z0-9-]%.[A-Za-z]%';

UPDATE [dbo].[SearchResultClick]
SET    [Query] = N'[redacted]'
WHERE  dbo.fn_Search_LooksLikeIdentifier([Query]) = 1
   OR  N' ' + [Query] + N' ' LIKE '%[^0-9A-Za-z_][0-9][0-9][0-9][-. ][0-9][0-9][0-9][-. ][0-9][0-9][0-9][0-9][^0-9A-Za-z_]%'
   OR  [Query] LIKE '%[A-Za-z0-9._%+-]@[A-Za-z0-9-]%.[A-Za-z]%';

DELETE FROM [dbo].[SearchQuerySummary]
WHERE  dbo.fn_Search_LooksLikeIdentifier([Query]) = 1
   OR  N' ' + [Query] + N' ' LIKE '%[^0-9A-Za-z_][0-9][0-9][0-9][-. ][0-9][0-9][0-9][-. ][0-9][0-9][0-9][0-9][^0-9A-Za-z_]%'
   OR  [Query] LIKE '%[A-Za-z0-9._%+-]@[A-Za-z0-9-]%.[A-Za-z]%';
GO
