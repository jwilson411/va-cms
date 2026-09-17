using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Auth;

/// <summary>
/// Rejects access tokens that were minted before the user's sessions were revoked
/// (#163). The JWT carries User.SessionVersion as the "sv" claim; deactivation and
/// role changes bump the row through usp_RefreshToken_RevokeAllForUser. On every
/// authenticated request the guard compares claim and row — through a per-node
/// cache of auth.revocationCheckSeconds so the check costs one lookup per user per
/// window, not one per request. A deactivated row also fails, so an admin
/// deactivating a user ends that user's session within the same window.
/// </summary>
public interface ISessionRevocationGuard
{
    /// <summary>True when the principal's session version is current and the account is active.</summary>
    Task<bool> IsCurrentAsync(ClaimsPrincipal principal, IUserRepository users);

    /// <summary>Drops the cached row for a user so the next request re-reads it (call after a revocation on this node).</summary>
    void Invalidate(long userId);
}

public sealed class SessionRevocationGuard : ISessionRevocationGuard
{
    private readonly record struct Snapshot(int SessionVersion, bool IsActive, DateTime FetchedAt);

    private readonly ConcurrentDictionary<long, Snapshot> _cache = new();
    private readonly ISiteSettingsService _settings;

    public SessionRevocationGuard(ISiteSettingsService settings) => _settings = settings;

    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, IUserRepository users)
    {
        if (!long.TryParse(principal.FindFirst("cms_user_id")?.Value, out var userId))
            return true;   // not a CMS-issued token shape; nothing to compare against

        // Tokens minted before #163 carry no version: treat as 0, the column default.
        _ = int.TryParse(principal.FindFirst(JwtService.SessionVersionClaim)?.Value, out var claimed);

        var ttl = TimeSpan.FromSeconds(_settings.RevocationCheckSeconds());
        if (!_cache.TryGetValue(userId, out var snap) || DateTime.UtcNow - snap.FetchedAt > ttl)
        {
            var user = await users.GetByIdAsync(userId);
            // A missing row means nothing has been revoked (the token was signed by us
            // for a user that existed at mint time); only a present row can say otherwise.
            snap = user is null
                ? new Snapshot(claimed, true, DateTime.UtcNow)
                : new Snapshot(user.SessionVersion, user.IsActive, DateTime.UtcNow);
            _cache[userId] = snap;
        }

        return snap.IsActive && snap.SessionVersion <= claimed;
    }

    public void Invalidate(long userId) => _cache.TryRemove(userId, out _);
}

/// <summary>Wires the guard into the JWT bearer handler.</summary>
public static class SessionRevocationJwtEvents
{
    public static JwtBearerEvents Build() => new()
    {
        OnTokenValidated = async ctx =>
        {
            var guard = ctx.HttpContext.RequestServices.GetRequiredService<ISessionRevocationGuard>();
            var users = ctx.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
            if (ctx.Principal is { } principal && !await guard.IsCurrentAsync(principal, users))
                ctx.Fail("Session has been revoked.");
        },
    };
}
