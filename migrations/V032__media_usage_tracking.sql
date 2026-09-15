-- V032__media_usage_tracking.sql
-- Issue #44 — Implement media usage tracking and safe delete
-- BRD FR-MEDIA-06
--
-- Adds a stored procedure that returns usage rows enriched with the
-- content entry title (extracted from FieldsJson) and a display-friendly
-- content type name for the 409 conflict body on DELETE /api/v1/media/{id}.
--
-- The MediaUsage table, usp_MediaUsage_Upsert, usp_MediaUsage_DeleteForEntry,
-- usp_MediaAsset_GetUsage, and usp_MediaAsset_SafeDelete already exist
-- (V008__stored_procedures.sql / V001__initial_schema.sql).
-- This migration adds the richer "GetUsageWithTitle" variant.

-- ─────────────────────────────────────────────────────────────────────────────
-- usp_MediaAsset_GetUsageWithTitle
-- Returns usage rows joined with ContentEntry AND ContentType, plus the 'title'
-- field extracted from the latest ContentVersion FieldsJson for each entry.
-- Used by DELETE /api/v1/media/{id} 409 response and the admin detail panel.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_GetUsageWithTitle]
    @MediaAssetId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        mu.ContentEntryId,
        mu.FieldName,
        e.Slug,
        e.Status,
        e.ContentTypeId,
        ct.DisplayName                      AS ContentTypeName,
        -- Extract title from the latest version FieldsJson (best-effort JSON_VALUE)
        -- Falls back to Slug when FieldsJson has no top-level "title" key.
        COALESCE(
            JSON_VALUE(v.FieldsJson, '$.title'),
            JSON_VALUE(v.FieldsJson, '$.Title'),
            e.Slug
        )                                   AS EntryTitle,
        e.CreatedAt,
        e.UpdatedAt
    FROM  [MediaUsage]      mu
    JOIN  [ContentEntry]    e  ON  e.Id  = mu.ContentEntryId
    JOIN  [ContentType]     ct ON  ct.Id = e.ContentTypeId
    LEFT JOIN [ContentVersion] v ON v.Id = e.PublishedVersionId
    WHERE mu.MediaAssetId = @MediaAssetId
    ORDER BY e.UpdatedAt DESC;
END;
GO
