-- V030__audit_log_viewer_sps.sql
-- Issue #57 — Build audit log viewer in admin (BRD FR-USERS-06)
--
-- Adds two new stored procedures:
--   usp_AuditLog_ListPaged  — filtered, paged list with total count and actor email
--   usp_AuditLog_ExportCsv  — same filters, no paging (returns up to 1000 rows for CSV export)
--
-- The existing usp_AuditLog_List is kept unchanged for backward compatibility.

-- ── usp_AuditLog_ListPaged ────────────────────────────────────────────────────
-- Returns audit rows newest-first with TotalRows OUTPUT for pagination.
-- All filter parameters are optional and AND-combined when provided.
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_ListPaged]
    @ActorId    BIGINT        = NULL,
    @Action     NVARCHAR(50)  = NULL,
    @EntityType NVARCHAR(100) = NULL,
    @FromDate   DATETIME2     = NULL,
    @ToDate     DATETIME2     = NULL,
    @Page       INT           = 1,
    @PageSize   INT           = 50,
    @TotalRows  INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Capture total count first
    SELECT @TotalRows = COUNT(*)
    FROM   [AuditLog]
    WHERE  (@ActorId    IS NULL OR [ActorId]    = @ActorId)
      AND  (@Action     IS NULL OR [Action]     = @Action)
      AND  (@EntityType IS NULL OR [EntityType] = @EntityType)
      AND  (@FromDate   IS NULL OR [CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR [CreatedAt] <= @ToDate);

    -- Return paged rows
    SELECT
        al.[Id],
        al.[ActorId],
        al.[ActorEmail],
        al.[EntityType],
        al.[EntityId],
        al.[Action],
        al.[DiffJson],
        al.[CreatedAt],
        ISNULL(u.[DisplayName], al.[ActorEmail]) AS ActorDisplayName
    FROM   [AuditLog] al
    LEFT JOIN [User] u ON u.Id = al.ActorId
    WHERE  (@ActorId    IS NULL OR al.[ActorId]    = @ActorId)
      AND  (@Action     IS NULL OR al.[Action]     = @Action)
      AND  (@EntityType IS NULL OR al.[EntityType] = @EntityType)
      AND  (@FromDate   IS NULL OR al.[CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR al.[CreatedAt] <= @ToDate)
    ORDER  BY al.[CreatedAt] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- ── usp_AuditLog_ExportCsv ────────────────────────────────────────────────────
-- Same filters as ListPaged but without paging — returns up to 1000 rows.
-- Used by the CSV export endpoint.
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_ExportCsv]
    @ActorId    BIGINT        = NULL,
    @Action     NVARCHAR(50)  = NULL,
    @EntityType NVARCHAR(100) = NULL,
    @FromDate   DATETIME2     = NULL,
    @ToDate     DATETIME2     = NULL
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
        al.[CreatedAt]
    FROM   [AuditLog] al
    LEFT JOIN [User] u ON u.Id = al.ActorId
    WHERE  (@ActorId    IS NULL OR al.[ActorId]    = @ActorId)
      AND  (@Action     IS NULL OR al.[Action]     = @Action)
      AND  (@EntityType IS NULL OR al.[EntityType] = @EntityType)
      AND  (@FromDate   IS NULL OR al.[CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR al.[CreatedAt] <= @ToDate)
    ORDER  BY al.[CreatedAt] DESC;
END;
GO
