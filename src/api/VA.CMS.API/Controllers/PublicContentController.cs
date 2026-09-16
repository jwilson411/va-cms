using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Markdown;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Public content delivery API consumed by the Next.js site
/// (docs/ARCHITECTURE.md "Content Rendering Pipeline", BRD FR-DEV-03/04).
///
///   GET /api/v1/content/{slug}?type={contentTypeName}&amp;locale={locale}
///
/// Anonymous, read-only, Published entries only. Slugs may contain "/"
/// (e.g. demo/health-care-overview) so the route is a catch-all; numeric ids
/// still resolve to ContentController.GetById because a constrained segment
/// outranks a catch-all in endpoint routing.
///
/// Response shape mirrors src/public/lib/cms/content.ts ContentEntry:
///   { id, contentTypeId, contentTypeName, slug, locale, status, fields, publishedAt }
/// where fields = the version's FieldsJson plus renderedBody (HTML). renderedBody
/// comes from RenderedFieldsJson when the publish pipeline produced it, otherwise
/// the Markdown body is rendered on the fly so seeded/legacy entries still display.
/// MediaReference fields holding an asset id are expanded to
/// { id, storageUrl, altText, width, height } (src/public/lib/cms/content.ts MediaAssetRef),
/// with storageUrl pointing at GET /api/v1/media/serve/{id}.
/// </summary>
[ApiController]
[Route("api/v1/content")]
[AllowAnonymous]
public class PublicContentController : ControllerBase
{
    private const string BodyField         = "body";
    private const string RenderedBodyField = "renderedBody";

    private readonly IContentEntryRepository _entries;
    private readonly IMarkdownRenderer       _renderer;
    private readonly IMediaAssetRepository   _media;
    private readonly IFieldTypeRegistry      _registry;

    public PublicContentController(
        IContentEntryRepository entries,
        IMarkdownRenderer       renderer,
        IMediaAssetRepository   media,
        IFieldTypeRegistry      registry)
    {
        _entries  = entries;
        _renderer = renderer;
        _media    = media;
        _registry = registry;
    }

    /// <summary>
    /// Get a published content entry by slug for public rendering.
    /// Returns 404 when no published entry exists for the slug, or when
    /// <paramref name="type"/> is supplied and does not match the entry's content type.
    /// </summary>
    [HttpGet("{**slug}")]
    [ProducesResponseType(typeof(PublishedContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublished(
        string slug,
        [FromQuery] string? type   = null,
        [FromQuery] string  locale = "en-US")
    {
        // Tolerate clients that percent-encode the whole slug (demo%2Fhome): routing
        // leaves %2F intact in a catch-all value, so unescape before the lookup.
        slug = Uri.UnescapeDataString(slug).Trim('/');
        if (slug.Length == 0) return NotFound();

        var entry = await _entries.GetPublishedBySlugAsync(slug, locale);
        if (entry is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(type)
            && !string.Equals(entry.ContentTypeName, type, StringComparison.OrdinalIgnoreCase))
        {
            // A news article requested through the standard-page template (or vice versa)
            // is a 404 for that route, exactly as the Next.js page handlers expect.
            return NotFound();
        }

        var fields = BuildFields(entry.FieldsJson, entry.RenderedFieldsJson, _renderer);
        await ExpandMediaReferencesAsync(fields, entry.ContentTypeName);

        return Ok(new PublishedContentResponse(
            entry.Id,
            entry.ContentTypeId,
            entry.ContentTypeName,
            entry.TemplateId,
            entry.Slug,
            entry.Locale,
            entry.Status,
            entry.VersionNumber,
            fields,
            entry.PublishedAt));
    }

    /// <summary>
    /// Replace MediaReference field values (asset id, or a numeric string) with the
    /// asset descriptor the public templates render. Unknown ids and URL strings are
    /// left untouched; the registry decides which fields are media references.
    /// </summary>
    private async Task ExpandMediaReferencesAsync(JsonObject fields, string contentTypeName)
    {
        var definition = _registry.GetByName(contentTypeName);
        if (definition is null) return;

        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

        foreach (var field in definition.Fields.Where(f => f.Type == FieldType.MediaReference))
        {
            if (fields[field.Name] is not JsonValue value) continue;

            long id;
            if (value.TryGetValue<long>(out var num)) id = num;
            else if (value.TryGetValue<string>(out var str) && long.TryParse(str, out var parsed)) id = parsed;
            else continue;

            var asset = await _media.GetByIdAsync(id);
            if (asset is null) continue;

            fields[field.Name] = new JsonObject
            {
                ["id"]         = asset.Id,
                ["storageUrl"] = $"{baseUrl}/api/v1/media/serve/{asset.Id}",
                ["altText"]    = asset.AltText ?? string.Empty,
                ["width"]      = asset.Width,
                ["height"]     = asset.Height,
                ["mimeType"]   = asset.MimeType,
            };
        }
    }

    /// <summary>
    /// Fields for the public payload: the raw FieldsJson object with renderedBody added.
    /// Prefers the publish-time RenderedFieldsJson["body"]; falls back to rendering the
    /// Markdown body now. Returns an empty object if FieldsJson is not a JSON object.
    /// </summary>
    public static JsonObject BuildFields(string fieldsJson, string? renderedFieldsJson, IMarkdownRenderer renderer)
    {
        JsonObject fields;
        try
        {
            fields = JsonNode.Parse(fieldsJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            fields = new JsonObject();
        }

        string? renderedBody = null;
        if (!string.IsNullOrWhiteSpace(renderedFieldsJson))
        {
            try
            {
                var rendered = JsonNode.Parse(renderedFieldsJson) as JsonObject;
                renderedBody = rendered?[BodyField]?.GetValue<string>();
            }
            catch (JsonException) { /* fall through to on-the-fly render */ }
            catch (InvalidOperationException) { /* body was not a string */ }
        }

        if (string.IsNullOrEmpty(renderedBody))
        {
            var body = fields[BodyField] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            if (body is not null)
                renderedBody = renderer.Render(body);
        }

        if (renderedBody is not null)
            fields[RenderedBodyField] = renderedBody;

        return fields;
    }
}

/// <summary>Public delivery payload for GET /api/v1/content/{slug}.</summary>
public sealed record PublishedContentResponse(
    long       Id,
    long       ContentTypeId,
    string     ContentTypeName,
    string?    TemplateId,
    string     Slug,
    string     Locale,
    string     Status,
    int        VersionNumber,
    JsonObject Fields,
    DateTime   PublishedAt);
