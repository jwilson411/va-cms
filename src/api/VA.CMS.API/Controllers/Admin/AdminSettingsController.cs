using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Admin Settings — AD Group → CMS Role Mapping.
///
/// Story #67 (BRD FR-USERS-01b): admins configure which AD groups map to
/// which CMS roles. Mappings are applied at each login / token refresh.
///
/// Endpoints (all require SystemAdmin):
///   GET    /api/v1/admin/settings/ad-group-mappings
///   POST   /api/v1/admin/settings/ad-group-mappings
///   DELETE /api/v1/admin/settings/ad-group-mappings/{id}
/// </summary>
[ApiController]
[Route("api/v1/admin/settings/ad-group-mappings")]
[Authorize(Policy = CmsRoles.Policies.CanAdminSystem)]
public class AdminSettingsController : ControllerBase
{
    private readonly IAdGroupMappingRepository _mappings;
    private readonly IAuditLogRepository       _audit;
    private readonly IRbacService              _rbac;

    public AdminSettingsController(
        IAdGroupMappingRepository mappings,
        IAuditLogRepository       audit,
        IRbacService              rbac)
    {
        _mappings = mappings;
        _audit    = audit;
        _rbac     = rbac;
    }

    /// <summary>List all AD group → role mappings.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AdGroupRoleMappingRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var rows = await _mappings.ListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Create (or re-acknowledge) an AD group → role mapping.
    /// Idempotent: if the (AdGroup, RoleId) pair already exists the call
    /// succeeds and returns the existing record's Id.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateMappingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAdGroupMappingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AdGroup))
            return BadRequest("AdGroup is required.");
        if (request.RoleId <= 0)
            return BadRequest("RoleId is required.");

        var actorId = _rbac.GetUserId(User) ?? 0;

        var id = await _mappings.UpsertAsync(request.AdGroup.Trim(), request.RoleId, actorId);

        // Audit: "AD group mapping created/updated by [admin]" (AC requirement)
        await _audit.WriteAsync(
            actorId,
            entityType: "AdGroupRoleMapping",
            entityId:   id,
            action:     "AdGroupMappingUpserted",
            diffJson:   $"{{\"adGroup\":\"{request.AdGroup.Trim()}\",\"roleId\":{request.RoleId}}}");

        return CreatedAtAction(nameof(List), new CreateMappingResponse(id));
    }

    /// <summary>Delete an AD group → role mapping by Id.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id)
    {
        var actorId = _rbac.GetUserId(User) ?? 0;

        await _mappings.DeleteAsync(id);

        // Audit
        await _audit.WriteAsync(
            actorId,
            entityType: "AdGroupRoleMapping",
            entityId:   id,
            action:     "AdGroupMappingDeleted",
            diffJson:   null);

        return NoContent();
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Request body for creating an AD group mapping.</summary>
public sealed record CreateAdGroupMappingRequest(string AdGroup, long RoleId);

/// <summary>Response body after a successful create.</summary>
public sealed record CreateMappingResponse(long Id);
