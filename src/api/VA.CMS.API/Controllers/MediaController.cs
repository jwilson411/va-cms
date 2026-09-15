using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Media library endpoints.
/// Issues #40, #41, #42 — BRD FR-MEDIA-01, FR-MEDIA-02.
///
///   POST   /api/v1/media/upload — multipart file upload; CanWrite required.
///   GET    /api/v1/media        — list assets with search/filter/paging.
///   GET    /api/v1/media/{id}   — detail: preview, alt text, usage, metadata.
///   PATCH  /api/v1/media/{id}   — update alt text, title, description, tags.
///   DELETE /api/v1/media/{id}   — safe delete (blocked if asset is in use).
/// </summary>
[ApiController]
[Route("api/v1/media")]
public class MediaController : ControllerBase
{
    private readonly IMediaUploadService      _uploader;
    private readonly IMediaAssetRepository    _assets;
    private readonly IMediaExtendedRepository _extended;
    private readonly IRbacService             _rbac;

    public MediaController(
        IMediaUploadService      uploader,
        IMediaAssetRepository    assets,
        IMediaExtendedRepository extended,
        IRbacService             rbac)
    {
        _uploader = uploader;
        _assets   = assets;
        _extended = extended;
        _rbac     = rbac;
    }

    // ── Upload ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a media file (multipart/form-data).
    ///
    /// AC: POST /api/v1/media/upload accepts multipart file.
    /// AC: Storage backend selected by config (local, unc, azure_blob).
    /// AC: File stored outside web root.
    /// AC: File extension validated against MIME allow-list (BRD FR-SECURITY-06).
    /// AC: MediaAsset row created with path, MIME, size, dimensions.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(104_857_600)] // 100 MB hard cap
    [ProducesResponseType(typeof(MediaUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MediaErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new MediaErrorResponse("No file was provided or the file is empty."));

        var userId = _rbac.GetUserId(User) ?? 0;

        var (asset, error) = await _uploader.UploadAsync(file, userId, ct);

        if (error is not null)
            return BadRequest(new MediaErrorResponse(error));

        var response = new MediaUploadResponse(
            Id:              asset!.Id,
            FileName:        asset.FileName,
            StoragePath:     asset.StoragePath,
            StorageBackend:  asset.StorageBackend,
            MimeType:        asset.MimeType,
            FileSizeBytes:   asset.FileSizeBytes,
            Width:           asset.Width,
            Height:          asset.Height,
            WebPStoragePath: asset.WebPStoragePath);

        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, response);
    }

    // ── List ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// List media assets.
    ///
    /// AC (Issue #42):
    ///   - Grid and list view is a client-side toggle; server returns items.
    ///   - Search by filename, alt text (q param maps to searchTerm in SP).
    ///   - Filter by MIME type (mimeType param, e.g. "image/") and date range.
    ///   - Paginated (page, pageSize).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(MediaListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] string? mimeType,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page     = Math.Max(1, page);

        var pageResult = await _assets.ListAsync(page, pageSize, mimeType, q);

        var items = pageResult.Items
            .Select(a => MapToSummary(a))
            .ToList();

        return Ok(new MediaListResponse(
            Items:      items,
            Page:       page,
            PageSize:   pageSize,
            TotalItems: (int)pageResult.TotalItems));
    }

    // ── Detail ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get a single media asset with full metadata and usage list.
    ///
    /// AC (Issue #42): Click to view detail: preview, alt text, usage list, metadata.
    /// </summary>
    [HttpGet("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(MediaDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetById(long id)
    {
        var asset = await _assets.GetByIdAsync(id);
        if (asset is null)
            return NotFound();

        var usages = await _extended.GetUsageWithTitleAsync(id);

        return Ok(new MediaDetailResponse(
            Id:              asset.Id,
            FileName:        asset.FileName,
            StoragePath:     asset.StoragePath,
            StorageBackend:  asset.StorageBackend,
            MimeType:        asset.MimeType,
            FileSizeBytes:   asset.FileSizeBytes,
            AltText:         asset.AltText,
            Title:           asset.Title,
            Description:     asset.Description,
            Tags:            asset.Tags,
            Width:           asset.Width,
            Height:          asset.Height,
            WebPStoragePath: asset.WebPStoragePath,
            IsVirusScanPassed: asset.IsVirusScanPassed,
            UploadedById:    asset.UploadedById,
            CreatedAt:       asset.CreatedAt,
            UpdatedAt:       asset.UpdatedAt,
            Usages:          usages.Select(u => new MediaUsageSummary(
                                 u.ContentEntryId,
                                 u.FieldName,
                                 u.Slug,
                                 u.Status,
                                 u.ContentTypeId,
                                 u.ContentTypeName,
                                 u.EntryTitle)).ToList()));
    }

    // ── Update metadata ───────────────────────────────────────────────────────

    /// <summary>
    /// Update alt text, title, description, and/or tags for a media asset.
    ///
    /// AC (Issue #42): Edit alt text inline in detail view.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MediaErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateMetadata(long id, [FromBody] MediaPatchRequest body)
    {
        var asset = await _assets.GetByIdAsync(id);
        if (asset is null)
            return NotFound(new MediaErrorResponse("Media asset not found."));

        // Apply only provided fields (partial update)
        if (body.AltText     is not null) asset.AltText     = body.AltText;
        if (body.Title       is not null) asset.Title       = body.Title;
        if (body.Description is not null) asset.Description = body.Description;
        if (body.Tags        is not null) asset.Tags        = body.Tags;

        await _assets.UpdateAsync(asset);
        return NoContent();
    }

    // ── Safe delete ───────────────────────────────────────────────────────────

    /// <summary>
    /// Delete a media asset. Blocked (409) when the asset is referenced by any content entry.
    /// The 409 body includes the list of content entries (title, slug, status) currently using the asset.
    ///
    /// AC (Issue #44 — FR-MEDIA-06):
    ///   DELETE returns 409 with usage list if asset is in use.
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MediaDeleteBlockedResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(MediaErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(long id)
    {
        var asset = await _assets.GetByIdAsync(id);
        if (asset is null)
            return NotFound(new MediaErrorResponse("Media asset not found."));

        // Fetch enriched usage before attempting delete — needed for 409 body.
        var usages = (await _extended.GetUsageWithTitleAsync(id)).ToList();

        if (usages.Count > 0)
        {
            // Asset is in use — return 409 with the full usage list so callers know
            // which content entries must be updated before the delete can succeed.
            return Conflict(new MediaDeleteBlockedResponse(
                Error: "Asset is in use by one or more content entries and cannot be deleted.",
                Usages: usages.Select(u => new MediaDeleteBlockedUsage(
                    u.ContentEntryId,
                    u.EntryTitle,
                    u.Slug,
                    u.Status,
                    u.FieldName,
                    u.ContentTypeName)).ToList()));
        }

        // Asset is not in use — safe to delete.
        await _extended.SafeDeleteAsync(id);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MediaAssetSummary MapToSummary(MediaAsset a) =>
        new(a.Id, a.FileName, a.StoragePath, a.MimeType, a.FileSizeBytes,
            a.AltText, a.Title, a.Width, a.Height, a.WebPStoragePath,
            a.IsVirusScanPassed, a.CreatedAt);
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Response body for a successful media upload.</summary>
public sealed record MediaUploadResponse(
    long    Id,
    string  FileName,
    string  StoragePath,
    string  StorageBackend,
    string  MimeType,
    long    FileSizeBytes,
    int?    Width,
    int?    Height,
    string? WebPStoragePath);

/// <summary>Summary item returned in GET /api/v1/media list.</summary>
public sealed record MediaAssetSummary(
    long     Id,
    string   FileName,
    string   StoragePath,
    string   MimeType,
    long     FileSizeBytes,
    string?  AltText,
    string?  Title,
    int?     Width,
    int?     Height,
    string?  WebPStoragePath,
    bool?    IsVirusScanPassed,
    DateTime CreatedAt);

/// <summary>Paginated list response for GET /api/v1/media.</summary>
public sealed record MediaListResponse(
    IReadOnlyList<MediaAssetSummary> Items,
    int Page,
    int PageSize,
    int TotalItems);

/// <summary>Detail response for GET /api/v1/media/{id}, including usage list.</summary>
public sealed record MediaDetailResponse(
    long     Id,
    string   FileName,
    string   StoragePath,
    string   StorageBackend,
    string   MimeType,
    long     FileSizeBytes,
    string?  AltText,
    string?  Title,
    string?  Description,
    string?  Tags,
    int?     Width,
    int?     Height,
    string?  WebPStoragePath,
    bool?    IsVirusScanPassed,
    long     UploadedById,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<MediaUsageSummary> Usages);

/// <summary>A single usage record: which content entry + field references this asset.</summary>
public sealed record MediaUsageSummary(
    long    ContentEntryId,
    string  FieldName,
    string  Slug,
    string  Status,
    long    ContentTypeId,
    /// <summary>Human-readable content type display name. Issue #44.</summary>
    string  ContentTypeName,
    /// <summary>Best-effort entry title from FieldsJson; falls back to Slug. Issue #44.</summary>
    string  EntryTitle);

/// <summary>Request body for PATCH /api/v1/media/{id}.</summary>
public sealed class MediaPatchRequest
{
    public string? AltText     { get; set; }
    public string? Title       { get; set; }
    public string? Description { get; set; }
    public string? Tags        { get; set; }
}

/// <summary>Generic error body.</summary>
public sealed record MediaErrorResponse(string Error);

// ── Issue #44: Safe delete 409 body ──────────────────────────────────────────

/// <summary>
/// 409 Conflict body when DELETE /api/v1/media/{id} is blocked because
/// the asset is referenced by one or more content entries.
/// AC (Issue #44 — FR-MEDIA-06): response includes the full usage list.
/// </summary>
public sealed record MediaDeleteBlockedResponse(
    string Error,
    IReadOnlyList<MediaDeleteBlockedUsage> Usages);

/// <summary>A single content entry that is blocking the media asset delete.</summary>
public sealed record MediaDeleteBlockedUsage(
    long   ContentEntryId,
    string EntryTitle,
    string Slug,
    string Status,
    string FieldName,
    string ContentTypeName);
