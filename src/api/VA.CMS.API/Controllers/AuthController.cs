using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Handles the AD → JWT auth flow.
///
/// Endpoints (all at /api/auth/*):
///   GET  /api/auth/login    → redirect to Azure AD OIDC (requires ack=1, see below)
///   GET  /api/auth/callback → AAD session cookie validated, refresh cookie set, 302 into the SPA
///   POST /api/auth/refresh  → validates + rotates the refresh cookie, issues new JWT
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
///
/// #163: refresh tokens live in the database and rotate on every use; a replayed
/// token revokes its whole chain. #164: the browser entry point (/login) refuses
/// to start a sign-in until the system-use notice has been acknowledged (ack=1),
/// so a bookmarked /api/auth/login cannot skip the AC-8 banner on the SPA page.
/// Refresh is a POST so that no GET ever mints a credential. #165: every logon,
/// logoff, refresh and failure is an AuditLog row.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>Query parameter the SPA sets once the system-use notice has been acknowledged.</summary>
    public const string AckParameter = "ack";

    private readonly IUserRepository        _users;
    private readonly IJwtService            _jwt;
    private readonly IRefreshTokenService   _refreshTokens;
    private readonly IWebHostEnvironment    _env;
    private readonly IAdGroupRoleResolver   _groupResolver;
    private readonly AuthOptions            _authOptions;
    private readonly ISiteSettingsService   _settings;
    private readonly IAuditLogRepository    _audit;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository         users,
        IJwtService             jwt,
        IRefreshTokenService    refreshTokens,
        IWebHostEnvironment     env,
        IAdGroupRoleResolver    groupResolver,
        AuthOptions             authOptions,
        ISiteSettingsService    settings,
        IAuditLogRepository     audit,
        ILogger<AuthController> logger)
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

    private bool AzureAdActive => _authOptions.Mode == AuthMode.AzureAd;

    private bool SecureCookies => AuthCookieHelper.SecureFor(_env);

    private RefreshClient Client => AuthAudit.ClientOf(HttpContext);

    /// <summary>Only ever redirect to a same-site path so login/logout can never become an open redirect.</summary>
    private string SafeLocal(string? returnUrl, string fallback = "/")
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : fallback;

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/auth/login?returnUrl=&ack=1
    // Redirects the browser to Azure AD OIDC. Without ack=1 the browser is sent
    // to the SPA's /login page, which shows the system-use notice (AC-8).
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl, [FromQuery] string? ack)
    {
        var safeReturn = SafeLocal(returnUrl, fallback: string.Empty);
        var toSpaLogin = safeReturn.Length == 0
            ? "/login"
            : $"/login?returnUrl={Uri.EscapeDataString(safeReturn)}";

        if (ack != "1")
            return LocalRedirect(toSpaLogin);

        // DevBypass: the "AzureAd" scheme is not registered, so Challenge() would
        // throw. Send the browser to the admin SPA's /login page instead, which
        // offers the dev user picker (backed by /api/auth/dev-users).
        if (_authOptions.Mode == AuthMode.DevBypass && _env.IsDevelopment())
            return LocalRedirect(toSpaLogin);

        // WindowsAuth (on-prem IIS / Kerberos): hand the navigation to the Negotiate-
        // protected endpoint. The browser completes the ticket exchange there, the
        // cms_rt cookie is set, and the user is sent back into the SPA — the same
        // shape as the OIDC callback, so the SPA needs no mode awareness.
        if (_authOptions.Mode == AuthMode.WindowsAuth)
        {
            return LocalRedirect(Url.Action(nameof(WindowsAuthController.WindowsLogin), "WindowsAuth",
                new { returnUrl = safeReturn.Length == 0 ? "/" : safeReturn, ack = "1" })!);
        }

        if (!AzureAdActive)
            return NotFound();

        var props = new AuthenticationProperties
        {
            RedirectUri = safeReturn.Length == 0
                ? Url.Action(nameof(Callback), "Auth", new { ack = "1" })
                : Url.Action(nameof(Callback), "Auth", new { returnUrl = safeReturn, ack = "1" }),
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
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl, [FromQuery] string? ack)
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
        {
            await LogonFailureAsync(null, upn, "identity_incomplete");
            return Unauthorized("Could not determine user identity from AD token.");
        }

        // #155: unless auto-provisioning is on, only identities an administrator has
        // already created may sign in. Unknown tenant users get a clear message on
        // the SPA login page rather than an empty, role-less session.
        if (!_settings.GetBool(SiteSettingKeys.AuthAutoProvisionUsers)
            && await _users.GetByExternalIdAsync(oid) is null)
        {
            _logger.LogWarning(
                "AzureAd login rejected: {Upn} (oid {Oid}) is not a provisioned CMS user and auth.autoProvisionUsers is off.",
                upn, oid);
            await LogonFailureAsync(null, upn, "not_provisioned");
            await HttpContext.SignOutAsync(AzureAdSchemes.Cookie);
            return LocalRedirect("/login?error=not_provisioned");
        }

        // Upsert user row
        var userId = await _users.UpsertAsync(oid, upn, displayName);

        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            await LogonFailureAsync(userId, upn, "account_disabled");
            await HttpContext.SignOutAsync(AzureAdSchemes.Cookie);
            return Unauthorized("Account is disabled.");
        }

        // Explicit (manually-assigned) roles merged with AD group-mapped roles
        // (explicit always wins — #67 AC5). The JWT itself is minted by the SPA's
        // silent refresh; here we only need the groups for the session.
        var adGroups = _groupResolver.ExtractGroups(principal);

        // Issue refresh token in httpOnly cookie; the login-time groups travel with it (#153)
        var refreshToken = await _refreshTokens.IssueAsync(userId, adGroups, Client);
        var cookieOpts   = AuthCookieHelper.BuildCookieOptions(SecureCookies, lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, refreshToken, cookieOpts);

        _logger.LogInformation("AzureAd login: session issued for userId={UserId} ({GroupCount} groups).", userId, adGroups.Count);
        await _audit.WriteAsync(userId, AuthAudit.EntityType, userId, AuthAudit.Logon,
            AuthAudit.Diff(new { mode = "AzureAd", upn, systemUseAcknowledged = ack == "1", groups = adGroups.Count }));

        return LocalRedirect(SafeLocal(returnUrl));
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/auth/refresh
    // Validates and rotates the httpOnly refresh cookie and issues a new JWT.
    // Story #67 AC4: re-applies the current AdGroupRoleMapping rows to the
    // groups captured at login. Mapping changes take effect here.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(AuthCookieHelper.RefreshTokenCookieName, out var refreshToken)
            || string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized("Refresh token missing.");
        }

        var validation = await _refreshTokens.ValidateAsync(refreshToken, Client);
        if (!validation.Ok)
        {
            // Unknown values are stale cookies from before a purge or a redeploy —
            // not worth a row. Idle/expired/replayed sessions are (AU-2).
            if (validation.Failure is not RefreshFailure.Unknown)
            {
                await _audit.WriteAsync(null, AuthAudit.EntityType, 0,
                    validation.Failure == RefreshFailure.Replay ? AuthAudit.RefreshReplay : AuthAudit.RefreshFailure,
                    AuthAudit.Diff(new { reason = validation.Failure.ToString() }), AuditOutcome.Failure);
            }
            ExpireCookie();
            return Unauthorized(validation.Failure switch
            {
                RefreshFailure.Idle   => "Session ended after inactivity.",
                RefreshFailure.Replay => "Refresh token reuse detected; session revoked.",
                _                     => "Refresh token invalid or expired.",
            });
        }

        var session = validation.Session!;
        var userId  = session.UserId;
        var user    = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            await _refreshTokens.RevokeAsync(refreshToken, RefreshRevokeReason.Disabled);
            await _audit.WriteAsync(userId, AuthAudit.EntityType, userId, AuthAudit.RefreshFailure,
                AuthAudit.Diff(new { reason = "account_disabled" }), AuditOutcome.Failure);
            ExpireCookie();
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

        // Rotate: the presented token is spent, its replacement goes back in the cookie.
        var nextToken  = await _refreshTokens.RotateAsync(refreshToken, session, Client);
        var cookieOpts = AuthCookieHelper.BuildCookieOptions(SecureCookies, lifetime: _refreshTokens.Lifetime);
        Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, nextToken, cookieOpts);

        var accessToken = _jwt.IssueAccessToken(user, effectiveRoles);
        await _audit.WriteAsync(userId, AuthAudit.EntityType, userId, AuthAudit.Refresh, null);

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
            var validation = await _refreshTokens.ValidateAsync(refreshToken, Client);
            await _refreshTokens.RevokeAsync(refreshToken, RefreshRevokeReason.Logout);

            var userId = validation.Session?.UserId
                      ?? (long.TryParse(User.FindFirst("cms_user_id")?.Value, out var fromToken) ? fromToken : (long?)null);
            await _audit.WriteAsync(userId, AuthAudit.EntityType, userId ?? 0, AuthAudit.Logoff, null);
        }

        ExpireCookie();

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

    private void ExpireCookie()
        => Response.Cookies.Append(AuthCookieHelper.RefreshTokenCookieName, string.Empty,
            AuthCookieHelper.BuildExpiryCookieOptions(SecureCookies));

    private Task LogonFailureAsync(long? userId, string upn, string reason)
        => _audit.WriteAsync(userId, AuthAudit.EntityType, userId ?? 0, AuthAudit.LogonFailure,
            AuthAudit.Diff(new { mode = "AzureAd", upn, reason }), AuditOutcome.Failure);
}

/// <summary>
/// Audit vocabulary for authentication events (#165). EntityType "Session"; EntityId is the
/// CMS user id when known, else 0 with the attempted UPN in DiffJson.
/// </summary>
public static class AuthAudit
{
    public const string EntityType    = "Session";
    public const string Logon         = "Logon";
    public const string LogonFailure  = "LogonFailure";
    public const string Logoff        = "Logoff";
    public const string Refresh       = "Refresh";
    public const string RefreshFailure = "RefreshFailure";
    public const string RefreshReplay = "RefreshReplay";
    public const string SessionsRevoked = "SessionsRevoked";
    public const string SystemUseAckHeader = "X-System-Use-Ack";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Diff(object value) => JsonSerializer.Serialize(value, Json);

    public static RefreshClient ClientOf(HttpContext ctx) => new(
        ctx.Connection.RemoteIpAddress?.ToString(),
        AuditContext.Truncate(ctx.Request.Headers.UserAgent.ToString(), AuditContext.UserAgentMaxLength));
}
