using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Handles the AD → JWT auth flow.
///
/// Endpoints (all at /api/auth/*):
///   GET  /api/auth/login    → redirect to Azure AD OIDC
///   GET  /api/auth/callback → AAD session cookie validated, refresh cookie set, 302 into the SPA
///   GET  /api/auth/refresh  → validates refresh cookie, issues new JWT
///   POST /api/auth/logout   → revokes refresh cookie; returns the AAD sign-out URL when enabled
///   GET  /api/auth/signout  → front-channel sign-out of the AAD session (browser navigation)
///
/// Story #67 addition: both Callback and Refresh now call AdGroupRoleResolver
/// to merge group-mapped roles into the effective role set. Explicit UserRole
/// assignments always win over group-mapped roles.
///
/// #153: the groups observed at login are persisted with the refresh session.
/// Refresh re-applies the current mappings to those groups; the X-Dev-Groups
/// header is honoured only under DevBypass in the Development environment.
///
/// #154: the OIDC handler owns /signin-oidc and signs the AAD identity into the
/// AzureAdCookies scheme; Callback reads that cookie rather than User (whose
/// default scheme is JWT bearer), never returns the access token to the browser,
/// and redirects to a validated local returnUrl. The SPA bootstraps via silent
/// refresh on load.
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
    private readonly ISiteSettingsService   _settings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository         users,
        IJwtService             jwt,
        IRefreshTokenService    refreshTokens,
        IWebHostEnvironment     env,
        IAdGroupRoleResolver    groupResolver,
        AuthOptions             authOptions,
        ISiteSettingsService    settings,
        ILogger<AuthController> logger)
    {
        _users         = users;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _env           = env;
        _groupResolver = groupResolver;
        _authOptions   = authOptions;
        _settings      = settings;
        _logger        = logger;
    }

    private bool AzureAdActive => _authOptions.Mode == AuthMode.AzureAd;

    /// <summary>Only ever redirect to a same-site path so login/logout can never become an open redirect.</summary>
    private string SafeLocal(string? returnUrl, string fallback = "/")
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : fallback;

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/login
    // Redirects the browser to Azure AD OIDC.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        var safeReturn = SafeLocal(returnUrl, fallback: string.Empty);

        // DevBypass: the "AzureAd" scheme is not registered, so Challenge() would
        // throw. Send the browser to the admin SPA's /login page instead, which
        // offers the dev user picker (backed by /api/auth/dev-users).
        if (_authOptions.Mode == AuthMode.DevBypass && !_env.IsProduction())
        {
            return Redirect(safeReturn.Length == 0
                ? "/login"
                : $"/login?returnUrl={Uri.EscapeDataString(safeReturn)}");
        }

        if (!AzureAdActive)
            return NotFound();

        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback), "Auth",
                safeReturn.Length == 0 ? null : new { returnUrl = safeReturn }),
        };
        return Challenge(props, AzureAdSchemes.OpenIdConnect);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/callback
    // Reached after the OIDC handler has processed /signin-oidc and signed the
    // AAD identity into the AzureAdCookies scheme. Issues the CMS refresh cookie
    // and sends the browser into the SPA — the access token never appears in a
    // URL, response body or history entry (#154).
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl)
    {
        if (!AzureAdActive)
            return NotFound();

        var aad = await HttpContext.AuthenticateAsync(AzureAdSchemes.Cookie);
        var principal = aad.Principal;
        if (!aad.Succeeded || principal?.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var upn = principal.FindFirst("preferred_username")?.Value
               ?? principal.FindFirst("upn")?.Value
               ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
               ?? string.Empty;
        var displayName = principal.FindFirst("name")?.Value
               ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
               ?? upn;
        var oid = principal.FindFirst("oid")?.Value
               ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
               ?? upn;

        if (string.IsNullOrEmpty(upn) || string.IsNullOrEmpty(oid))
            return Unauthorized("Could not determine user identity from AD token.");

        // #155: unless auto-provisioning is on, only identities an administrator has
        // already created may sign in. Unknown tenant users get a clear message on
        // the SPA login page rather than an empty, role-less session.
        if (!_settings.GetBool(SiteSettingKeys.AuthAutoProvisionUsers)
            && await _users.GetByExternalIdAsync(oid) is null)
        {
            _logger.LogWarning(
                "AzureAd login rejected: {Upn} (oid {Oid}) is not a provisioned CMS user and auth.autoProvisionUsers is off.",
                upn, oid);
            await HttpContext.SignOutAsync(AzureAdSchemes.Cookie);
            return LocalRedirect("/login?error=not_provisioned");
        }

        // Upsert user row
        var userId = await _users.UpsertAsync(oid, upn, displayName);

        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            await HttpContext.SignOutAsync(AzureAdSchemes.Cookie);
            return Unauthorized("Account is disabled.");
        }

        // Explicit (manually-assigned) roles merged with AD group-mapped roles
        // (explicit always wins — #67 AC5). The JWT itself is minted by the SPA's
        // silent refresh; here we only need the groups for the session.
        var adGroups = _groupResolver.ExtractGroups(principal);

        // Issue refresh token in httpOnly cookie; the login-time groups travel with it (#153)
        var refreshToken = _refreshTokens.Issue(userId, adGroups);
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(isProduction: _env.IsProduction(), lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        _logger.LogInformation("AzureAd login: session issued for {Upn} (userId={UserId})", upn, userId);

        return LocalRedirect(SafeLocal(returnUrl));
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/refresh
    // Validates the httpOnly refresh cookie and issues a new JWT.
    // Story #67 AC4: re-applies the current AdGroupRoleMapping rows to the
    // groups captured at login. Mapping changes take effect here.
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

        var session = _refreshTokens.Validate(refreshToken);
        if (session is null)
            return Unauthorized("Refresh token invalid or expired.");

        var userId = session.UserId;
        var user   = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            _refreshTokens.Revoke(refreshToken);
            var expiryCookieOpts = AuthCookieHelper.BuildExpiryCookieOptions(isProduction: _env.IsProduction());
            Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, string.Empty, expiryCookieOpts);
            return Unauthorized("Account is disabled.");
        }

        // Explicit roles
        var explicitRoles = await _users.GetRolesAsync(userId);

        // The refresh token is an opaque CMS token, not an AAD token, so there are no
        // group claims on this request. The groups resolved at login were persisted
        // with the session; re-resolving them here means mapping changes made by an
        // admin take effect on the next refresh rather than the next login.
        var adGroups = new List<string>(session.AdGroups);

        // DevBypass (Development only): no UserRole rows exist for dev users, so
        // re-grant the DevBypassRoles set, and let the X-Dev-Groups header simulate
        // AD membership. Outside that exact configuration the header is ignored —
        // honouring it in AzureAd/WindowsAuth would let any cookie holder mint a
        // token with whatever group-mapped role they name (#153).
        if (_authOptions.Mode == AuthMode.DevBypass && _env.IsDevelopment())
        {
            if (user.ExternalId.StartsWith(DevBypassRoles.ExternalIdPrefix, StringComparison.Ordinal))
                explicitRoles = explicitRoles.Concat(DevBypassRoles.Build()).ToList();

            if (Request.Headers.TryGetValue("X-Dev-Groups", out var devGroupHeader))
            {
                adGroups.AddRange(devGroupHeader.ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        // Re-apply current group mappings so admin changes take effect on next refresh.
        var effectiveRoles = await _groupResolver.MergeRolesAsync(adGroups, explicitRoles);

        var accessToken = _jwt.IssueAccessToken(user, effectiveRoles);

        return Ok(new
        {
            accessToken,
            expiresIn = (int)_jwt.AccessTokenLifetime.TotalSeconds,
            tokenType = "Bearer",
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/auth/logout
    // Revokes the CMS refresh session. In AzureAd mode with auth.azureAdSignOut
    // on, the AAD session is left in place and the SPA is told where to
    // navigate (GET /api/auth/signout) so the browser — not a fetch — performs
    // the front-channel end-session round trip with the id_token hint intact.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue(AuthCookieHelper.RefreshTokenCookieName, out var refreshToken)
            && !string.IsNullOrEmpty(refreshToken))
        {
            _refreshTokens.Revoke(refreshToken);
        }

        var expiryCookieOpts = AuthCookieHelper.BuildExpiryCookieOptions(isProduction: _env.IsProduction());
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, string.Empty, expiryCookieOpts);

        if (!AzureAdActive)
            return Ok(new { signOutUrl = (string?)null });

        if (!_settings.GetBool(SiteSettingKeys.AuthAzureAdSignOut))
        {
            await HttpContext.SignOutAsync(AzureAdSchemes.Cookie);
            return Ok(new { signOutUrl = (string?)null });
        }

        return Ok(new { signOutUrl = Url.Action(nameof(AadSignOut), "Auth") });
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/signout
    // Browser navigation target after POST /logout: clears the AAD session
    // cookie and redirects to the Azure AD end-session endpoint, which returns
    // the browser to the SPA login page (VA 6500 AC-12 shared-workstation control).
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("signout")]
    [AllowAnonymous]
    public IActionResult AadSignOut()
    {
        if (!AzureAdActive)
            return NotFound();

        var props = new AuthenticationProperties { RedirectUri = "/login" };
        return SignOut(props, AzureAdSchemes.Cookie, AzureAdSchemes.OpenIdConnect);
    }
}
