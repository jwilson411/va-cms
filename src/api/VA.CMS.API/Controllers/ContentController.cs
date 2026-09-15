using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Content CRUD and workflow endpoints.
/// Story #23 RBAC gates (BRD FR-USERS-03 / FR-USERS-04):
///
///   GET    /api/v1/content          — CanRead  (all roles)
///   GET    /api/v1/content/{id}     — CanRead
///   POST   /api/v1/content          — CanWrite (ContentOwner, Editor, SiteAdmin, SystemAdmin)
///   PATCH  /api/v1/content/{id}     — CanWrite + section scope enforced by service
///   DELETE /api/v1/content/{id}     — CanWrite + section scope enforced by service
///   POST   /api/v1/content/{id}/submit-review  — CanWrite
///   POST   /api/v1/content/{id}/approve        — CanPublish (Editor, SiteAdmin, SystemAdmin)
///   POST   /api/v1/content/{id}/publish        — CanPublish
///   POST   /api/v1/content/{id}/unpublish      — CanPublish
///   POST   /api/v1/content/{id}/archive        — CanPublish
/// </summary>
[ApiController]
[Route("api/v1/content")]
public class ContentController : ControllerBase
{
    private readonly IContentEntryRepository _entries;
    private readonly IRbacService _rbac;

    public ContentController(IContentEntryRepository entries, IRbacService rbac)
    {
        _entries = entries;
        _rbac    = rbac;
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>List content entries. All CMS roles permitted.</summary>
    [HttpGet]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(IEnumerable<ContentEntry>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] long? contentTypeId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _entries.ListAsync(page, pageSize, status);
        return Ok(result.Items);
    }

    /// <summary>Get a single content entry by id. All CMS roles permitted.</summary>
    [HttpGet("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(ContentEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        return entry is null ? NotFound() : Ok(entry);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new content entry. Requires CanWrite.
    /// ContentOwner: section scope enforced by checking slug against assigned section.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(typeof(ContentEntryCreateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] ContentEntryCreateRequest request)
    {
        // Section-scope enforcement for ContentOwner.
        // Editor / SiteAdmin / SystemAdmin bypass this check (global roles).
        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            // Must be a ContentOwner — verify they are authorized for the slug.
            if (!_rbac.IsAuthorizedForSlug(User, request.Slug, CmsRoles.ContentOwner))
            {
                return Forbidden("You do not have permission to create content in this section.");
            }
        }

        var userId = _rbac.GetUserId(User) ?? 0;
        var entry  = new ContentEntry
        {
            ContentTypeId = request.ContentTypeId,
            Slug          = request.Slug,
            Locale        = request.Locale ?? "en-US",
            OwnerId       = userId,
        };

        var id = await _entries.CreateAsync(entry);
        return CreatedAtAction(nameof(GetById), new { id }, new ContentEntryCreateResponse(id));
    }

    /// <summary>
    /// Update content entry fields. Requires CanWrite.
    /// ContentOwner: section scope enforced against the entry's slug.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(long id, [FromBody] ContentEntryUpdateRequest request)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        // Section-scope enforcement for ContentOwner.
        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return Forbidden("You do not have permission to edit content in this section.");
        }

        await _entries.UpdateAsync(entry);
        return NoContent();
    }

    /// <summary>
    /// Archive (soft-delete) a content entry. Requires CanWrite.
    /// ContentOwner: section scope enforced.
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return Forbidden("You do not have permission to archive content in this section.");
        }

        await _entries.ArchiveAsync(id, _rbac.GetUserId(User) ?? 0);
        return NoContent();
    }

    // ── Workflow transitions ───────────────────────────────────────────────────

    /// <summary>Submit content for review (Draft → InReview). Requires CanWrite.</summary>
    [HttpPost("{id:long}/submit-review")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitReview(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return Forbidden("You do not have permission to submit content in this section.");
        }

        return NoContent(); // Workflow service wired in a later story.
    }

    /// <summary>
    /// Approve or publish content. Requires CanPublish (Editor, SiteAdmin, SystemAdmin).
    /// ContentOwner and ReadOnly receive 403.
    /// </summary>
    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = CmsRoles.Policies.CanPublish)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();
        return NoContent();
    }

    /// <summary>Direct publish. Requires CanPublish.</summary>
    [HttpPost("{id:long}/publish")]
    [Authorize(Policy = CmsRoles.Policies.CanPublish)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();
        return NoContent();
    }

    /// <summary>Unpublish. Requires CanPublish.</summary>
    [HttpPost("{id:long}/unpublish")]
    [Authorize(Policy = CmsRoles.Policies.CanPublish)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unpublish(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();
        return NoContent();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private ObjectResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = message });
}

// ── Request / response DTOs ───────────────────────────────────────────────────

public sealed record ContentEntryCreateRequest(
    long    ContentTypeId,
    string  Slug,
    string? Locale = null);

public sealed record ContentEntryUpdateRequest();   // fields TBD in content-model story

public sealed record ContentEntryCreateResponse(long Id);
