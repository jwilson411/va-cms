using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VA.CMS.API.Auth;

/// <summary>
/// Test-only substitute for the Microsoft.Identity.Web OpenID Connect handler.
///
/// The real handler needs a reachable authority for discovery, so the
/// login → callback → SPA redirect → logout flow cannot be exercised in
/// WebApplicationFactory. This handler keeps the same contract:
///   - Challenge: redirects the browser to <see cref="AzureAdSchemes.CallbackPath"/>
///     (as AAD would after sign-in), carrying the RedirectUri as <c>state</c>.
///   - Callback: builds an AAD-shaped principal from trusted test headers,
///     signs it into <see cref="AzureAdSchemes.Cookie"/> and redirects to the
///     original RedirectUri (<c>/api/auth/callback</c>).
///   - SignOut: redirects to a fake end-session endpoint with
///     <c>post_logout_redirect_uri</c>, as the OIDC handler does.
///
/// IMPORTANT: activated only when AZUREAD_FAKE_OIDC=true is set in configuration.
/// Program.cs refuses that flag in Production. Never use this in a deployment.
/// </summary>
public sealed class FakeAzureAdHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>, IAuthenticationRequestHandler, IAuthenticationSignOutHandler
{
    /// <summary>Test-only header: the UPN AAD would return as <c>preferred_username</c>.</summary>
    public const string TestUpnHeader = "X-Test-Aad-Upn";

    /// <summary>Test-only header: comma-separated values emitted as <c>groups</c> claims.</summary>
    public const string TestGroupsHeader = "X-Test-Aad-Groups";

    /// <summary>Where SignOut sends the browser; tests assert on this prefix.</summary>
    public const string EndSessionEndpoint = "https://login.microsoftonline.test/common/oauth2/v2.0/logout";

    public FakeAzureAdHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    // The OIDC scheme never authenticates a request by itself; the cookie scheme does.
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var state = properties.RedirectUri ?? "/";
        Response.Redirect($"{AzureAdSchemes.CallbackPath}?state={Uri.EscapeDataString(state)}");
        return Task.CompletedTask;
    }

    public async Task<bool> HandleRequestAsync()
    {
        if (!Request.Path.Equals(AzureAdSchemes.CallbackPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var upn = Request.Headers[TestUpnHeader].ToString();
        if (string.IsNullOrWhiteSpace(upn))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return true;
        }

        var claims = new List<Claim>
        {
            new("preferred_username", upn),
            new("name",               upn.Split('@')[0]),
            new("oid",                $"oid-{upn}"),
        };
        claims.AddRange(Request.Headers[TestGroupsHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => new Claim("groups", g)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        await Context.SignInAsync(AzureAdSchemes.Cookie, principal, new AuthenticationProperties());

        var state = Request.Query["state"].ToString();
        Response.Redirect(state.StartsWith('/') && !state.StartsWith("//") ? state : "/");
        return true;
    }

    public Task SignOutAsync(AuthenticationProperties? properties)
    {
        var post = properties?.RedirectUri ?? "/";
        var absolute = post.StartsWith('/')
            ? $"{Request.Scheme}://{Request.Host}{post}"
            : post;
        Response.Redirect($"{EndSessionEndpoint}?post_logout_redirect_uri={Uri.EscapeDataString(absolute)}");
        return Task.CompletedTask;
    }
}
