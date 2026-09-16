using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.ContentTypes;
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
///   POST   /api/v1/content/{id}/return         — CanPublish (InReview → Draft, comment required)
///   POST   /api/v1/content/{id}/archive        — CanPublish (Published/Approved → Archived)
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
    private readonly IMediaExtendedRepository _mediaUsage;
    private readonly IContentTypeRepository _contentTypes;
    private readonly IFieldTypeRegistry _registry;
    private readonly IWebhookBackgroundDispatcher _webhooks;

    public ContentController(
        IContentEntryRepository entries,
        IRbacService rbac,
        IMediaAltTextGuardRepository altTextGuard,
        IContentVersionRepository versions,
        IMarkdownRenderer renderer,
        IMediaExtendedRepository mediaUsage,
        IContentTypeRepository contentTypes,
        IFieldTypeRegistry registry,
        IWebhookBackgroundDispatcher webhooks)
    {
        _entries      = entries;
        _rbac         = rbac;
        _altTextGuard = altTextGuard;
        _versions     = versions;
        _renderer     = renderer;
        _mediaUsage   = mediaUsage;
        _contentTypes = contentTypes;
        _registry     = registry;
        _webhooks     = webhooks;
    }

    /// <summary>Payload for content.* webhook events (issue #54).</summary>
    private static object ContentEventPayload(ContentEntry entry) => new
    {
        id              = entry.Id,
        slug            = entry.Slug,
        locale          = entry.Locale,
        contentTypeName = entry.ContentTypeName,
        status          = entry.Status,
    };

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

        // The admin editor works on the *latest* version (a draft may be newer than
        // the published one); entry.FieldsJson is the published version's fields.
        var latest = (await _versions.ListWithAuthorAsync(id, 1, 1)).FirstOrDefault();

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
            RenderedBody: entry.RenderedFieldsJson,
            FieldsJson:      latest?.FieldsJson ?? entry.FieldsJson ?? "{}",
            LatestVersionId: latest?.Id,
            ContentTypeName: entry.ContentTypeName));
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

        // Resolve the content type: by DB id, or by registry name (the admin SPA only
        // knows names). A registered type gets its ContentType row created on first use.
        long contentTypeId;
        if (request.ContentTypeId is > 0)
        {
            contentTypeId = request.ContentTypeId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.ContentTypeName))
        {
            var resolved = await EnsureContentTypeAsync(request.ContentTypeName);
            if (resolved is null)
                return UnprocessableEntity(new { error = $"Content type '{request.ContentTypeName}' is not registered." });
            contentTypeId = resolved.Value;
        }
        else
        {
            return UnprocessableEntity(new { error = "contentTypeId or contentTypeName is required." });
        }

        if (request.FieldsJson is not null && !IsJsonObject(request.FieldsJson))
            return UnprocessableEntity(new { error = "fieldsJson must be a JSON object." });

        var userId = _rbac.GetUserId(User) ?? 0;
        var entry  = new ContentEntry
        {
            ContentTypeId = contentTypeId,
            Slug          = request.Slug,
            Locale        = request.Locale ?? "en-US",
            OwnerId       = userId,
        };

        var id = await _entries.CreateAsync(entry);

        // Every entry gets an initial version so workflow transitions (which log a
        // ContentVersionId) and the editor's field load always have something to use.
        await _versions.CreateAsync(new ContentVersion
        {
            ContentEntryId = id,
            FieldsJson     = request.FieldsJson ?? "{}",
            Status         = "Draft",
            AuthorId       = userId,
            ChangeNote     = "Created",
        });

        return CreatedAtAction(nameof(GetById), new { id }, new ContentEntryCreateResponse(id));
    }

    /// <summary>
    /// Resolve a registry content type name to its ContentType row id, creating the
    /// row from the registry definition if it does not exist yet. Null if unregistered.
    /// </summary>
    private async Task<long?> EnsureContentTypeAsync(string name)
    {
        var existing = await _contentTypes.GetByNameAsync(name);
        if (existing is not null) return existing.Id;

        var def = _registry.GetByName(name);
        if (def is null) return null;

        var schema = def.Fields.Select(f => new
        {
            name     = f.Name,
            label    = f.Label,
            type     = f.Type.ToString(),
            required = f.Required,
        });

        return await _contentTypes.UpsertAsync(new ContentType
        {
            Name            = def.Name,
            DisplayName     = def.DisplayName,
            TemplateId      = def.TemplateId,
            AllowWorkflow   = def.AllowWorkflow,
            FieldSchemaJson = JsonSerializer.Serialize(schema),
        });
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
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

        if (request.FieldsJson is not null)
        {
            if (!IsJsonObject(request.FieldsJson))
                return UnprocessableEntity(new { error = "fieldsJson must be a JSON object." });

            // Each save is an immutable version (BRD FR-AUTH-03: version history).
            // The published version is untouched until the next publish.
            await _versions.CreateAsync(new ContentVersion
            {
                ContentEntryId = id,
                FieldsJson     = request.FieldsJson,
                Status         = "Draft",
                AuthorId       = _rbac.GetUserId(User) ?? 0,
                ChangeNote     = request.ChangeNote,
            });
        }

        await _entries.UpdateAsync(entry);   // bumps UpdatedAt
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
        entry.Status = "Archived";
        _webhooks.Enqueue(WebhookEvents.ContentArchived, ContentEventPayload(entry));
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

        return await TransitionAsync(entry, "InReview");
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

        return await TransitionAsync(entry, "Approved");
    }

    /// <summary>
    /// Return content to the author (InReview → Draft). A comment is required.
    /// Requires CanPublish (reviewers).
    /// </summary>
    [HttpPost("{id:long}/return")]
    [Authorize(Policy = CmsRoles.Policies.CanPublish)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ContentTransitionErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Return(long id, [FromBody] ContentReturnRequest? request)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        return await TransitionAsync(entry, "Draft", request?.Comment);
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

        var latestVersion = (await _versions.ListWithAuthorAsync(id, 1, 1)).FirstOrDefault();
        if (latestVersion is null)
            return UnprocessableEntity(new ContentTransitionErrorResponse("Content has no versions to publish."));

        // Issue #66: regenerate RenderedFieldsJson on publish (BRD FR-AUTH-02a).
        var renderedJson = RenderFieldsJson(latestVersion.FieldsJson, _renderer);
        await _versions.UpdateRenderedFieldsAsync(latestVersion.Id, renderedJson);

        // Already published: re-point at the latest version without a status transition
        // (usp_Workflow_Transition has no Published→Published edge).
        if (entry.Status == "Published")
        {
            entry.PublishedVersionId = latestVersion.Id;
            await _entries.UpdateAsync(entry);
            _webhooks.Enqueue(WebhookEvents.ContentPublished, ContentEventPayload(entry));
            return NoContent();
        }

        // Draft→Published (direct publish) and Approved→Published are both allowed by
        // usp_Workflow_Transition; anything else (InReview, Archived) is rejected there.
        var result = await TransitionAsync(entry, "Published", version: latestVersion);
        if (result is not NoContentResult) return result;

        entry.Status             = "Published";
        entry.PublishedVersionId = latestVersion.Id;
        await _entries.UpdateAsync(entry);
        _webhooks.Enqueue(WebhookEvents.ContentPublished, ContentEventPayload(entry));
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

        // Published → Approved (same edge the scheduled-expiry worker uses).
        var result = await TransitionAsync(entry, "Approved");
        if (result is NoContentResult)
        {
            entry.Status = "Approved";
            _webhooks.Enqueue(WebhookEvents.ContentUnpublished, ContentEventPayload(entry));
        }
        return result;
    }

    /// <summary>
    /// Archive via the workflow state machine (Published/Approved → Archived), logging a
    /// WorkflowTransition row. Requires CanPublish. Issue #37.
    /// DELETE /api/v1/content/{id} remains the unconditional soft-delete for authors.
    /// </summary>
    [HttpPost("{id:long}/archive")]
    [Authorize(Policy = CmsRoles.Policies.CanPublish)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ContentTransitionErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveTransition(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        var result = await TransitionAsync(entry, "Archived");
        if (result is NoContentResult)
        {
            entry.Status = "Archived";
            _webhooks.Enqueue(WebhookEvents.ContentArchived, ContentEventPayload(entry));
        }
        return result;
    }

    /// <summary>
    /// Run a workflow transition from the entry's current status to <paramref name="toStatus"/>.
    /// 422 with a plain-language message when the state machine rejects it.
    /// </summary>
    private async Task<IActionResult> TransitionAsync(
        ContentEntry entry, string toStatus, string? comment = null, ContentVersionWithAuthor? version = null)
    {
        version ??= (await _versions.ListWithAuthorAsync(entry.Id, 1, 1)).FirstOrDefault();
        if (version is null)
            return UnprocessableEntity(new ContentTransitionErrorResponse("Content has no versions."));

        var (ok, error) = await _entries.TransitionAsync(
            entry.Id, version.Id, entry.Status, toStatus, _rbac.GetUserId(User) ?? 0, comment);

        return ok
            ? NoContent()
            : UnprocessableEntity(new ContentTransitionErrorResponse(error ?? "Transition not permitted."));
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

    // ── Media usage sync ──────────────────────────────────────────────────────

    /// <summary>
    /// Sync media asset usage for a content entry.
    /// Called by the admin SPA whenever the field state changes (draft save or publish).
    ///
    /// AC (Issue #44 — FR-MEDIA-06):
    ///   MediaUsage rows created/removed when assets are added/removed from content fields.
    ///
    /// Replaces all MediaUsage rows for this entry with the provided list.
    /// Idempotent: sending the same set twice produces the same result.
    /// Requires CanWrite.
    /// </summary>
    [HttpPost("{id:long}/sync-media-usage")]
    [Authorize(Policy = CmsRoles.Policies.CanWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncMediaUsage(
        long id,
        [FromBody] SyncMediaUsageRequest request)
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

        // Clear all existing usage rows for this entry, then re-insert the provided set.
        // This is a replace-all sync: the caller is authoritative on the current field state.
        await _mediaUsage.DeleteUsageForEntryAsync(id);

        foreach (var usage in request.Usages ?? [])
        {
            if (usage.AssetId > 0 && !string.IsNullOrWhiteSpace(usage.FieldName))
                await _mediaUsage.UpsertUsageAsync(usage.AssetId, id, usage.FieldName.Trim());
        }

        return NoContent();
    }

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

/// <summary>
/// Create request. Supply either <see cref="ContentTypeId"/> (DB id) or
/// <see cref="ContentTypeName"/> (registry name, e.g. "standard_page"). Optional
/// <see cref="FieldsJson"/> seeds the initial version.
/// </summary>
public sealed record ContentEntryCreateRequest(
    string  Slug,
    long?   ContentTypeId   = null,
    string? ContentTypeName = null,
    string? Locale          = null,
    string? FieldsJson      = null);

/// <summary>
/// Update request. <see cref="FieldsJson"/> (a JSON object of field values) is saved as
/// a new Draft version; omit it to only touch UpdatedAt.
/// </summary>
public sealed record ContentEntryUpdateRequest(string? FieldsJson = null, string? ChangeNote = null);

/// <summary>Body for POST /api/v1/content/{id}/return — a comment is required.</summary>
public sealed record ContentReturnRequest(string? Comment);

/// <summary>422 body when usp_Workflow_Transition rejects a state change.</summary>
public sealed record ContentTransitionErrorResponse(string Error);

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

/// <summary>A single asset blocking publish due to missing alt text.</summary>
public sealed record BlockingAssetItem(long Id, string FileName);

// ── Issue #44: Media usage sync DTOs ─────────────────────────────────────────

/// <summary>
/// Request body for POST /api/v1/content/{id}/sync-media-usage.
/// Issue #44 — FR-MEDIA-06: sync media usage rows when content fields change.
/// </summary>
public sealed class SyncMediaUsageRequest
{
    /// <summary>
    /// Current set of media asset references in this content entry's fields.
    /// Each item maps one asset to the field that references it.
    /// Sending an empty list clears all MediaUsage rows for this entry.
    /// </summary>
    public IReadOnlyList<MediaUsageItem>? Usages { get; set; }
}

/// <summary>A single asset reference in a content entry field.</summary>
public sealed class MediaUsageItem
{
    /// <summary>MediaAsset.Id of the referenced asset.</summary>
    public long   AssetId   { get; set; }
    /// <summary>Field name in the content type schema, e.g. "featuredImage".</summary>
    public string FieldName { get; set; } = string.Empty;
}

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
    string?   RenderedBody,
    // Latest version's field values (what the admin editor loads) and its id
    string    FieldsJson,
    long?     LatestVersionId,
    string?   ContentTypeName);

