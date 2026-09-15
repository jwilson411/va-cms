using System.Net.Http.Headers;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Auth;

/// <summary>
/// Development authentication bypass middleware.
///
/// Active only when Auth:Mode=DevBypass. Intercepts every request:
/// - If an X-Dev-User header is present with a recognised UPN, upserts the
///   user row, issues a CMS JWT, and sets Authorization: Bearer on the
///   incoming request so downstream JWT bearer authentication accepts it.
/// - Requests that already carry a valid Bearer token are passed through
///   unchanged (avoids double-issuance in integration tests that call
///   /api/auth/dev-login directly).
/// - Requests without X-Dev-User are passed through; they will fail the
///   fallback authorization policy with 401 as expected.
///
/// Never registered when ASPNETCORE_ENVIRONMENT=Production (Program.cs guard).
/// </summary>
public class DevBypassMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthOptions     _authOptions;
    private readonly IJwtService     _jwt;
    private readonly ILogger<DevBypassMiddleware> _logger;

    public DevBypassMiddleware(
        RequestDelegate next,
        AuthOptions     authOptions,
        IJwtService     jwt,
        ILogger<DevBypassMiddleware> logger)
    {
        _next        = next;
        _authOptions = authOptions;
        _jwt         = jwt;
        _logger      = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository users)
    {
        // Only act when a X-Dev-User header is present and no Bearer token already supplied
        if (!context.Request.Headers.TryGetValue("X-Dev-User", out var devUserHeader)
            || string.IsNullOrWhiteSpace(devUserHeader))
        {
            await _next(context);
            return;
        }

        // Skip if Authorization: Bearer is already present — respect explicit tokens
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            await _next(context);
            return;
        }

        var upn = devUserHeader.ToString().Trim();

        // Validate against the allowed list configured in appsettings.Development.json
        if (_authOptions.DevBypassAllowedUsers.Length > 0
            && !_authOptions.DevBypassAllowedUsers.Contains(upn, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "DevBypass: UPN '{Upn}' is not in DevBypassAllowedUsers; request rejected.", upn);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync($"DevBypass: UPN '{upn}' is not permitted.");
            return;
        }

        // Upsert the user row (creates on first use, updates last-login on subsequent calls)
        var userId = await users.UpsertAsync(
            externalId:  $"devbypass:{upn}",
            email:       upn,
            displayName: upn.Split('@')[0]);

        var user = await users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("DevBypass: user '{Upn}' (id {Id}) is inactive.", upn, userId);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("DevBypass: user account is disabled.");
            return;
        }

        // Issue a real CMS JWT so all downstream auth/authz middleware works normally
        // DevBypass users are given the Developer role so they can access all dev endpoints
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

        var token = _jwt.IssueAccessToken(user, roles);

        // Inject the token as if the client sent it
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        _logger.LogDebug("DevBypass: issued JWT for '{Upn}'.", upn);

        await _next(context);
    }
}

/// <summary>Extension method for registering the DevBypass middleware.</summary>
public static class DevBypassMiddlewareExtensions
{
    public static IApplicationBuilder UseDevBypassAuth(this IApplicationBuilder app)
        => app.UseMiddleware<DevBypassMiddleware>();
}
