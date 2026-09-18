using Microsoft.Extensions.Caching.Memory;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Navigation;

/// <summary>A redirect rule as the public site needs it: where a request path goes and with which status.</summary>
public sealed record ResolvedRedirect(string FromPath, string ToPath, int StatusCode);

/// <summary>
/// Resolves an incoming public-site path to an active redirect rule (#169).
/// Backs GET /api/v1/redirects/resolve, which the public site's proxy consults on
/// every page request, so hits and misses are both cached for
/// redirects.cacheSeconds. Mutations in this process call <see cref="Invalidate"/>;
/// other nodes converge within the TTL.
/// </summary>
public interface IRedirectResolver
{
    /// <summary>Returns the active rule for <paramref name="path"/> (exact, or its trailing-slash twin) or null.</summary>
    Task<ResolvedRedirect?> ResolveAsync(string path, CancellationToken ct = default);

    /// <summary>Drops every cached answer; called after any redirect or slug mutation.</summary>
    void Invalidate();

    /// <summary>The TTL both this cache and the resolve response's Cache-Control use.</summary>
    TimeSpan CacheTtl { get; }
}

/// <summary>
/// Process-wide answer cache shared by the scoped <see cref="RedirectResolver"/> instances.
/// A miss is cached as a sentinel so a crawler hammering dead URLs does not hammer SQL.
/// </summary>
public sealed class RedirectResolveCache : IDisposable
{
    private static readonly ResolvedRedirect Miss = new(string.Empty, string.Empty, 0);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 20_000 });

    public bool TryGet(string path, out ResolvedRedirect? hit)
    {
        if (_cache.TryGetValue(path, out ResolvedRedirect? cached))
        {
            hit = ReferenceEquals(cached, Miss) ? null : cached;
            return true;
        }
        hit = null;
        return false;
    }

    public void Set(string path, ResolvedRedirect? value, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) return;
        _cache.Set(path, value ?? Miss, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl, Size = 1 });
    }

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Dispose();
}

public sealed class RedirectResolver : IRedirectResolver
{
    /// <summary>Longest path the resolver will look up; matches the FromPath column width.</summary>
    public const int MaxPathLength = 2000;

    private readonly INavigationRepository _nav;
    private readonly RedirectResolveCache  _cache;
    private readonly ISiteSettingsService  _settings;

    public RedirectResolver(INavigationRepository nav, RedirectResolveCache cache, ISiteSettingsService settings)
    {
        _nav      = nav;
        _cache    = cache;
        _settings = settings;
    }

    public TimeSpan CacheTtl => TimeSpan.FromSeconds(Math.Clamp(_settings.GetInt(SiteSettingKeys.RedirectsCacheSeconds), 0, 86_400));

    public async Task<ResolvedRedirect?> ResolveAsync(string path, CancellationToken ct = default)
    {
        var key = Normalize(path);
        if (key is null) return null;

        if (_cache.TryGet(key, out var cached)) return cached;

        var rule = await _nav.GetRedirectByPathAsync(key);
        var resolved = rule is null ? null : new ResolvedRedirect(rule.FromPath, rule.ToPath, rule.StatusCode);
        _cache.Set(key, resolved, CacheTtl);
        return resolved;
    }

    public void Invalidate() => _cache.Clear();

    /// <summary>
    /// The lookup key for a request path: site-relative, no query/fragment, at most
    /// <see cref="MaxPathLength"/> chars. Null when the input cannot be a stored FromPath.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim();

        var cut = p.IndexOfAny(['?', '#']);
        if (cut >= 0) p = p[..cut];

        if (p.Length == 0 || p.Length > MaxPathLength) return null;
        if (!RedirectPathValidator.IsSiteRelative(p)) return null;
        return p;
    }
}
