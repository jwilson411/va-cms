using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Content versioning endpoints — issue #32, FR-AUTH-05.
///
///   GET  /api/v1/content/{id}/versions                     — List all versions (CanRead)
///   GET  /api/v1/content/{id}/versions/{versionId}         — Get one version (CanRead)
///   POST /api/v1/content/{id}/versions/{versionId}/restore — Restore a version (CanWrite)
/// </summary>
[ApiController]
[Route("api/v1/content/{entryId:long}/versions")]
public class ContentVersionController : ControllerBase
{
    private readonly IContentVersionRepository _versions;
    private readonly IContentEntryRepository   _entries;
    private readonly IRbacService              _rbac;

    public ContentVersionController(
        IContentVersionRepository versions,
        IContentEntryRepository   entries,
        IRbacService              rbac)
    {
        _versions = versions;
        _entries  = entries;
        _rbac     = rbac;
    }

    // ── GET /api/v1/content/{entryId}/versions ────────────────────────────────

    /// <summary>
    /// List all versions for a content entry.
    /// Returns: version number, author, date, change note, status.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ContentVersionSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        long entryId,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 25)
    {
        var entry = await _entries.GetByIdAsync(entryId);
        if (entry is null) return NotFound();

        var versions = await _versions.ListWithAuthorAsync(entryId, page, pageSize);
        var dtos = versions.Select(v => new ContentVersionSummaryDto(
            v.Id,
            v.VersionNumber,
            v.AuthorName,
            v.CreatedAt,
            v.ChangeNote,
            v.Status)).ToList();

        return Ok(dtos);
    }

    // ── GET /api/v1/content/{entryId}/versions/{versionId} ───────────────────

    /// <summary>Get a specific version by id.</summary>
    [HttpGet("{versionId:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(ContentVersionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long entryId, long versionId)
    {
        var entry = await _entries.GetByIdAsync(entryId);
        if (entry is null) return NotFound();

        var version = await _versions.GetByIdWithAuthorAsync(versionId);
        if (version is null || version.ContentEntryId != entryId) return NotFound();

        return Ok(new ContentVersionDetailDto(
            version.Id,
            version.ContentEntryId,
            version.VersionNumber,
            version.AuthorName,
            version.CreatedAt,
            version.ChangeNote,
            version.Status,
            version.FieldsJson));
    }

    // ── POST /api/v1/content/{entryId}/versions/{versionId}/restore ──────────

    /// <summary>
    /// Restore a prior version. Sets the current draft fields to the selected version's
    /// FieldsJson by creating a NEW version row — history is never deleted.
    /// Requires CanWrite.
    /// </summary>
    [HttpPost("{versionId:long}/restore")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(typeof(ContentVersionRestoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(long entryId, long versionId)
    {
        var entry = await _entries.GetByIdAsync(entryId);
        if (entry is null) return NotFound();

        // Section-scope enforcement for ContentOwner (same as ContentController)
        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "You do not have permission to restore content in this section." });
        }

        var actorId = _rbac.GetUserId(User) ?? 0;
        var newVersionId = await _versions.RestoreAsync(entryId, versionId, actorId);

        return Ok(new ContentVersionRestoreResponse(newVersionId));
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Version list row — AC: version number, author, date, change note, status.</summary>
public sealed record ContentVersionSummaryDto(
    long     Id,
    int      VersionNumber,
    string   AuthorName,
    DateTime CreatedAt,
    string?  ChangeNote,
    string   Status);

/// <summary>Full version detail including FieldsJson (for restore preview).</summary>
public sealed record ContentVersionDetailDto(
    long     Id,
    long     ContentEntryId,
    int      VersionNumber,
    string   AuthorName,
    DateTime CreatedAt,
    string?  ChangeNote,
    string   Status,
    string   FieldsJson);

/// <summary>Restore response — carries the new version id.</summary>
public sealed record ContentVersionRestoreResponse(long NewVersionId);
