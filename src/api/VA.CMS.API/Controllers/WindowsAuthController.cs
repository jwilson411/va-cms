using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Handles the Windows Integrated Authentication login flow.
///
/// Active only when Auth:Mode=WindowsAuth is configured. On IIS, the
/// /api/auth/windows-login endpoint is protected by IIS Windows Authentication,
/// which means the browser supplies Kerberos/NTLM credentials automatically
/// on the intranet before the request reaches ASP.NET Core.
///
/// Flow:
///   1. Browser/SPA requests GET /api/auth/windows-login
///   2. IIS (or Negotiate middleware) challenges with 401 Negotiate
///   3. Browser responds with Kerberos/NTLM token
///   4. Negotiate middleware authenticates and populates User.Identity
///   5. This action reads the UPN, upserts the User row, issues CMS JWT + refresh cookie
///
/// The JWT and refresh token are identical in structure to the AzureAd path —
/// downstream code is auth-mode-agnostic.
/// </summary>
[ApiController]
[Route("api/auth")]
public class WindowsAuthController : ControllerBase
{
    private readonly IUserRepository     _users;
    private readonly IJwtService         _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IWebHostEnvironment  _env;
    private readonly AuthOptions          _authOptions;
    private readonly ILogger<WindowsAuthController> _logger;

    public WindowsAuthController(
        IUserRepository      users,
        IJwtService          jwt,
        IRefreshTokenService refreshTokens,
        IWebHostEnvironment  env,
        AuthOptions          authOptions,
        ILogger<WindowsAuthController> logger)
    {
        _users         = users;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _env           = env;
        _authOptions   = authOptions;
        _logger        = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/windows-login
    //
    // Requires Negotiate authentication. On IIS with Windows Authentication
    // enabled, the browser supplies credentials automatically. The Negotiate
    // middleware validates the ticket and populates User.Identity with the
    // Windows identity (Domain\Username or UPN).
    //
    // Returns the same {accessToken, expiresIn, tokenType} shape as the
    // AzureAd /api/auth/callback endpoint so the SPA needs no mode-awareness.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("windows-login")]
    [Authorize(AuthenticationSchemes = NegotiateDefaults.AuthenticationScheme)]
    public async Task<IActionResult> WindowsLogin()
    {
        // Guard: only active when Auth:Mode=WindowsAuth
        if (_authOptions.Mode != AuthMode.WindowsAuth)
            return NotFound();

        if (User?.Identity?.IsAuthenticated != true)
            return Unauthorized();

        // Extract UPN from the Windows identity.
        // WindowsIdentity.Name is typically "DOMAIN\username" or "user@domain.com".
        // We normalise to UPN (email) format for consistent lookup.
        var rawName = User.Identity.Name ?? string.Empty;
        var upn = NormaliseWindowsUpn(rawName);

        if (string.IsNullOrEmpty(upn))
        {
            _logger.LogWarning("WindowsAuth login: could not determine UPN from identity name '{RawName}'", rawName);
            return Unauthorized("Could not determine UPN from Windows identity.");
        }

        // The display name is the same as the UPN until the User table is
        // enriched from a directory lookup. UPN is used as ExternalId since
        // there is no AAD Object ID in Windows Auth mode.
        var displayName = upn;

        // Upsert the user row — ExternalId = UPN (canonical in Windows Auth mode).
        var userId = await _users.UpsertAsync(upn, upn, displayName);

        // Load the user to check IsActive
        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Unauthorized("Account is disabled.");

        // Resolve role assignments and issue CMS JWT
        var roles = await _users.GetRolesAsync(userId);
        var accessToken = _jwt.IssueAccessToken(user, roles);

        // Issue refresh token in httpOnly cookie (8 hr)
        var refreshToken = _refreshTokens.Issue(userId);
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(isProduction: _env.IsProduction(), lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        _logger.LogInformation("WindowsAuth login: issued JWT for {Upn} (userId={UserId})", upn, userId);

        return Ok(new
        {
            accessToken,
            expiresIn = 900, // 15 minutes in seconds
            tokenType = "Bearer",
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helper: normalise a Windows identity name to UPN.
    //
    // WindowsIdentity.Name can be:
    //   DOMAIN\username   → username@domain.local (best-effort; lowercase)
    //   user@domain.com   → user@domain.com (already UPN — return as-is)
    //   .\username        → username (local account; no domain)
    // ──────────────────────────────────────────────────────────────────────
    public static string NormaliseWindowsUpn(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        // Already UPN-style
        if (rawName.Contains('@'))
            return rawName.Trim().ToLowerInvariant();

        // DOMAIN\username → username@domain.local
        var parts = rawName.Split('\\', 2);
        if (parts.Length == 2)
        {
            var domain = parts[0].Trim();
            var user   = parts[1].Trim();
            if (!string.IsNullOrEmpty(user))
                return $"{user}@{domain}".ToLowerInvariant();
        }

        // Fallback: return as-is (lowercased)
        return rawName.Trim().ToLowerInvariant();
    }
}
