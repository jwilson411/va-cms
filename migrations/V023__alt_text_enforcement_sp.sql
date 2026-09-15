-- V023__alt_text_enforcement_sp.sql
-- Issue #43 — Enforce alt text requirement before asset use in published content.
-- BRD FR-MEDIA-05.
--
-- New SP: usp_MediaAsset_GetMissingAltTextForEntry
--   Returns asset IDs that are referenced by a content entry and lack alt text.
--   Called by the publish guard before allowing a content entry to transition to Published.

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_GetMissingAltTextForEntry]
    @ContentEntryId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    -- Return all media assets referenced by this entry that are images and have no alt text.
    -- Non-image assets (PDFs, etc.) are excluded — alt text is only required for images.
    SELECT ma.Id, ma.FileName, ma.MimeType
    FROM   [MediaUsage] mu
    JOIN   [MediaAsset] ma ON ma.Id = mu.MediaAssetId
    WHERE  mu.ContentEntryId = @ContentEntryId
      AND  ma.MimeType LIKE 'image/%'
      AND  (ma.AltText IS NULL OR LEN(TRIM(ma.AltText)) = 0);
END;
GO
