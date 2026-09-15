-- V024__search_sp_with_title_summary.sql
-- Enhances usp_Search_FullText to return Title, Summary, ContentTypeName, and PublishedAt.
-- These fields are required by issue #49 acceptance criteria:
--   "Results include: title, content type, slug, summary excerpt, published date"
-- Title and Summary are extracted from v.FieldsJson via JSON_VALUE.
-- ContentTypeName comes from a join to [ContentType].
-- PublishedAt is e.UpdatedAt (the last time the entry transitioned to Published).
--
-- Safe to re-run: uses CREATE OR ALTER.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_FullText]
    @Query         NVARCHAR(500),
    @ContentTypeId BIGINT       = NULL,
    @Page          INT          = 1,
    @PageSize      INT          = 25,
    @TotalRows     INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- This SP requires SQL Server Full-Text Search (FTS) to be installed and V002 to have run.
    -- When FTS is not available, returns an empty result set with TotalRows = 0.
    SET @TotalRows = 0;

    IF CAST(ISNULL(SERVERPROPERTY('IsFullTextInstalled'), 0) AS BIT) = 0
    BEGIN
        PRINT 'usp_Search_FullText: Full-Text Search is not installed. Returning empty result.';
        RETURN;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('[dbo].[ContentVersion]')
          AND name = 'FieldsPlainText'
    )
    BEGIN
        PRINT 'usp_Search_FullText: FieldsPlainText column not found. Run V002 migration first.';
        RETURN;
    END;

    -- Sanitize query: wrap in double-quotes for exact-phrase FTS unless already an FTS expression
    DECLARE @FtsQuery NVARCHAR(600) = '"' + REPLACE(@Query, '"', '') + '"';
    DECLARE @SQL      NVARCHAR(MAX);

    -- Count total matching rows
    SET @SQL = N'
        SELECT @TotalRows = COUNT(DISTINCT e.[Id])
        FROM   [dbo].[ContentEntry]  e
        JOIN   [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
        WHERE  e.[Status] = ''Published''
          AND  CONTAINS(v.[FieldsPlainText], @FtsQuery)
          AND  (@ContentTypeId IS NULL OR e.[ContentTypeId] = @ContentTypeId);';
    EXEC sp_executesql @SQL,
        N'@FtsQuery NVARCHAR(600), @ContentTypeId BIGINT, @TotalRows INT OUTPUT',
        @FtsQuery, @ContentTypeId, @TotalRows OUTPUT;

    -- Return ranked page with title, summary, content type name, and published date
    SET @SQL = N'
        SELECT
            e.[Id],
            e.[Slug],
            e.[ContentTypeId],
            ct.[DisplayName]                               AS ContentTypeName,
            e.[Locale],
            e.[UpdatedAt]                                  AS PublishedAt,
            JSON_VALUE(v.[FieldsJson], ''$.title'')        AS Title,
            COALESCE(
                JSON_VALUE(v.[FieldsJson], ''$.summary''),
                LEFT(v.[FieldsPlainText], 300)
            )                                              AS Excerpt,
            kt.[RANK]
        FROM   [dbo].[ContentEntry] e
        JOIN   [dbo].[ContentVersion] v   ON v.[Id] = e.[PublishedVersionId]
        JOIN   [dbo].[ContentType]    ct  ON ct.[Id] = e.[ContentTypeId]
        JOIN   CONTAINSTABLE([dbo].[ContentVersion], [FieldsPlainText], @FtsQuery) kt
                   ON kt.[KEY] = v.[Id]
        WHERE  e.[Status] = ''Published''
          AND  (@ContentTypeId IS NULL OR e.[ContentTypeId] = @ContentTypeId)
        ORDER  BY kt.[RANK] DESC
        OFFSET (@Page - 1) * @PageSize ROWS
        FETCH  NEXT @PageSize ROWS ONLY;';
    EXEC sp_executesql @SQL,
        N'@FtsQuery NVARCHAR(600), @ContentTypeId BIGINT, @Page INT, @PageSize INT',
        @FtsQuery, @ContentTypeId, @Page, @PageSize;
END;
GO
