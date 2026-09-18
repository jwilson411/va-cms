using System.Net;
using System.Net.Sockets;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Egress policy for webhook destinations (#168, epic #152). The dispatcher POSTs from
/// inside the VA network, so an unchecked URL is server-side request forgery by design:
/// a Developer could point a webhook at the IIS management port, an internal SQL box or
/// a link-local metadata service. Three layers, each checked at registration *and* at
/// every delivery (settings change; DNS changes):
///
///   1. Scheme  — https only outside Development.
///   2. Host    — must match the webhooks.allowedHosts site setting ("www.va.gov" or
///                "*.va.gov"). An empty list means no deliveries outside Development.
///   3. Address — every A/AAAA the host resolves to (or the literal IP in the URL) must
///                be a routable unicast address: loopback, unspecified, link-local,
///                multicast and broadcast are always refused; RFC 1918 / CGNAT / ULA only
///                when webhooks.allowPrivateNetworks is on (an on-prem subscriber).
///
/// Layer 3 runs inside the HTTP connection callback (<see cref="WebhookHttpHandler"/>),
/// so the address that is checked is the address that is connected to — a DNS answer
/// that changes between validation and connect (rebinding) cannot slip past. Redirects
/// are not followed at all, so a 3xx to a private host is just a failed delivery.
///
/// In Development everything is allowed: the public site runs on http://localhost.
/// </summary>
public sealed class WebhookDestinationPolicy
{
    private readonly ISiteSettingsService _settings;

    public WebhookDestinationPolicy(ISiteSettingsService settings, IHostEnvironment environment)
        : this(settings, environment.IsDevelopment()) { }

    public WebhookDestinationPolicy(ISiteSettingsService settings, bool isDevelopment)
    {
        _settings     = settings;
        IsDevelopment = isDevelopment;
    }

    public bool IsDevelopment { get; }

    private bool AllowPrivateNetworks => IsDevelopment || _settings.GetBool(SiteSettingKeys.WebhooksAllowPrivateNetworks);

    /// <summary>
    /// Layers 1 and 2 plus a literal-IP layer 3 check. Returns null when the URL may be used,
    /// otherwise a message suitable for a 400 or a delivery-log row.
    /// </summary>
    public string? ValidateUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url))
            return "Url is required.";

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Url must be a valid HTTP/HTTPS URL.";

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "Url must not carry credentials.";

        if (uri.Scheme != Uri.UriSchemeHttps && !IsDevelopment)
            return "Url must use https outside Development.";

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            // A literal address is checked here; a host name is checked when it resolves.
            if (IPAddress.TryParse(uri.IdnHost, out var literal) && ValidateAddress(literal, uri.IdnHost) is { } addressError)
                return addressError;
        }

        return ValidateHost(uri.IdnHost);
    }

    /// <summary>Layer 2 only: is this host name on webhooks.allowedHosts?</summary>
    public string? ValidateHost(string host)
    {
        var allowed = _settings.GetStringList(SiteSettingKeys.WebhooksAllowedHosts);
        if (HostAllowed(host, allowed)) return null;

        if (IsDevelopment && allowed.Count == 0) return null;   // local dev: no list = anything goes

        return allowed.Count == 0
            ? "No webhook hosts are allowed: set the webhooks.allowedHosts site setting (e.g. [\"*.va.gov\"]) first."
            : $"Host '{host}' is not on the webhooks.allowedHosts list.";
    }

    /// <summary>Layer 3: is this resolved (or literal) address a permitted destination?</summary>
    public string? ValidateAddress(IPAddress address, string host)
    {
        if (IsDevelopment) return null;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IsAlwaysBlocked(address))
            return $"Host '{host}' resolves to {address}, which is a loopback, link-local, multicast or unspecified address.";

        if (IsPrivate(address) && !AllowPrivateNetworks)
            return $"Host '{host}' resolves to the private address {address}; enable webhooks.allowPrivateNetworks for on-prem subscribers.";

        return null;
    }

    // ── address classes ───────────────────────────────────────────────────────

    /// <summary>Loopback, unspecified, link-local, multicast, broadcast, IPv6 site-local and documentation ranges.</summary>
    public static bool IsAlwaysBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                     // 0.0.0.0/8 "this network"
                || (b[0] == 169 && b[1] == 254)                  // link-local incl. 169.254.169.254
                || b[0] >= 224                                   // multicast 224/4, reserved 240/4, broadcast
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // 192.0.0.0/24 IETF protocol assignments
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)       // TEST-NET-1
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113);    // TEST-NET-3
        }

        return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal
            || address.IsIPv6Teredo
            || IsIPv6Documentation(address);
    }

    /// <summary>RFC 1918, CGNAT (100.64/10) and IPv6 unique-local (fc00::/7).</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;

        return false;
    }

    private static bool IsIPv6Documentation(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        var b = address.GetAddressBytes();
        return b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8;   // 2001:db8::/32
    }

    // ── allow-list matching ("www.va.gov" exact, "*.va.gov" any sub-domain) ──

    public static bool HostAllowed(string host, IEnumerable<string> allowed)
    {
        foreach (var raw in allowed)
        {
            var pattern = raw.Trim();
            if (pattern.Length == 0) continue;

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..];
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
                    return true;
            }
            else if (string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
