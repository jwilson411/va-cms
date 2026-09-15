using System.Security.Cryptography;

namespace VA.CMS.API.Auth;

/// <summary>
/// Manages opaque refresh tokens stored in httpOnly cookies.
/// The token itself is a cryptographically random 256-bit value (stored in-memory
/// in this implementation — a future story will persist it to the DB for revocation).
/// Token lifetime: 8 hours.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Generates a new refresh token string and records its association with a CMS user ID.</summary>
    string Issue(long userId);

    /// <summary>Validates the token and returns the associated CMS user ID, or null if expired/invalid.</summary>
    long? Validate(string token);

    /// <summary>Revokes a refresh token (called on logout or when the AD account is detected as disabled).</summary>
    void Revoke(string token);
}

/// <summary>
/// In-memory refresh token store. Suitable for single-node dev/staging.
/// A later story will replace this with a DB-backed store (usp_RefreshToken_*).
/// </summary>
public sealed class InMemoryRefreshTokenService : IRefreshTokenService
{
    private readonly record struct Entry(long UserId, DateTime ExpiresAt);
    private readonly Dictionary<string, Entry> _store = new();
    private readonly object _lock = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public string Issue(long userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entry = new Entry(userId, DateTime.UtcNow.Add(Lifetime));

        lock (_lock)
        {
            _store[token] = entry;
            PurgeExpired();
        }

        return token;
    }

    public long? Validate(string token)
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

            return entry.UserId;
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
