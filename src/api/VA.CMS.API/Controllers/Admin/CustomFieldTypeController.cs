using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.ContentTypes;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Admin endpoint for listing registered custom field type plugins (FR-DEV-05 / issue #27).
///
///   GET /api/v1/admin/custom-field-types
///
/// The admin form editor calls this on startup to know which custom React
/// components to activate. Each entry contains the <c>typeName</c> that is used
/// as the key when calling <c>registerCustomField(typeName, Component)</c>
/// in the frontend registry.
///
/// All CMS roles may read this endpoint (CanRead policy).
/// </summary>
[ApiController]
[Route("api/v1/admin/custom-field-types")]
[Authorize(Policy = CmsRoles.Policies.CanRead)]
public class CustomFieldTypeController : ControllerBase
{
    private readonly ICustomFieldTypeRegistry _registry;

    public CustomFieldTypeController(ICustomFieldTypeRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Returns all registered custom field type plugins.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CustomFieldTypeDto>), StatusCodes.Status200OK)]
    public IActionResult List()
    {
        var types = _registry.GetAll()
            .Select(t => new CustomFieldTypeDto(
                TypeName:    t.TypeName,
                StorageType: t.StorageType.Name))
            .ToList();

        return Ok(types);
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>Summary of a registered custom field type plugin.</summary>
public sealed record CustomFieldTypeDto(
    /// <summary>Unique machine-readable type name (matches the React registry key).</summary>
    string TypeName,
    /// <summary>.NET storage type name (e.g. "String", "Int32").</summary>
    string StorageType);
