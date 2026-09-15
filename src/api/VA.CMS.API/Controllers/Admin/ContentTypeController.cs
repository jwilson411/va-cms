using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.ContentTypes;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Admin content type browser endpoints (FR-SCHEMA-06 / issue #26).
///
///   GET /api/v1/admin/content-types          — list all registered types
///   GET /api/v1/admin/content-types/{name}   — get type definition + ordered field schema
///
/// All CMS roles may read the schema browser (CanRead policy).
/// </summary>
[ApiController]
[Route("api/v1/admin/content-types")]
[Authorize(Policy = CmsRoles.Policies.CanRead)]
public class ContentTypeController : ControllerBase
{
    private readonly IFieldTypeRegistry _registry;

    public ContentTypeController(IFieldTypeRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Returns all registered content types with a summary of each type's fields.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ContentTypeSummaryDto>), StatusCodes.Status200OK)]
    public IActionResult List()
    {
        var types = _registry.GetAll()
            .Select(t => new ContentTypeSummaryDto(
                Name:         t.Name,
                DisplayName:  t.DisplayName,
                Description:  null,          // ContentTypeDefinitionBase has no Description — null for now
                FieldCount:   t.Fields.Count,
                AllowWorkflow: t.AllowWorkflow,
                Fields:       t.Fields.Select(MapField).ToList()))
            .ToList();

        return Ok(types);
    }

    /// <summary>
    /// Returns the full definition for a single content type including its
    /// ordered field list with name, type, required flag, and constraints.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ContentTypeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetByName(string name)
    {
        var type = _registry.GetByName(name);
        if (type is null)
            return NotFound(new { error = $"Content type '{name}' is not registered." });

        var dto = new ContentTypeDetailDto(
            Name:          type.Name,
            DisplayName:   type.DisplayName,
            Description:   null,
            TemplateId:    type.TemplateId,
            AllowWorkflow: type.AllowWorkflow,
            Fields:        type.Fields.Select(MapField).ToList());

        return Ok(dto);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FieldSchemaDto MapField(Infrastructure.ContentTypes.FieldDefinition f) =>
        new(
            Name:      f.Name,
            Label:     f.Label,
            Type:      f.Type.ToString(),
            Required:  f.Required,
            MaxLength: f.MaxLength);
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Summary row returned by the list endpoint.</summary>
public sealed record ContentTypeSummaryDto(
    string Name,
    string DisplayName,
    string? Description,
    int FieldCount,
    bool AllowWorkflow,
    IReadOnlyList<FieldSchemaDto> Fields);

/// <summary>Full detail returned by the single-type endpoint.</summary>
public sealed record ContentTypeDetailDto(
    string Name,
    string DisplayName,
    string? Description,
    string? TemplateId,
    bool AllowWorkflow,
    IReadOnlyList<FieldSchemaDto> Fields);

/// <summary>A single field definition in the schema viewer.</summary>
public sealed record FieldSchemaDto(
    string Name,
    string Label,
    string Type,
    bool Required,
    int? MaxLength);
