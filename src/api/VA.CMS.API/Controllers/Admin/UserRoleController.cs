using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// User and role management endpoints.
/// Story #23 RBAC gates (BRD FR-USERS-04):
///
///   GET    /api/v1/admin/users                          — CanAdminSystem (SystemAdmin)
///   GET    /api/v1/admin/users/{id}/roles               — CanAdminSystem
///   POST   /api/v1/admin/users/{id}/roles               — CanAdminSystem
///   DELETE /api/v1/admin/users/{userId}/roles/{roleId}  — CanAdminSystem
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = CmsRoles.Policies.CanAdminSystem)]
public class UserRoleController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IUserRoleRepository _userRoles;

    public UserRoleController(IUserRepository users, IUserRoleRepository userRoles)
    {
        _users     = users;
        _userRoles = userRoles;
    }

    /// <summary>List active users.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<User>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var users = await _userRoles.ListAsync(search, isActive: true, page, pageSize);
        return Ok(users);
    }

    /// <summary>Get all role assignments for a user.</summary>
    [HttpGet("{userId:long}/roles")]
    [ProducesResponseType(typeof(IEnumerable<UserRoleAssignment>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserRoles(long userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();

        var roles = await _users.GetRolesAsync(userId);
        return Ok(roles);
    }

    /// <summary>Assign a role to a user, optionally scoped to a section.</summary>
    [HttpPost("{userId:long}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        long userId,
        [FromBody] AssignRoleRequest request)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();

        // Look up the actor (granter) from JWT
        var grantedByClaimVal = User.FindFirst("cms_user_id")?.Value;
        long.TryParse(grantedByClaimVal, out var grantedById);

        await _userRoles.AssignRoleAsync(userId, request.RoleId, grantedById, request.SectionId);
        return NoContent();
    }

    /// <summary>Revoke a role from a user.</summary>
    [HttpDelete("{userId:long}/roles/{roleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeRole(
        long userId,
        long roleId,
        [FromQuery] long? sectionId = null)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();

        await _userRoles.RevokeRoleAsync(userId, roleId, sectionId);
        return NoContent();
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record AssignRoleRequest(long RoleId, long? SectionId = null);
