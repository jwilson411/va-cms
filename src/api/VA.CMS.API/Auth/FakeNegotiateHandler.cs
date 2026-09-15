using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VA.CMS.API.Auth;

/// <summary>
/// Test-only substitute for the real Negotiate authentication handler.
///
/// The real NegotiateHandler requires IConnectionItemsFeature (Kestrel only),
/// which is not available in WebApplicationFactory's in-memory test server.
/// This handler authenticates based on a trusted test header instead of a
/// real Kerberos/NTLM exchange.
///
/// IMPORTANT: This handler is activated only when WINDOWS_AUTH_FAKE_NEGOTIATE=true
/// is set in configuration. That key is explicitly blocked in Production environments
/// (Program.cs throws if both IsProduction and the flag are true).
///
/// Never use this handler in production deployments.
/// </summary>
public sealed class FakeNegotiateHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Test-only HTTP header. Set to a UPN to simulate a successfully
    /// negotiated Windows identity (as IIS would establish).
    /// </summary>
    public const string TestUpnHeader = "X-Test-Windows-Upn";

    public FakeNegotiateHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestUpnHeader, out var upnValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var upn = upnValues.ToString();
        if (string.IsNullOrWhiteSpace(upn))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Build a ClaimsPrincipal that looks like what IIS Negotiate would produce
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, upn),
            new Claim(ClaimTypes.NameIdentifier, upn),
        };
        var identity  = new ClaimsIdentity(claims, NegotiateDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, NegotiateDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
