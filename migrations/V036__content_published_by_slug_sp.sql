-- V036__content_published_by_slug_sp.sql
-- Public delivery lookup for the Next.js site (docs/ARCHITECTURE.md "Content
-- Rendering Pipeline": GET /api/v1/content/{slug}).
--
-- Like usp_ContentEntry_GetBySlug this only returns Published entries joined to
-- their published version, but additionally returns the content type name (so the
-- caller can filter by ?type=) and PublishedAt (e.UpdatedAt — same convention as
-- usp_Search_FullText: the last time the entry transitioned to Published).

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetPublishedBySlug]
    @Slug   NVARCHAR(500),
    @Locale NVARCHAR(10) = 'en-US'
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.[Id],
           e.[ContentTypeId],
           ct.[Name]             AS ContentTypeName,
           ct.[TemplateId],
           e.[Slug],
           e.[Locale],
           e.[Status],
           v.[VersionNumber],
           v.[FieldsJson],
           v.[RenderedFieldsJson],
           e.[UpdatedAt]         AS PublishedAt
    FROM   [dbo].[ContentEntry]   e
    JOIN   [dbo].[ContentVersion] v  ON v.[Id]  = e.[PublishedVersionId]
    JOIN   [dbo].[ContentType]    ct ON ct.[Id] = e.[ContentTypeId]
    WHERE  e.[Slug]   = @Slug
      AND  e.[Locale] = @Locale
      AND  e.[Status] = 'Published';
END;
GO
