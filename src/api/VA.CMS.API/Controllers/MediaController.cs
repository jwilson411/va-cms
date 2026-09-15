using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Media library upload endpoint.
/// Issue #40 — Build media upload API with storage backend abstraction.
/// BRD FR-MEDIA-01, FR-MEDIA-07, FR-SECURITY-06.
///
///   POST /api/v1/media/upload — multipart file upload; CanWrite required.
/// </summary>
[ApiController]
[Route("api/v1/media")]
public class MediaController : ControllerBase
{
    private readonly IMediaUploadService _uploader;
    private readonly IRbacService _rbac;

    public MediaController(IMediaUploadService uploader, IRbacService rbac)
    {
        _uploader = uploader;
        _rbac     = rbac;
    }

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
    [ProducesResponseType(typeof(MediaUploadErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new MediaUploadErrorResponse("No file was provided or the file is empty."));

        var userId = _rbac.GetUserId(User) ?? 0;

        var (asset, error) = await _uploader.UploadAsync(file, userId, ct);

        if (error is not null)
            return BadRequest(new MediaUploadErrorResponse(error));

        var response = new MediaUploadResponse(
            Id:            asset!.Id,
            FileName:      asset.FileName,
            StoragePath:   asset.StoragePath,
            StorageBackend: asset.StorageBackend,
            MimeType:      asset.MimeType,
            FileSizeBytes: asset.FileSizeBytes,
            Width:         asset.Width,
            Height:        asset.Height);

        return CreatedAtAction(nameof(Upload), new { id = asset.Id }, response);
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Response body for a successful media upload.
/// Contains the MediaAsset row's key fields. AltText is empty on upload;
/// callers must PATCH /api/v1/media/{id} to set it before referencing in content.
/// </summary>
public sealed record MediaUploadResponse(
    long    Id,
    string  FileName,
    string  StoragePath,
    string  StorageBackend,
    string  MimeType,
    long    FileSizeBytes,
    int?    Width,
    int?    Height);

/// <summary>Response body for a 400 error.</summary>
public sealed record MediaUploadErrorResponse(string Error);
