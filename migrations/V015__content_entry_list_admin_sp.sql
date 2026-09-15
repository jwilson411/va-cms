-- V015__content_entry_list_admin_sp.sql
-- Stored procedure for the admin content entry list screen (issue #29).
-- Returns: Id, Slug, Status, ContentTypeId, ContentTypeName, OwnerId, AuthorDisplayName,
--          UpdatedAt, Title (extracted from FieldsJson of the latest version), TotalRows OUTPUT.
-- All filters are optional; sort is controlled by @SortBy / @SortDir.

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_ListAdmin]
    @ContentTypeId BIGINT        = NULL,
    @Status        NVARCHAR(20)  = NULL,
    @AuthorSearch  NVARCHAR(200) = NULL,   -- matched against User.DisplayName or User.Email LIKE
    @DateFrom      DATETIME2     = NULL,
    @DateTo        DATETIME2     = NULL,
    @SortBy        NVARCHAR(20)  = 'UpdatedAt',  -- 'Title' | 'Status' | 'UpdatedAt'
    @SortDir       NVARCHAR(4)   = 'DESC',        -- 'ASC' | 'DESC'
    @Page          INT           = 1,
    @PageSize      INT           = 25,
    @TotalRows     INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Clamp to safe sort column to avoid injection via ORDER BY
    IF @SortBy NOT IN ('Title', 'Status', 'UpdatedAt')
        SET @SortBy = 'UpdatedAt';
    IF @SortDir NOT IN ('ASC', 'DESC')
        SET @SortDir = 'DESC';

    -- Count query
    SELECT @TotalRows = COUNT(*)
    FROM   [ContentEntry] e
    JOIN   [ContentType]  ct ON ct.Id = e.ContentTypeId
    JOIN   [User]         u  ON u.Id  = e.OwnerId
    WHERE  e.Status != 'Archived'
      AND  (@ContentTypeId IS NULL OR e.ContentTypeId = @ContentTypeId)
      AND  (@Status        IS NULL OR e.Status        = @Status)
      AND  (@AuthorSearch  IS NULL
            OR u.DisplayName LIKE '%' + @AuthorSearch + '%'
            OR u.Email       LIKE '%' + @AuthorSearch + '%')
      AND  (@DateFrom      IS NULL OR e.UpdatedAt >= @DateFrom)
      AND  (@DateTo        IS NULL OR e.UpdatedAt <= @DateTo);

    -- Pull the "title" field value from the most recent ContentVersion for each entry.
    -- The FieldsJson is a JSON object; we use JSON_VALUE to extract fields.title.
    -- Falls back to the entry Slug if no version exists or title field is absent.
    WITH LatestVersion AS (
        SELECT v.ContentEntryId,
               ISNULL(
                   JSON_VALUE(v.FieldsJson, '$.title'),
                   e2.Slug
               ) AS Title
        FROM   [ContentVersion] v
        JOIN   [ContentEntry]   e2 ON e2.Id = v.ContentEntryId
        WHERE  v.Id = (
            SELECT TOP 1 vv.Id
            FROM   [ContentVersion] vv
            WHERE  vv.ContentEntryId = v.ContentEntryId
            ORDER  BY vv.VersionNumber DESC
        )
    ),
    Filtered AS (
        SELECT
            e.Id,
            e.Slug,
            e.Status,
            e.ContentTypeId,
            ct.DisplayName   AS ContentTypeName,
            e.OwnerId,
            u.DisplayName    AS AuthorDisplayName,
            e.UpdatedAt,
            ISNULL(lv.Title, e.Slug) AS Title
        FROM   [ContentEntry] e
        JOIN   [ContentType]  ct ON ct.Id = e.ContentTypeId
        JOIN   [User]         u  ON u.Id  = e.OwnerId
        LEFT JOIN LatestVersion lv ON lv.ContentEntryId = e.Id
        WHERE  e.Status != 'Archived'
          AND  (@ContentTypeId IS NULL OR e.ContentTypeId = @ContentTypeId)
          AND  (@Status        IS NULL OR e.Status        = @Status)
          AND  (@AuthorSearch  IS NULL
                OR u.DisplayName LIKE '%' + @AuthorSearch + '%'
                OR u.Email       LIKE '%' + @AuthorSearch + '%')
          AND  (@DateFrom      IS NULL OR e.UpdatedAt >= @DateFrom)
          AND  (@DateTo        IS NULL OR e.UpdatedAt <= @DateTo)
    ),
    Ranked AS (
        SELECT *,
               ROW_NUMBER() OVER (
                   ORDER BY
                       CASE WHEN @SortBy = 'Title'     AND @SortDir = 'ASC'  THEN Title     END ASC,
                       CASE WHEN @SortBy = 'Title'     AND @SortDir = 'DESC' THEN Title     END DESC,
                       CASE WHEN @SortBy = 'Status'    AND @SortDir = 'ASC'  THEN Status    END ASC,
                       CASE WHEN @SortBy = 'Status'    AND @SortDir = 'DESC' THEN Status    END DESC,
                       CASE WHEN @SortBy = 'UpdatedAt' AND @SortDir = 'ASC'  THEN UpdatedAt END ASC,
                       CASE WHEN @SortBy = 'UpdatedAt' AND @SortDir = 'DESC' THEN UpdatedAt END DESC
               ) AS RowNum
        FROM Filtered
    )
    SELECT Id, Slug, Status, ContentTypeId, ContentTypeName,
           OwnerId, AuthorDisplayName, UpdatedAt, Title
    FROM   Ranked
    WHERE  RowNum BETWEEN ((@Page - 1) * @PageSize + 1) AND (@Page * @PageSize)
    ORDER  BY RowNum;
END;
GO
