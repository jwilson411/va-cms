using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace VA.CMS.API.Webhooks;

/// <summary>Resolves a webhook host to its addresses. Replaced by a stub in tests (DNS rebinding cases).</summary>
public interface IWebhookDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsWebhookDnsResolver : IWebhookDnsResolver
{
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}

/// <summary>
/// The pinned <see cref="SocketsHttpHandler"/> behind the "WebhookClient" named client (#168):
///
///   * <see cref="SocketsHttpHandler.ConnectCallback"/> resolves the host through
///     <see cref="IWebhookDnsResolver"/>, runs every answer through
///     <see cref="WebhookDestinationPolicy.ValidateAddress"/> and connects only to an
///     address that passed. Validation and connection use the same DNS answer, so a
///     rebinding attack (public address at registration, private address at delivery)
///     is refused at the socket.
///   * No redirects (a 3xx is a failed delivery, never followed to another host),
///     no proxy inheritance from the app-pool identity, no cookies.
///   * TLS 1.2+ with default (OS) certificate validation — no TrustServerCertificate
///     analogue exists here on purpose.
///   * Response headers capped; the client's MaxResponseContentBufferSize caps the body,
///     since a subscriber's reply is only ever logged as a status code.
/// </summary>
public static class WebhookHttpHandler
{
    public const string ClientName = "WebhookClient";

    /// <summary>Upper bound on a subscriber's response body; the dispatcher only reads the status code.</summary>
    public const long MaxResponseBytes = 64 * 1024;

    public static SocketsHttpHandler Create(WebhookDestinationPolicy policy, IWebhookDnsResolver resolver)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect        = false,
            UseProxy                 = false,
            UseCookies               = false,
            MaxAutomaticRedirections = 1,
            MaxResponseHeadersLength = 16,          // KB
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),   // re-resolve DNS regularly
            ConnectTimeout           = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
            ConnectCallback = (context, ct) => ConnectAsync(context, policy, resolver, ct),
        };
        return handler;
    }

    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromMinutes(5);   // ceiling; the per-delivery timeout is webhooks.timeoutSeconds
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VA-CMS-Webhooks", "1.0"));
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, WebhookDestinationPolicy policy, IWebhookDnsResolver resolver, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        try { addresses = await resolver.ResolveAsync(host, ct); }
        catch (SocketException ex)
        {
            throw new WebhookDestinationException($"Host '{host}' could not be resolved: {ex.SocketErrorCode}.", ex);
        }

        if (addresses.Length == 0)
            throw new WebhookDestinationException($"Host '{host}' has no addresses.");

        // Every answer must pass: an attacker who controls the zone can return one public and
        // one private address and hope the client picks the private one.
        foreach (var address in addresses)
        {
            if (policy.ValidateAddress(address, host) is { } error)
                throw new WebhookDestinationException(error);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>A delivery refused by <see cref="WebhookDestinationPolicy"/> at connect time. Never retried.</summary>
public sealed class WebhookDestinationException : HttpRequestException
{
    public WebhookDestinationException(string message, Exception? inner = null) : base(message, inner) { }
}
