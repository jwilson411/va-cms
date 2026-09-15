-- V002__add_fts_catalog.sql
-- Full-text search infrastructure: catalog, computed column, FTS indexes.
-- Guarded by SERVERPROPERTY('IsFullTextInstalled') so migrations run cleanly on
-- SQL Server containers that don't ship the FTS feature (e.g. dev Docker image).
-- When FTS IS installed (production), all objects are created normally.
-- See DATABASE_LAYER.md §3.3

-- Required SET options for persisted computed columns
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================
-- Scalar UDF: extract plain text from FieldsJson for FTS indexing.
-- Always created — no FTS dependency on the UDF itself.
-- ============================================================
CREATE OR ALTER FUNCTION [dbo].[fn_ExtractPlainText]
(
    @Json NVARCHAR(MAX)
)
RETURNS NVARCHAR(MAX)
WITH SCHEMABINDING
AS
BEGIN
    -- Strip JSON structural characters so FTS indexes field values, not syntax.
    DECLARE @Result NVARCHAR(MAX) = @Json;
    SET @Result = REPLACE(@Result, '{', ' ');
    SET @Result = REPLACE(@Result, '}', ' ');
    SET @Result = REPLACE(@Result, '[', ' ');
    SET @Result = REPLACE(@Result, ']', ' ');
    SET @Result = REPLACE(@Result, '"', ' ');
    SET @Result = REPLACE(@Result, ':', ' ');
    SET @Result = REPLACE(@Result, ',', ' ');
    RETURN @Result;
END;
GO

-- ============================================================
-- FTS catalog + computed column + FTS indexes —
-- only when the Full-Text Search feature is installed.
-- SERVERPROPERTY is safe to call regardless of FTS presence.
-- ============================================================
DECLARE @FtsInstalled BIT = CAST(ISNULL(SERVERPROPERTY('IsFullTextInstalled'), 0) AS BIT);

IF @FtsInstalled = 0
BEGIN
    PRINT 'V002: Full-Text Search is not installed on this SQL Server instance. FTS objects will be skipped.';
    PRINT 'V002: Install the Full-Text Search feature and rerun this migration to enable FTS.';
END;
ELSE
BEGIN
    -- Full-text catalog
    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE [name] = 'FTC_CmsContent')
    BEGIN
        EXEC('CREATE FULLTEXT CATALOG [FTC_CmsContent] AS DEFAULT');
    END;

    -- Persisted computed column (required by FTS index; depends on UDF above)
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('[dbo].[ContentVersion]')
          AND [name] = 'FieldsPlainText'
    )
    BEGIN
        ALTER TABLE [dbo].[ContentVersion]
            ADD [FieldsPlainText] AS ([dbo].[fn_ExtractPlainText]([FieldsJson])) PERSISTED;
    END;

    -- FTS indexes
    IF NOT EXISTS (
        SELECT 1 FROM sys.fulltext_indexes fi
        JOIN sys.tables t ON t.object_id = fi.object_id
        WHERE t.[name] = 'ContentVersion'
    )
    BEGIN
        EXEC('
            CREATE FULLTEXT INDEX ON [dbo].[ContentVersion]
                ([FieldsPlainText] LANGUAGE 1033)
                KEY INDEX [PK_ContentVersion]
                ON [FTC_CmsContent]
                WITH CHANGE_TRACKING AUTO
        ');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.fulltext_indexes fi
        JOIN sys.tables t ON t.object_id = fi.object_id
        WHERE t.[name] = 'MediaAsset'
    )
    BEGIN
        EXEC('
            CREATE FULLTEXT INDEX ON [dbo].[MediaAsset]
                ([AltText] LANGUAGE 1033, [Title] LANGUAGE 1033, [Description] LANGUAGE 1033)
                KEY INDEX [PK_MediaAsset]
                ON [FTC_CmsContent]
                WITH CHANGE_TRACKING AUTO
        ');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.fulltext_indexes fi
        JOIN sys.tables t ON t.object_id = fi.object_id
        WHERE t.[name] = 'TaxonomyTerm'
    )
    BEGIN
        EXEC('
            CREATE FULLTEXT INDEX ON [dbo].[TaxonomyTerm]
                ([Name] LANGUAGE 1033)
                KEY INDEX [PK_TaxonomyTerm]
                ON [FTC_CmsContent]
                WITH CHANGE_TRACKING AUTO
        ');
    END;

    PRINT 'V002: Full-Text Search objects created successfully.';
END;
GO
