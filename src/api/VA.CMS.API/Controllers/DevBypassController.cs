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
///   Also sets the httpOnly refresh cookie so the admin SPA's silent-refresh
///   flow (GET /api/auth/refresh) works exactly as it does after an AD login.
///
/// GET /api/auth/dev-users
///   Returns: { users: [upn, ...] } — the DevBypassAllowedUsers list, so the
///   admin SPA login page can offer a dev user picker. 404 unless DevBypass is active.
///
/// This endpoint is only registered and reachable when Auth:Mode=DevBypass.
/// Program.cs refuses to start with DevBypass outside ASPNETCORE_ENVIRONMENT=Development (#164).
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
    private readonly AuthOptions          _authOptions;
    private readonly IUserRepository      _users;
    private readonly IJwtService          _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IWebHostEnvironment  _env;
    private readonly IAuditLogRepository  _audit;
    private readonly ILogger<DevBypassController> _logger;

    public DevBypassController(
        AuthOptions          authOptions,
        IUserRepository      users,
        IJwtService          jwt,
        IRefreshTokenService refreshTokens,
        IWebHostEnvironment  env,
        IAuditLogRepository  audit,
        ILogger<DevBypassController> logger)
    {
        _authOptions   = authOptions;
        _users         = users;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _env           = env;
        _audit         = audit;
        _logger        = logger;
    }

    private bool DevBypassActive =>
        _env.IsDevelopment() && _authOptions.Mode == AuthMode.DevBypass;

    /// <summary>
    /// Lists the UPNs permitted for DevBypass sign-in. Used by the admin SPA login
    /// page to render a dev user picker. 404 when DevBypass is not active so the
    /// SPA falls back to the normal AD login flow.
    /// </summary>
    [HttpGet("dev-users")]
    [AllowAnonymous]
    public IActionResult DevUsers()
    {
        if (!DevBypassActive)
            return NotFound();

        return Ok(new { users = _authOptions.DevBypassAllowedUsers });
    }

    /// <summary>
    /// Accepts an X-Dev-User header and issues a CMS JWT without AD.
    /// Returns 401 if DevBypass mode is not active.
    /// </summary>
    [HttpPost("dev-login")]
    [AllowAnonymous]
    public async Task<IActionResult> DevLogin()
    {
        // Hard guard: never serve this outside Development regardless of config (#164)
        if (!_env.IsDevelopment())
        {
            _logger.LogWarning("DevBypass dev-login attempted in {Environment} — rejected.", _env.EnvironmentName);
            return Unauthorized("DevBypass is only available in the Development environment.");
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
            await _audit.WriteAsync(null, AuthAudit.EntityType, 0, AuthAudit.LogonFailure,
                AuthAudit.Diff(new { mode = "DevBypass", upn, reason = "not_allowed" }), AuditOutcome.Failure);
            return Unauthorized($"UPN '{upn}' is not in the DevBypassAllowedUsers list.");
        }

        // Upsert the user row
        var userId = await _users.UpsertAsync(
            externalId:  $"{DevBypassRoles.ExternalIdPrefix}{upn}",
            email:       upn,
            displayName: upn.Split('@')[0]);

        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("DevBypass: user '{Upn}' is inactive.", upn);
            await _audit.WriteAsync(userId, AuthAudit.EntityType, userId, AuthAudit.LogonFailure,
                AuthAudit.Diff(new { mode = "DevBypass", upn, reason = "account_disabled" }), AuditOutcome.Failure);
            return Unauthorized("User account is disabled.");
        }

        // Issue JWT — SystemAdmin + Developer so the whole admin SPA is usable locally
        var accessToken = _jwt.IssueAccessToken(user, DevBypassRoles.Build());

        // Issue refresh token in httpOnly cookie — same as the AD callback,
        // so the SPA can silently refresh on reload instead of bouncing to login.
        var refreshToken = await _refreshTokens.IssueAsync(userId, null, AuthAudit.ClientOf(HttpContext));
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(AuthCookieHelper.SecureFor(_env), lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        _logger.LogInformation("DevBypass: issued JWT for '{Upn}'.", upn);
        // The SPA sends X-System-Use-Ack: 1 once the notice was acknowledged (#164).
        var acknowledged = Request.Headers[AuthAudit.SystemUseAckHeader].ToString() == "1";
        await _audit.WriteAsync(userId, AuthAudit.EntityType, userId, AuthAudit.Logon,
            AuthAudit.Diff(new { mode = "DevBypass", upn, systemUseAcknowledged = acknowledged }));

        return Ok(new
        {
            accessToken,
            expiresIn = (int)_jwt.AccessTokenLifetime.TotalSeconds,
            tokenType = "Bearer",
        });
    }
}
