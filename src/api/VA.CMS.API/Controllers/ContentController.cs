using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Markdown;

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
    private readonly IMediaAltTextGuardRepository _altTextGuard;
    private readonly IContentVersionRepository _versions;
    private readonly IMarkdownRenderer _renderer;

    public ContentController(
        IContentEntryRepository entries,
        IRbacService rbac,
        IMediaAltTextGuardRepository altTextGuard,
        IContentVersionRepository versions,
        IMarkdownRenderer renderer)
    {
        _entries      = entries;
        _rbac         = rbac;
        _altTextGuard = altTextGuard;
        _versions     = versions;
        _renderer     = renderer;
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
    [ProducesResponseType(typeof(ContentEntryDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        return Ok(new ContentEntryDetailResponse(
            entry.Id,
            entry.ContentTypeId,
            entry.Slug,
            entry.Locale,
            entry.Status,
            entry.PublishedVersionId,
            entry.OwnerId,
            entry.CreatedAt,
            entry.UpdatedAt,
            MarkdownBody: entry.FieldsJson,
            RenderedBody: entry.RenderedFieldsJson));
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

    /// <summary>
    /// Direct publish. Requires CanPublish.
    /// Issue #43 — FR-MEDIA-05: blocks publish if any referenced image asset lacks alt text.
    /// Issue #66 — FR-AUTH-02a: regenerates RenderedFieldsJson on publish.
    /// </summary>
    [HttpPost("{id:long}/publish")]
    [Authorize(Policy = CmsRoles.Policies.CanPublish)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ContentPublishBlockedResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        // Alt text guard: block publish if any referenced image lacks alt text.
        var missingAltText = await _altTextGuard.GetMissingAltTextAsync(id);
        if (missingAltText.Count > 0)
        {
            return UnprocessableEntity(new ContentPublishBlockedResponse(
                Error: "One or more images referenced by this content are missing alt text and cannot be used in published content.",
                BlockingAssets: missingAltText
                    .Select(a => new BlockingAssetItem(a.Id, a.FileName))
                    .ToList()));
        }

        // Issue #66: regenerate RenderedFieldsJson on publish (BRD FR-AUTH-02a).
        var versions = await _versions.ListWithAuthorAsync(id, 1, 1);
        var latestVersion = versions.FirstOrDefault();
        if (latestVersion != null)
        {
            var renderedJson = RenderFieldsJson(latestVersion.FieldsJson, _renderer);
            await _versions.UpdateRenderedFieldsAsync(latestVersion.Id, renderedJson);
        }

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

    /// <summary>
    /// Update a content entry's slug.
    /// Validates uniqueness within locale; creates a 301 redirect if the entry is Published.
    /// Issue #33: FR-NAV-05.
    /// </summary>
    [HttpPatch("{id:long}/slug")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSlug(long id, [FromBody] ContentEntryUpdateSlugRequest request)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return Forbidden("You do not have permission to edit content in this section.");
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest(new { error = "Slug must not be empty." });

        var actorId = _rbac.GetUserId(User) ?? 0;
        var (success, errorMsg) = await _entries.UpdateSlugAsync(id, request.Slug.Trim(), actorId);

        if (!success)
            return BadRequest(new { error = errorMsg ?? "Slug update failed." });

        return NoContent();
    }

    /// <summary>
    /// Duplicate a content entry (issue #36, BRD FR-AUTH-07).
    /// Creates a new Draft with '(Copy)' appended to title, slug cleared,
    /// all field values copied, media references shared (not re-uploaded).
    /// Requires CanWrite.
    /// </summary>
    [HttpPost("{id:long}/duplicate")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(typeof(ContentEntryDuplicateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        // Section-scope enforcement for ContentOwner.
        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return Forbidden("You do not have permission to duplicate content in this section.");
        }

        var actorId = _rbac.GetUserId(User) ?? 0;
        var (success, newEntryId, errorMsg) = await _entries.DuplicateAsync(id, actorId);

        if (!success)
            return BadRequest(new { error = errorMsg ?? "Duplicate failed." });

        return CreatedAtAction(
            nameof(GetById),
            new { id = newEntryId },
            new ContentEntryDuplicateResponse(newEntryId!.Value));
    }

    /// <summary>
    /// Set or clear scheduled publish/expire times for a content entry.
    /// Issue #35: BRD FR-AUTH-04.
    /// Content owner can set a 'Publish at' datetime on a draft (AC1).
    /// Content owner can set an 'Expire at' datetime on a published entry (AC3).
    /// Requires CanWrite.
    /// </summary>
    [HttpPatch("{id:long}/schedule")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSchedule(long id, [FromBody] ContentEntryScheduleRequest request)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        // Section-scope enforcement for ContentOwner.
        if (!_rbac.HasGlobalRole(User,
                CmsRoles.Editor, CmsRoles.SiteAdmin, CmsRoles.SystemAdmin))
        {
            if (!_rbac.IsAuthorizedForSlug(User, entry.Slug, CmsRoles.ContentOwner))
                return Forbidden("You do not have permission to schedule content in this section.");
        }

        var actorId = _rbac.GetUserId(User) ?? 0;
        var (success, errorMsg) = await _entries.SetScheduleAsync(
            id, request.ScheduledPublishAt, request.ScheduledExpireAt, actorId);

        if (!success)
            return BadRequest(new { error = errorMsg ?? "Schedule update failed." });

        return NoContent();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private ObjectResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = message });

    /// <summary>
    /// Render all string values in a FieldsJson blob through the Markdown renderer.
    /// Returns a new JSON object with the same keys but string values replaced with rendered HTML.
    /// Issue #66: BRD FR-AUTH-02a/02b — RenderedFieldsJson mirrors FieldsJson with HTML values.
    /// </summary>
    internal static string RenderFieldsJson(string fieldsJson, IMarkdownRenderer renderer)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(fieldsJson);
            var rendered = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var raw = prop.Value.GetString() ?? string.Empty;
                    rendered[prop.Name] = renderer.Render(raw);
                }
                else
                {
                    rendered[prop.Name] = prop.Value.Clone();
                }
            }
            return System.Text.Json.JsonSerializer.Serialize(rendered);
        }
        catch
        {
            return "{}";
        }
    }
}

// ── Request / response DTOs ───────────────────────────────────────────────────

public sealed record ContentEntryCreateRequest(
    long    ContentTypeId,
    string  Slug,
    string? Locale = null);

public sealed record ContentEntryUpdateRequest();   // fields TBD in content-model story

public sealed record ContentEntryUpdateSlugRequest(string Slug);

/// <summary>
/// Request body for PATCH /api/v1/content/{id}/schedule.
/// Issue #35: BRD FR-AUTH-04.
/// Both fields are optional — null clears the value; omitting a field leaves it unchanged.
/// </summary>
public sealed record ContentEntryScheduleRequest(
    DateTime? ScheduledPublishAt,
    DateTime? ScheduledExpireAt);

public sealed record ContentEntryCreateResponse(long Id);

/// <summary>Response body for POST /api/v1/content/{id}/duplicate. Issue #36.</summary>
public sealed record ContentEntryDuplicateResponse(long NewEntryId);

// ── Issue #43: Alt text enforcement DTOs ──────────────────────────────────────

/// <summary>
/// Response body when POST /api/v1/content/{id}/publish is blocked due to missing alt text.
/// Issue #43 — FR-MEDIA-05.
/// </summary>
public sealed record ContentPublishBlockedResponse(
    string Error,
    IReadOnlyList<BlockingAssetItem> BlockingAssets);

/// <summary>A single asset that is blocking publish because its alt text is not set.</summary>
public sealed record BlockingAssetItem(long Id, string FileName);

// ── Issue #66: Content entry detail response with markdownBody/renderedBody ───

/// <summary>
/// Content entry API response including rendered fields — issue #66, BRD FR-AUTH-02a.
/// markdownBody: raw CommonMark Markdown stored in FieldsJson (from joined ContentVersion).
/// renderedBody: server-rendered HTML via Markdig DisableHtml() pipeline.
/// Both fields are null when the entry has no published version yet.
/// </summary>
public sealed record ContentEntryDetailResponse(
    long      Id,
    long      ContentTypeId,
    string    Slug,
    string    Locale,
    string    Status,
    long?     PublishedVersionId,
    long      OwnerId,
    DateTime  CreatedAt,
    DateTime  UpdatedAt,
    // FR-AUTH-02a/02b: both forms always present for RichText fields
    string?   MarkdownBody,
    string?   RenderedBody);

