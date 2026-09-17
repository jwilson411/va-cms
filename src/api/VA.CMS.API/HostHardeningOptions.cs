using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace VA.CMS.API;

/// <summary>
/// Startup-time host hardening (#162): AllowedHosts must be explicit outside
/// Development, and X-Forwarded-* headers are trusted only from the proxies /
/// networks named in configuration (IIS ARR, the load balancer).
///
///   "AllowedHosts": "cms.va.gov;cms-admin.va.gov"
///   "ForwardedHeaders": { "KnownProxies": ["10.1.2.3"], "KnownNetworks": ["10.1.0.0/16"] }
///   "Cors": { "AllowedOrigins": ["https://www.va.gov"] }   // only when a front end is on another origin
/// </summary>
public static class HostHardeningOptions
{
    /// <summary>Returns an error when AllowedHosts is missing or a wildcard outside Development.</summary>
    public static string? ValidateAllowedHosts(string? allowedHosts, bool isDevelopment)
    {
        if (isDevelopment) return null;

        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(h => h == "*"))
        {
            return "AllowedHosts must list the real host names (e.g. \"cms.va.gov;cms-admin.va.gov\") outside " +
                   "Development; \"*\" accepts any Host header (#162).";
        }
        return null;
    }

    /// <summary>
    /// Forwarded-header trust from configuration. Null when nothing is configured, in
    /// which case UseForwardedHeaders is not added and X-Forwarded-* is ignored.
    /// </summary>
    public static ForwardedHeadersOptions? BuildForwardedHeaders(IConfiguration configuration)
    {
        var section  = configuration.GetSection("ForwardedHeaders");
        var proxies  = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
            return null;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            ForwardLimit     = section.GetValue<int?>("ForwardLimit") ?? 1,
        };
        // Drop the loopback defaults so only the configured hops are trusted.
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var p in proxies)
            options.KnownProxies.Add(IPAddress.Parse(p.Trim()));

        foreach (var n in networks)
        {
            var parts = n.Trim().Split('/');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix))
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks entry '{n}' must be CIDR (e.g. 10.0.0.0/8).");
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(parts[0]), prefix));
        }

        var allowedHosts = section.GetSection("AllowedHosts").Get<string[]>();
        if (allowedHosts is { Length: > 0 })
            options.AllowedHosts = allowedHosts;

        return options;
    }

    /// <summary>
    /// #164: outside Development the refresh cookie is Secure, so the API must be reachable
    /// over HTTPS. Returns an error when Kestrel's own bindings ("urls" / ASPNETCORE_URLS)
    /// are configured, none of them is https://, and no forwarded-header trust is set
    /// (which would mean TLS terminates at IIS ARR or a load balancer). Nothing can be
    /// decided when no bindings are configured (IIS in-process, tests), so that passes.
    /// </summary>
    public static string? ValidateHttpsAvailable(IConfiguration configuration, bool isDevelopment)
    {
        if (isDevelopment) return null;

        var urls = configuration["urls"] ?? configuration["ASPNETCORE_URLS"];
        if (string.IsNullOrWhiteSpace(urls)) return null;

        var bindings = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (bindings.Any(b => b.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return null;

        if (BuildForwardedHeaders(configuration) is not null)
            return null;   // a trusted proxy supplies X-Forwarded-Proto: https

        return $"The API is bound to \"{urls}\" with no https:// endpoint and no ForwardedHeaders:KnownProxies/KnownNetworks. " +
               "Outside Development the cms_rt cookie is Secure and browsers will never return it over HTTP, so every " +
               "sign-in would fail. Bind an https:// URL, or trust the TLS-terminating proxy in ForwardedHeaders (#164).";
    }

    public static string[] CorsAllowedOrigins(IConfiguration configuration)
        => configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
               .Select(o => o.Trim().TrimEnd('/')).Where(o => o.Length > 0).ToArray() ?? [];
}
