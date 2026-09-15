using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// DevBypass login endpoint.
///
/// POST /api/auth/dev-login
///   Header: X-Dev-User: {upn}
///   Returns: { accessToken, expiresIn, tokenType }
///
/// This endpoint is only registered and reachable when Auth:Mode=DevBypass.
/// Program.cs refuses to start with DevBypass when ASPNETCORE_ENVIRONMENT=Production.
///
/// Intended use:
///   - Local development: UI or CLI tools post here to obtain a JWT without AD.
///   - CI integration tests: include X-Dev-User in the test request pipeline.
///
/// The endpoint returns 401 if DevBypass is not active, or if the UPN is not
/// in the DevBypassAllowedUsers list (when that list is non-empty).
/// </summary>
[ApiController]
[Route("api/auth")]
public class DevBypassController : ControllerBase
{
    private readonly AuthOptions     _authOptions;
    private readonly IUserRepository _users;
    private readonly IJwtService     _jwt;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DevBypassController> _logger;

    public DevBypassController(
        AuthOptions     authOptions,
        IUserRepository users,
        IJwtService     jwt,
        IWebHostEnvironment env,
        ILogger<DevBypassController> logger)
    {
        _authOptions = authOptions;
        _users       = users;
        _jwt         = jwt;
        _env         = env;
        _logger      = logger;
    }

    /// <summary>
    /// Accepts an X-Dev-User header and issues a CMS JWT without AD.
    /// Returns 401 if DevBypass mode is not active.
    /// </summary>
    [HttpPost("dev-login")]
    [AllowAnonymous]
    public async Task<IActionResult> DevLogin()
    {
        // Hard guard: never serve this in Production regardless of config
        if (_env.IsProduction())
        {
            _logger.LogWarning("DevBypass dev-login attempted in Production — rejected.");
            return Unauthorized("DevBypass is not available in Production.");
        }

        if (_authOptions.Mode != AuthMode.DevBypass)
            return Unauthorized("DevBypass mode is not enabled. Set Auth:Mode=DevBypass in appsettings.Development.json.");

        if (!Request.Headers.TryGetValue("X-Dev-User", out var devUserHeader)
            || string.IsNullOrWhiteSpace(devUserHeader))
        {
            return BadRequest("X-Dev-User header is required.");
        }

        var upn = devUserHeader.ToString().Trim();

        // Validate against the allowed list (when non-empty)
        if (_authOptions.DevBypassAllowedUsers.Length > 0
            && !_authOptions.DevBypassAllowedUsers.Contains(upn, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("DevBypass: UPN '{Upn}' is not in DevBypassAllowedUsers.", upn);
            return Unauthorized($"UPN '{upn}' is not in the DevBypassAllowedUsers list.");
        }

        // Upsert the user row
        var userId = await _users.UpsertAsync(
            externalId:  $"devbypass:{upn}",
            email:       upn,
            displayName: upn.Split('@')[0]);

        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("DevBypass: user '{Upn}' is inactive.", upn);
            return Unauthorized("User account is disabled.");
        }

        // Issue JWT — developer role for broad local access
        var roles = new[]
        {
            new UserRoleAssignment
            {
                RoleId   = 0,
                RoleName = CmsRoles.Developer,
                SectionId         = null,
                SectionSlugPrefix = null,
            },
        };

        var accessToken = _jwt.IssueAccessToken(user, roles);

        _logger.LogInformation("DevBypass: issued JWT for '{Upn}'.", upn);

        return Ok(new
        {
            accessToken,
            expiresIn = 900,
            tokenType = "Bearer",
        });
    }
}
