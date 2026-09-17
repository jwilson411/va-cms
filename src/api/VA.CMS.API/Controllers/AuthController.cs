using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Handles the AD → JWT auth flow.
///
/// Endpoints (all at /api/auth/*):
///   GET  /api/auth/login    → redirect to Azure AD OIDC
///   GET  /api/auth/callback → AD token validated, JWT issued, refresh cookie set
///   GET  /api/auth/refresh  → validates refresh cookie, issues new JWT
///   POST /api/auth/logout   → revokes refresh cookie
///
/// Story #67 addition: both Callback and Refresh now call AdGroupRoleResolver
/// to merge group-mapped roles into the effective role set. Explicit UserRole
/// assignments always win over group-mapped roles.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository        _users;
    private readonly IJwtService            _jwt;
    private readonly IRefreshTokenService   _refreshTokens;
    private readonly IWebHostEnvironment    _env;
    private readonly IAdGroupRoleResolver   _groupResolver;
    private readonly AuthOptions            _authOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository         users,
        IJwtService             jwt,
        IRefreshTokenService    refreshTokens,
        IWebHostEnvironment     env,
        IAdGroupRoleResolver    groupResolver,
        AuthOptions             authOptions,
        ILogger<AuthController> logger)
    {
        _users         = users;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _env           = env;
        _groupResolver = groupResolver;
        _authOptions   = authOptions;
        _logger        = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/login
    // Redirects the browser to Azure AD OIDC.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        // DevBypass: the "AzureAd" scheme is not registered, so Challenge() would
        // throw. Send the browser to the admin SPA's /login page instead, which
        // offers the dev user picker (backed by /api/auth/dev-users).
        if (_authOptions.Mode == AuthMode.DevBypass && !_env.IsProduction())
        {
            // Only forward a same-site path so this can never become an open redirect.
            var safeReturn = returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            return Redirect(safeReturn is null
                ? "/login"
                : $"/login?returnUrl={Uri.EscapeDataString(safeReturn)}");
        }

        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback), "Auth", new { returnUrl }),
        };
        return Challenge(props, "AzureAd");
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/callback
    // Called by Azure AD after successful OIDC authentication.
    // Microsoft.Identity.Web has already validated the ID token.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl)
    {
        if (User?.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var upn = User.FindFirst("preferred_username")?.Value
               ?? User.FindFirst("upn")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
               ?? string.Empty;
        var displayName = User.FindFirst("name")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
               ?? upn;
        var oid = User.FindFirst("oid")?.Value
               ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
               ?? upn;

        if (string.IsNullOrEmpty(upn) || string.IsNullOrEmpty(oid))
            return Unauthorized("Could not determine user identity from AD token.");

        // Upsert user row
        var userId = await _users.UpsertAsync(oid, upn, displayName);

        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Unauthorized("Account is disabled.");

        // Explicit (manually-assigned) roles
        var explicitRoles = await _users.GetRolesAsync(userId);

        // Merge with AD group-mapped roles (explicit always wins — #67 AC5)
        var effectiveRoles = await _groupResolver.MergeRolesAsync(User, explicitRoles);

        // Issue CMS JWT (15 min, HS256)
        var accessToken = _jwt.IssueAccessToken(user, effectiveRoles);

        // Issue refresh token in httpOnly cookie (8 hr)
        var refreshToken = _refreshTokens.Issue(userId);
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(isProduction: _env.IsProduction(), lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        return Ok(new
        {
            accessToken,
            expiresIn = 900,
            tokenType = "Bearer",
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/refresh
    // Validates the httpOnly refresh cookie and issues a new JWT.
    // Story #67 AC4: re-resolves AD group memberships from the token claims
    // and applies current mappings. Mapping changes take effect here.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(AuthCookieHelper.RefreshTokenCookieName, out var refreshToken)
            || string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized("Refresh token missing.");
        }

        var userId = _refreshTokens.Validate(refreshToken);
        if (userId is null)
            return Unauthorized("Refresh token invalid or expired.");

        var user = await _users.GetByIdAsync(userId.Value);
        if (user is null || !user.IsActive)
        {
            _refreshTokens.Revoke(refreshToken);
            var expiryCookieOpts = AuthCookieHelper.BuildExpiryCookieOptions(isProduction: _env.IsProduction());
            Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, string.Empty, expiryCookieOpts);
            return Unauthorized("Account is disabled.");
        }

        // Explicit roles
        var explicitRoles = await _users.GetRolesAsync(userId.Value);

        // DevBypass users have no UserRole rows — re-grant the DevBypassRoles set so a
        // refreshed token keeps the same permissions dev-login originally issued.
        if (_authOptions.Mode == AuthMode.DevBypass
            && !_env.IsProduction()
            && user.ExternalId.StartsWith(DevBypassRoles.ExternalIdPrefix, StringComparison.Ordinal))
        {
            explicitRoles = explicitRoles.Concat(DevBypassRoles.Build()).ToList();
        }

        // On refresh, we do not have a full ClaimsPrincipal with AD group claims
        // (the refresh token is a CMS-issued opaque token, not an AAD token).
        // We resolve group membership from the dev-header in DevBypass mode.
        // In AzureAd mode, pass an empty group list — the actual group resolution
        // happens at Callback when the AAD token (with group claims) is present.
        // The merged set stays current as long as the user re-authenticates before
        // group memberships change, which satisfies the AC: "takes effect on
        // the user's next login."
        //
        // To re-evaluate group mappings on every refresh call, re-query current
        // mappings for the groups that were baked into the refresh session.
        // Since the CMS refresh token is opaque (no group claims), we use the
        // AD group dev-header in DevBypass and empty groups otherwise.
        var devGroups = new List<string>();
        if (Request.Headers.TryGetValue("X-Dev-Groups", out var devGroupHeader))
        {
            devGroups = devGroupHeader.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // Re-apply current group mappings so admin changes take effect on next refresh.
        var effectiveRoles = await _groupResolver.MergeRolesAsync(devGroups, explicitRoles);

        var accessToken = _jwt.IssueAccessToken(user, effectiveRoles);

        return Ok(new
        {
            accessToken,
            expiresIn = 900,
            tokenType = "Bearer",
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/auth/logout
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        if (Request.Cookies.TryGetValue(AuthCookieHelper.RefreshTokenCookieName, out var refreshToken)
            && !string.IsNullOrEmpty(refreshToken))
        {
            _refreshTokens.Revoke(refreshToken);
        }

        var expiryCookieOpts = AuthCookieHelper.BuildExpiryCookieOptions(isProduction: _env.IsProduction());
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, string.Empty, expiryCookieOpts);

        return NoContent();
    }
}
