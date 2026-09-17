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
///
/// #165: the mutations are audited by usp_AdGroupMapping_Upsert / _Delete themselves,
/// like every other mutating stored procedure, rather than by this controller.
/// </summary>
[ApiController]
[Route("api/v1/admin/settings/ad-group-mappings")]
[Authorize(Policy = CmsRoles.Policies.CanAdminSystem)]
public class AdminSettingsController : ControllerBase
{
    private readonly IAdGroupMappingRepository _mappings;
    private readonly IRbacService              _rbac;

    public AdminSettingsController(
        IAdGroupMappingRepository mappings,
        IRbacService              rbac)
    {
        _mappings = mappings;
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

        // Audited as AdGroupRoleMapping/AdGroupMappingUpserted inside the stored
        // procedure, in the same transaction as the write (#165).
        var id = await _mappings.UpsertAsync(request.AdGroup.Trim(), request.RoleId, actorId);

        return CreatedAtAction(nameof(List), new CreateMappingResponse(id));
    }

    /// <summary>Delete an AD group → role mapping by Id.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id)
    {
        // Audited as AdGroupRoleMapping/AdGroupMappingDeleted inside the stored procedure;
        // the actor comes from the request's SESSION_CONTEXT (#165).
        await _mappings.DeleteAsync(id);

        return NoContent();
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Request body for creating an AD group mapping.</summary>
public sealed record CreateAdGroupMappingRequest(string AdGroup, long RoleId);

/// <summary>Response body after a successful create.</summary>
public sealed record CreateMappingResponse(long Id);
