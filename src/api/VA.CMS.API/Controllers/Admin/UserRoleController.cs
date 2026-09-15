using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// User directory and role management endpoints.
/// Story #56 — Build user directory and role assignment admin UI.
/// BRD FR-USERS-03 and FR-USERS-04.
///
///   GET    /api/v1/admin/users                           — CanAdminSystem (SystemAdmin)
///   GET    /api/v1/admin/users/{id}                      — CanAdminSystem
///   GET    /api/v1/admin/users/{id}/roles                — CanAdminSystem
///   POST   /api/v1/admin/users/{id}/roles                — CanAdminSystem
///   DELETE /api/v1/admin/users/{userId}/roles/{roleId}   — CanAdminSystem
///   POST   /api/v1/admin/users/{id}/deactivate           — CanAdminSystem
///   GET    /api/v1/admin/roles                           — CanAdminSystem
///   GET    /api/v1/admin/sections                        — CanAdminSystem
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = CmsRoles.Policies.CanAdminSystem)]
public class UserRoleController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IUserRoleRepository _userRoles;
    private readonly IRoleRepository _roles;

    public UserRoleController(
        IUserRepository users,
        IUserRoleRepository userRoles,
        IRoleRepository roles)
    {
        _users     = users;
        _userRoles = userRoles;
        _roles     = roles;
    }

    // ── User directory ─────────────────────────────────────────────────────────

    /// <summary>List active users with optional name/email search.</summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(IEnumerable<User>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var users = await _userRoles.ListAsync(search, isActive: true, page, pageSize);
        return Ok(users);
    }

    /// <summary>Get a single user with their role assignments.</summary>
    [HttpGet("users/{userId:long}")]
    [ProducesResponseType(typeof(UserDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(long userId)
    {
        var detail = await _userRoles.GetDetailAsync(userId);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    // ── Role assignments ───────────────────────────────────────────────────────

    /// <summary>Get all role assignments for a user.</summary>
    [HttpGet("users/{userId:long}/roles")]
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
    [HttpPost("users/{userId:long}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        long userId,
        [FromBody] AssignRoleRequest request)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();

        var grantedByClaimVal = User.FindFirst("cms_user_id")?.Value;
        long.TryParse(grantedByClaimVal, out var grantedById);

        await _userRoles.AssignRoleAsync(userId, request.RoleId, grantedById, request.SectionId);
        return NoContent();
    }

    /// <summary>Revoke a role from a user.</summary>
    [HttpDelete("users/{userId:long}/roles/{roleId:long}")]
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

    // ── User lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Deactivate a user — sets IsActive=0.
    /// The next login attempt returns 403 because the auth pipeline checks IsActive.
    /// </summary>
    [HttpPost("users/{userId:long}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateUser(long userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();

        var actorClaimVal = User.FindFirst("cms_user_id")?.Value;
        long.TryParse(actorClaimVal, out var actorId);

        await _userRoles.DeactivateAsync(userId, actorId);
        return NoContent();
    }

    // ── Lookup data ────────────────────────────────────────────────────────────

    /// <summary>List all roles (for role assignment dropdown).</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IEnumerable<RoleRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRoles()
    {
        var roles = await _roles.ListAllAsync();
        return Ok(roles);
    }

    /// <summary>List all content sections (for section-scoped role assignment).</summary>
    [HttpGet("sections")]
    [ProducesResponseType(typeof(IEnumerable<ContentSectionRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSections()
    {
        var sections = await _roles.ListSectionsAsync();
        return Ok(sections);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record AssignRoleRequest(long RoleId, long? SectionId = null);
