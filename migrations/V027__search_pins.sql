-- V027__search_pins.sql
-- Issue #52 — Implement pinned search results management
-- BRD FR-SEARCH-04
--
-- Creates:
--   SearchPin table — stores (QueryString, ContentEntryId) pairs
--   usp_SearchPin_List   — list all pins, optionally filtered by query string
--   usp_SearchPin_Create — create a pin (idempotent: upserts on QueryString)
--   usp_SearchPin_Delete — delete a pin by Id
--
-- Updated search SP (usp_Search_FullText_WithPins) is added here so the
-- SearchController can prepend pinned results at the top of any search response
-- with a IsFeatured = 1 flag.  The original usp_Search_FullText is left intact
-- for backwards compatibility; the app layer calls the new SP.

-- ── 1. SearchPin table ────────────────────────────────────────────────────────

CREATE TABLE [dbo].[SearchPin] (
    [Id]             BIGINT IDENTITY(1,1) NOT NULL,
    [QueryString]    NVARCHAR(500)        NOT NULL,
    [ContentEntryId] BIGINT               NOT NULL,
    [CreatedById]    BIGINT               NULL,
    [CreatedAt]      DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SearchPin] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_SearchPin_ContentEntry] FOREIGN KEY ([ContentEntryId])
        REFERENCES [dbo].[ContentEntry] ([Id]) ON DELETE CASCADE
);

-- A query string can only be pinned to one content entry at a time.
CREATE UNIQUE INDEX [UX_SearchPin_QueryString] ON [dbo].[SearchPin] ([QueryString]);

-- Fast lookup of all pins for a given content entry (e.g., "is this entry pinned?")
CREATE INDEX [IX_SearchPin_ContentEntryId] ON [dbo].[SearchPin] ([ContentEntryId]);
GO

-- ── 2. usp_SearchPin_List ─────────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_SearchPin_List]
    @QueryString NVARCHAR(500) = NULL   -- NULL = return all pins
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        sp.[Id],
        sp.[QueryString],
        sp.[ContentEntryId],
        sp.[CreatedById],
        sp.[CreatedAt],
        -- Denormalise entry metadata for the admin table display
        e.[Slug]          AS EntrySlug,
        e.[Status]        AS EntryStatus,
        e.[ContentTypeId] AS EntryContentTypeId,
        -- Title from the published version's FieldsJson
        -- Stored as JSON: {"title":"…", …}; extract with JSON_VALUE
        JSON_VALUE(v.[FieldsJson], '$.title') AS EntryTitle
    FROM   [dbo].[SearchPin] sp
    JOIN   [dbo].[ContentEntry]  e ON e.[Id] = sp.[ContentEntryId]
    LEFT JOIN [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
    WHERE  (@QueryString IS NULL OR sp.[QueryString] = @QueryString)
    ORDER  BY sp.[QueryString];
END;
GO

-- ── 3. usp_SearchPin_Create ───────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_SearchPin_Create]
    @QueryString    NVARCHAR(500),
    @ContentEntryId BIGINT,
    @CreatedById    BIGINT = NULL,
    @NewId          BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Upsert: if a pin already exists for this query, replace the entry.
    IF EXISTS (SELECT 1 FROM [dbo].[SearchPin] WHERE [QueryString] = @QueryString)
    BEGIN
        UPDATE [dbo].[SearchPin]
        SET    [ContentEntryId] = @ContentEntryId,
               [CreatedById]    = @CreatedById,
               [CreatedAt]      = SYSUTCDATETIME()
        WHERE  [QueryString] = @QueryString;

        SELECT @NewId = [Id] FROM [dbo].[SearchPin] WHERE [QueryString] = @QueryString;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[SearchPin] ([QueryString], [ContentEntryId], [CreatedById], [CreatedAt])
        VALUES (@QueryString, @ContentEntryId, @CreatedById, SYSUTCDATETIME());

        SET @NewId = SCOPE_IDENTITY();
    END;
END;
GO

-- ── 4. usp_SearchPin_Delete ───────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_SearchPin_Delete]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[SearchPin] WHERE [Id] = @Id;
END;
GO

-- ── 5. usp_Search_GetPinsForQuery ─────────────────────────────────────────────
-- Used by the search endpoint to prepend the pinned entry (if any) for a query.
-- Uses only columns that always exist (no FieldsPlainText dependency) so that
-- the SP is valid even when the FTS computed column from V002 is not present.

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_GetPinsForQuery]
    @QueryString NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        e.[Id],
        e.[Slug],
        e.[ContentTypeId],
        e.[Locale],
        e.[UpdatedAt]                                     AS PublishedAt,
        ct.[DisplayName]                                  AS ContentTypeName,
        JSON_VALUE(v.[FieldsJson], '$.title')             AS Title,
        -- Prefer the explicit summary field; fall back to first 300 chars of FieldsJson text.
        -- We do not reference the FTS computed column FieldsPlainText here because it may
        -- not exist when FTS is not installed (V002 guards it with IF NOT EXISTS).
        ISNULL(
            JSON_VALUE(v.[FieldsJson], '$.summary'),
            LEFT(v.[FieldsJson], 300)
        )                                                 AS Excerpt,
        0                                                 AS [RANK],   -- pinned results sorted to top
        CAST(1 AS BIT)                                    AS IsFeatured
    FROM   [dbo].[SearchPin] sp
    JOIN   [dbo].[ContentEntry] e  ON e.[Id]  = sp.[ContentEntryId]
    JOIN   [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
    JOIN   [dbo].[ContentType] ct  ON ct.[Id] = e.[ContentTypeId]
    WHERE  sp.[QueryString] = @QueryString
      AND  e.[Status] = 'Published';
END;
GO
