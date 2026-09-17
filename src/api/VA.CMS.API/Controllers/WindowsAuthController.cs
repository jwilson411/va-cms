using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

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
///   5. This action reads the UPN, upserts the User row, merges AD-group-mapped
///      roles (#67/#153), issues CMS JWT + refresh cookie
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
    private readonly IAdGroupRoleResolver _groupResolver;
    private readonly AuthOptions          _authOptions;
    private readonly ISiteSettingsService _settings;
    private readonly IAuditLogRepository  _audit;
    private readonly ILogger<WindowsAuthController> _logger;

    public WindowsAuthController(
        IUserRepository      users,
        IJwtService          jwt,
        IRefreshTokenService refreshTokens,
        IWebHostEnvironment  env,
        IAdGroupRoleResolver groupResolver,
        AuthOptions          authOptions,
        ISiteSettingsService settings,
        IAuditLogRepository  audit,
        ILogger<WindowsAuthController> logger)
    {
        _users         = users;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _env           = env;
        _groupResolver = groupResolver;
        _authOptions   = authOptions;
        _settings      = settings;
        _audit         = audit;
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
    // Two callers:
    //   - API clients / tests call it directly and get {accessToken, expiresIn, tokenType}.
    //   - The browser arrives via GET /api/auth/login?returnUrl=… (on-prem flow); with
    //     returnUrl present the cms_rt cookie is set and the response is a 302 to that
    //     local path, and the SPA bootstraps through its silent refresh — no token in
    //     a URL, body or history entry, exactly like the OIDC callback (#154).
    //     ack=1 travels with it from /api/auth/login: the system-use notice was
    //     acknowledged on the SPA page (#164) and the Logon audit row says so.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("windows-login")]
    [Authorize(AuthenticationSchemes = NegotiateDefaults.AuthenticationScheme,
               Policy = CmsRoles.Policies.AuthenticatedOnly)]
    public async Task<IActionResult> WindowsLogin([FromQuery] string? returnUrl = null, [FromQuery] string? ack = null)
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
            await LogonFailureAsync(null, rawName, "identity_incomplete");
            return Unauthorized("Could not determine UPN from Windows identity.");
        }

        // The display name is the same as the UPN until the User table is
        // enriched from a directory lookup. UPN is used as ExternalId since
        // there is no AAD Object ID in Windows Auth mode.
        var displayName = upn;

        // #155: unless auto-provisioning is on, only pre-created users may sign in.
        if (!_settings.GetBool(SiteSettingKeys.AuthAutoProvisionUsers)
            && await _users.GetByExternalIdAsync(upn) is null)
        {
            _logger.LogWarning(
                "WindowsAuth login rejected: {Upn} is not a provisioned CMS user and auth.autoProvisionUsers is off.", upn);
            await LogonFailureAsync(null, upn, "not_provisioned");
            return StatusCode(StatusCodes.Status403Forbidden,
                "This account has not been provisioned in the CMS. Contact your site administrator.");
        }

        // Upsert the user row — ExternalId = UPN (canonical in Windows Auth mode).
        var userId = await _users.UpsertAsync(upn, upn, displayName);

        // Load the user to check IsActive
        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            await LogonFailureAsync(userId, upn, "account_disabled");
            return Unauthorized("Account is disabled.");
        }

        // Explicit roles merged with AD-group-mapped roles. Negotiate exposes the
        // user's group SIDs; the resolver also translates them to DOMAIN\Group names
        // on Windows so mappings may be keyed by either (#153).
        var explicitRoles = await _users.GetRolesAsync(userId);
        var adGroups      = _groupResolver.ExtractGroups(User);
        var roles         = await _groupResolver.MergeRolesAsync(adGroups, explicitRoles);
        var accessToken   = _jwt.IssueAccessToken(user, roles);

        // Issue refresh token in httpOnly cookie; the login-time groups travel with it
        var refreshToken = await _refreshTokens.IssueAsync(userId, adGroups, AuthAudit.ClientOf(HttpContext));
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(AuthCookieHelper.SecureFor(_env), lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        _logger.LogInformation("WindowsAuth login: session issued for userId={UserId} ({GroupCount} groups).", userId, adGroups.Count);
        await _audit.WriteAsync(userId, AuthAudit.EntityType, userId, AuthAudit.Logon,
            AuthAudit.Diff(new { mode = "WindowsAuth", upn, systemUseAcknowledged = ack == "1", groups = adGroups.Count }));

        if (!string.IsNullOrEmpty(returnUrl))
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");

        return Ok(new
        {
            accessToken,
            expiresIn = (int)_jwt.AccessTokenLifetime.TotalSeconds,
            tokenType = "Bearer",
        });
    }

    private Task LogonFailureAsync(long? userId, string upn, string reason)
        => _audit.WriteAsync(userId, AuthAudit.EntityType, userId ?? 0, AuthAudit.LogonFailure,
            AuthAudit.Diff(new { mode = "WindowsAuth", upn, reason }), AuditOutcome.Failure);

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
