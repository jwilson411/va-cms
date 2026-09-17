using System.Security.Cryptography;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Auth;

/// <summary>
/// What a refresh token resolves to: the CMS user plus the AD groups that were
/// observed at login. Refresh re-applies the *current* AdGroupRoleMapping rows to
/// these original groups (#153) — it never accepts groups from the refresh request.
/// </summary>
public sealed record RefreshSession(long UserId, IReadOnlyList<string> AdGroups);

/// <summary>
/// Manages opaque refresh tokens stored in httpOnly cookies.
/// The token itself is a cryptographically random 256-bit value (stored in-memory
/// in this implementation — a future story will persist it to the DB for revocation).
/// Token lifetime: auth.refreshTokenHours (default 8).
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Current refresh token lifetime; also used for the cookie Max-Age.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>
    /// Generates a new refresh token string and records its association with a CMS user ID
    /// and the AD groups resolved at login (empty when the auth mode has none).
    /// </summary>
    string Issue(long userId, IEnumerable<string>? adGroups = null);

    /// <summary>Validates the token and returns the session it was issued for, or null if expired/invalid.</summary>
    RefreshSession? Validate(string token);

    /// <summary>Revokes a refresh token (called on logout or when the AD account is detected as disabled).</summary>
    void Revoke(string token);
}

/// <summary>
/// In-memory refresh token store. Suitable for single-node dev/staging.
/// A later story will replace this with a DB-backed store (usp_RefreshToken_*).
/// </summary>
public sealed class InMemoryRefreshTokenService : IRefreshTokenService
{
    private readonly record struct Entry(RefreshSession Session, DateTime ExpiresAt);
    private readonly Dictionary<string, Entry> _store = new();
    private readonly object _lock = new();
    private readonly ISiteSettingsService _settings;

    /// <param name="settings">Source of auth.refreshTokenHours (issue #146); code default when null.</param>
    public InMemoryRefreshTokenService(ISiteSettingsService? settings = null)
        => _settings = settings ?? StaticSiteSettings.Defaults;

    public TimeSpan Lifetime =>
        TimeSpan.FromHours(Math.Max(1, _settings.GetInt(SiteSettingKeys.AuthRefreshTokenHours)));

    public string Issue(long userId, IEnumerable<string>? adGroups = null)
    {
        var token   = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var groups  = adGroups?.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                      ?? Array.Empty<string>();
        var entry   = new Entry(new RefreshSession(userId, groups), DateTime.UtcNow.Add(Lifetime));

        lock (_lock)
        {
            _store[token] = entry;
            PurgeExpired();
        }

        return token;
    }

    public RefreshSession? Validate(string token)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(token, out var entry))
                return null;

            if (entry.ExpiresAt < DateTime.UtcNow)
            {
                _store.Remove(token);
                return null;
            }

            return entry.Session;
        }
    }

    public void Revoke(string token)
    {
        lock (_lock)
            _store.Remove(token);
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        var expired = _store.Where(kv => kv.Value.ExpiresAt < now).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            _store.Remove(key);
    }
}
