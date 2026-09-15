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
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository     _users;
    private readonly IJwtService         _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IWebHostEnvironment  _env;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository      users,
        IJwtService          jwt,
        IRefreshTokenService refreshTokens,
        IWebHostEnvironment  env,
        ILogger<AuthController> logger)
    {
        _users         = users;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _env           = env;
        _logger        = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/login
    // Redirects the browser to Azure AD OIDC. The middleware (wired in
    // Program.cs) handles the redirect; we just challenge here.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
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
    // We upsert the user, resolve roles, issue JWT + refresh cookie.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl)
    {
        // At this point, Microsoft.Identity.Web has validated the Azure AD token
        // and populated User.Claims with the AAD identity.
        if (User?.Identity?.IsAuthenticated != true)
            return Unauthorized();

        // Extract identity claims set by Microsoft.Identity.Web
        var upn         = User.FindFirst("preferred_username")?.Value
                       ?? User.FindFirst("upn")?.Value
                       ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                       ?? string.Empty;
        var displayName = User.FindFirst("name")?.Value
                       ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                       ?? upn;
        var oid         = User.FindFirst("oid")?.Value
                       ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                       ?? upn;

        if (string.IsNullOrEmpty(upn) || string.IsNullOrEmpty(oid))
            return Unauthorized("Could not determine user identity from AD token.");

        // Upsert user row by external ID (AAD Object ID)
        var userId = await _users.UpsertAsync(oid, upn, displayName);

        // Load the user to check IsActive
        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Unauthorized("Account is disabled.");

        // Resolve AD group → CMS role mappings
        var roles = await _users.GetRolesAsync(userId);

        // Issue CMS JWT (15 min, HS256)
        var accessToken = _jwt.IssueAccessToken(user, roles);

        // Issue refresh token in httpOnly cookie (8 hr)
        var refreshToken = _refreshTokens.Issue(userId);
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(isProduction: _env.IsProduction());
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        // Return the JWT to the SPA (stored in memory, not localStorage/sessionStorage).
        // The SPA reads this from the response body and holds it in a React state/context.
        return Ok(new
        {
            accessToken,
            expiresIn   = 900, // 15 minutes in seconds
            tokenType   = "Bearer",
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/refresh
    // Validates the httpOnly refresh cookie and issues a new JWT.
    // Called silently by the SPA before the access token expires.
    // Returns 401 if the refresh token is absent, expired, or the AD account
    // was disabled (IsActive = false).
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

        // Load and check the user's active status.
        // If the AD account has been disabled, return 401 so the SPA forces re-login.
        var user = await _users.GetByIdAsync(userId.Value);
        if (user is null || !user.IsActive)
        {
            _refreshTokens.Revoke(refreshToken);
            var expiryCookieOpts = AuthCookieHelper.BuildExpiryCookieOptions(isProduction: _env.IsProduction());
            Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, string.Empty, expiryCookieOpts);
            return Unauthorized("Account is disabled.");
        }

        // Resolve current role assignments and issue a fresh JWT
        var roles = await _users.GetRolesAsync(userId.Value);
        var accessToken = _jwt.IssueAccessToken(user, roles);

        return Ok(new
        {
            accessToken,
            expiresIn = 900,
            tokenType = "Bearer",
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/auth/logout
    // Revokes the refresh token and clears the cookie.
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
