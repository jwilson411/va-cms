-- V022__media_webp_path.sql
-- Issue #41 — Implement image resize and WebP conversion on upload
-- BRD FR-MEDIA-02
--
-- Adds WebPStoragePath column to MediaAsset to track the generated WebP variant.
-- NULL = asset is non-image or WebP generation failed (fallback to original).
-- Adds usp_MediaAsset_UpdateWebPPath for the image processing pipeline to record
-- the WebP path after upload.

ALTER TABLE [MediaAsset]
    ADD [WebPStoragePath] NVARCHAR(2000) NULL;
GO

-- Index: WebP lookup by asset ID (used by serve layer to pick WebP vs original)
CREATE INDEX [IX_MediaAsset_WebPStoragePath]
    ON [MediaAsset] ([Id])
    INCLUDE ([WebPStoragePath], [MimeType])
    WHERE [WebPStoragePath] IS NOT NULL;
GO

-- SP: record WebP path after generation
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_UpdateWebPPath]
    @Id            BIGINT,
    @WebPStoragePath NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [MediaAsset]
    SET    [WebPStoragePath] = @WebPStoragePath,
           [UpdatedAt]       = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

PRINT 'V022: MediaAsset.WebPStoragePath column + usp_MediaAsset_UpdateWebPPath added.';
GO
