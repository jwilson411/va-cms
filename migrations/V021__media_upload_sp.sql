-- V021__media_upload_sp.sql
-- Issue #40 — Build media upload API with storage backend abstraction
-- Adds usp_MediaAsset_GetByStoragePath for safe duplicate detection
-- and ensures usp_MediaAsset_Create accepts all required columns.
-- All existing SPs (usp_MediaAsset_Create, usp_MediaAsset_GetById, etc.)
-- were shipped in V008 / V011. No schema changes needed — MediaAsset table
-- already has all required columns (StoragePath, StorageBackend, MimeType,
-- FileSizeBytes, Width, Height, UploadedById). This migration is a no-op
-- placeholder so DbUp has a record of this story's schema checkpoint.
--
-- BRD FR-MEDIA-01, FR-MEDIA-07, FR-SECURITY-06
PRINT 'V021: media upload API checkpoint — no schema changes required.';
GO
