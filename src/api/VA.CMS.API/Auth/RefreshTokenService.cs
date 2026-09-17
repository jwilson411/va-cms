using System.Security.Cryptography;
using System.Text.Json;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Auth;

/// <summary>
/// What a refresh token resolves to: the CMS user plus the AD groups that were
/// observed at login. Refresh re-applies the *current* AdGroupRoleMapping rows to
/// these original groups (#153) — it never accepts groups from the refresh request.
/// </summary>
public sealed record RefreshSession(long UserId, IReadOnlyList<string> AdGroups, long TokenId);

/// <summary>Why a presented refresh token was not accepted.</summary>
public enum RefreshFailure
{
    /// <summary>Unknown value (never issued, or purged).</summary>
    Unknown,
    /// <summary>Past its own or the session's absolute expiry.</summary>
    Expired,
    /// <summary>Unused for longer than auth.idleTimeoutMinutes (AC-11).</summary>
    Idle,
    /// <summary>Already rotated or revoked and presented again: the whole chain has been revoked (FR-SECURITY-02).</summary>
    Replay,
}

/// <summary>Outcome of <see cref="IRefreshTokenService.ValidateAsync"/>.</summary>
public sealed record RefreshValidation(RefreshSession? Session, RefreshFailure? Failure)
{
    public bool Ok => Session is not null;
    public static RefreshValidation Fail(RefreshFailure why) => new(null, why);
    public static RefreshValidation Success(RefreshSession s) => new(s, null);
}

/// <summary>The caller's address and agent, recorded on the token row and its audit events.</summary>
public readonly record struct RefreshClient(string? Ip, string? UserAgent)
{
    public static readonly RefreshClient None = new(null, null);
}

/// <summary>Reasons recorded on RefreshToken.RevokedReason.</summary>
public static class RefreshRevokeReason
{
    public const string Logout      = "Logout";
    public const string Disabled    = "Disabled";
    public const string Deactivated = "Deactivated";
    public const string RoleChange  = "RoleChange";
    public const string Admin       = "Admin";
}

/// <summary>
/// Manages opaque refresh tokens stored in httpOnly cookies (#163, BRD FR-SECURITY-02).
/// The token itself is a cryptographically random 256-bit value; the store keeps only
/// its SHA-256. Every refresh rotates: the presented token is revoked and a new one
/// issued in its place. Presenting a revoked token again is replay and revokes the
/// whole chain. Lifetimes are site settings — auth.refreshTokenHours per token,
/// auth.idleTimeoutMinutes between uses, auth.absoluteSessionHours from login.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Per-token lifetime; also the cookie Max-Age.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>Starts a new session chain for a user at login. Returns the cookie value.</summary>
    Task<string> IssueAsync(long userId, IEnumerable<string>? adGroups = null, RefreshClient client = default);

    /// <summary>Resolves a presented token without consuming it.</summary>
    Task<RefreshValidation> ValidateAsync(string token, RefreshClient client = default);

    /// <summary>Revokes the presented token and issues its replacement. Returns the new cookie value.</summary>
    Task<string> RotateAsync(string token, RefreshSession session, RefreshClient client = default);

    /// <summary>Revokes one token (logout, disabled account).</summary>
    Task RevokeAsync(string token, string reason = RefreshRevokeReason.Logout);

    /// <summary>"Sign out everywhere": revokes every live token of a user and bumps their SessionVersion.</summary>
    Task RevokeAllForUserAsync(long userId, long? actorId, string reason = RefreshRevokeReason.Admin);
}

/// <summary>Token generation and hashing shared by both stores.</summary>
public static class RefreshTokenCodec
{
    public static string NewToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    public static string GroupsToJson(IEnumerable<string>? groups)
        => JsonSerializer.Serialize(NormalizeGroups(groups));

    public static IReadOnlyList<string> GroupsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    public static string[] NormalizeGroups(IEnumerable<string>? groups)
        => groups?.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
           ?? Array.Empty<string>();

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Database-backed store: one usp_RefreshToken_* call per operation, no state in the
/// process, so a web garden or a two-node farm shares sessions and a deploy does not
/// sign everyone out.
/// </summary>
public sealed class DbRefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenRepository _repo;
    private readonly ISiteSettingsService    _settings;

    public DbRefreshTokenService(IRefreshTokenRepository repo, ISiteSettingsService settings)
    {
        _repo     = repo;
        _settings = settings;
    }

    public TimeSpan Lifetime => TimeSpan.FromHours(_settings.RefreshTokenHours());

    public async Task<string> IssueAsync(long userId, IEnumerable<string>? adGroups = null, RefreshClient client = default)
    {
        var token = RefreshTokenCodec.NewToken();
        var now   = DateTime.UtcNow;
        await _repo.IssueAsync(userId, RefreshTokenCodec.Hash(token),
            expiresAt:         now.Add(Lifetime),
            absoluteExpiresAt: now.AddHours(_settings.AbsoluteSessionHours()),
            client.Ip, client.UserAgent, RefreshTokenCodec.GroupsToJson(adGroups));
        return token;
    }

    public async Task<RefreshValidation> ValidateAsync(string token, RefreshClient client = default)
    {
        var row = await _repo.ValidateAsync(RefreshTokenCodec.Hash(token),
            _settings.IdleTimeoutMinutes(), _settings.RefreshRotationGraceSeconds(), client.Ip, client.UserAgent);

        return row?.Status switch
        {
            null                       => RefreshValidation.Fail(RefreshFailure.Unknown),
            RefreshTokenStatus.Ok      => RefreshValidation.Success(new RefreshSession(row.UserId, RefreshTokenCodec.GroupsFromJson(row.GroupsJson), row.Id)),
            RefreshTokenStatus.Replay  => RefreshValidation.Fail(RefreshFailure.Replay),
            RefreshTokenStatus.Idle    => RefreshValidation.Fail(RefreshFailure.Idle),
            _                          => RefreshValidation.Fail(RefreshFailure.Expired),
        };
    }

    public async Task<string> RotateAsync(string token, RefreshSession session, RefreshClient client = default)
    {
        var next = RefreshTokenCodec.NewToken();
        await _repo.RotateAsync(session.TokenId, RefreshTokenCodec.Hash(next), DateTime.UtcNow.Add(Lifetime),
            client.Ip, client.UserAgent);
        return next;
    }

    public Task RevokeAsync(string token, string reason = RefreshRevokeReason.Logout)
        => _repo.RevokeAsync(RefreshTokenCodec.Hash(token), reason);

    public Task RevokeAllForUserAsync(long userId, long? actorId, string reason = RefreshRevokeReason.Admin)
        => _repo.RevokeAllForUserAsync(userId, actorId, reason);
}

/// <summary>
/// In-memory store with the same rotation / replay semantics, for unit and
/// integration tests that run without SQL Server. Not registered by Program.cs.
/// </summary>
public sealed class InMemoryRefreshTokenService : IRefreshTokenService
{
    private sealed class Entry
    {
        public required long UserId;
        public required string[] Groups;
        public required Guid Family;
        public required DateTime ExpiresAt;
        public required DateTime AbsoluteExpiresAt;
        public DateTime LastUsedAt = DateTime.UtcNow;
        public DateTime? RevokedAt;
        public string? RevokedReason;
        public long Id;
    }

    private readonly Dictionary<string, Entry> _store = new();
    private readonly object _lock = new();
    private readonly ISiteSettingsService _settings;
    private long _nextId;

    /// <summary>Users whose sessions were revoked via <see cref="RevokeAllForUserAsync"/>, with the reason — for test assertions.</summary>
    public List<(long UserId, string Reason)> RevokedUsers { get; } = new();

    /// <param name="settings">Source of the auth.* lifetimes (issue #146); code defaults when null.</param>
    public InMemoryRefreshTokenService(ISiteSettingsService? settings = null)
        => _settings = settings ?? StaticSiteSettings.Defaults;

    public TimeSpan Lifetime => TimeSpan.FromHours(_settings.RefreshTokenHours());

    public Task<string> IssueAsync(long userId, IEnumerable<string>? adGroups = null, RefreshClient client = default)
    {
        var token = RefreshTokenCodec.NewToken();
        var now   = DateTime.UtcNow;
        lock (_lock)
        {
            _store[token] = new Entry
            {
                Id = ++_nextId, UserId = userId, Groups = RefreshTokenCodec.NormalizeGroups(adGroups), Family = Guid.NewGuid(),
                ExpiresAt = now.Add(Lifetime), AbsoluteExpiresAt = now.AddHours(_settings.AbsoluteSessionHours()),
            };
            PurgeExpired();
        }
        return Task.FromResult(token);
    }

    public Task<RefreshValidation> ValidateAsync(string token, RefreshClient client = default)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(token, out var e))
                return Task.FromResult(RefreshValidation.Fail(RefreshFailure.Unknown));

            var now = DateTime.UtcNow;
            if (e.RevokedAt is { } revokedAt
                && !(e.RevokedReason == "Rotated" && (now - revokedAt).TotalSeconds <= _settings.RefreshRotationGraceSeconds()))
            {
                foreach (var other in _store.Values.Where(o => o.Family == e.Family && o.RevokedAt is null))
                {
                    other.RevokedAt = now;
                    other.RevokedReason = "Replay";
                }
                return Task.FromResult(RefreshValidation.Fail(RefreshFailure.Replay));
            }

            if (now >= e.ExpiresAt || now >= e.AbsoluteExpiresAt)
                return Task.FromResult(RefreshValidation.Fail(RefreshFailure.Expired));

            if ((now - e.LastUsedAt).TotalMinutes > _settings.IdleTimeoutMinutes())
                return Task.FromResult(RefreshValidation.Fail(RefreshFailure.Idle));

            return Task.FromResult(RefreshValidation.Success(new RefreshSession(e.UserId, e.Groups, e.Id)));
        }
    }

    public Task<string> RotateAsync(string token, RefreshSession session, RefreshClient client = default)
    {
        var next = RefreshTokenCodec.NewToken();
        var now  = DateTime.UtcNow;
        lock (_lock)
        {
            if (!_store.TryGetValue(token, out var old))
                throw new InvalidOperationException("Cannot rotate a token that was not validated first.");

            old.RevokedAt     ??= now;
            old.RevokedReason ??= "Rotated";
            old.LastUsedAt      = now;

            var expires = now.Add(Lifetime);
            _store[next] = new Entry
            {
                Id = ++_nextId, UserId = old.UserId, Groups = old.Groups, Family = old.Family,
                ExpiresAt = expires < old.AbsoluteExpiresAt ? expires : old.AbsoluteExpiresAt,
                AbsoluteExpiresAt = old.AbsoluteExpiresAt,
            };
        }
        return Task.FromResult(next);
    }

    public Task RevokeAsync(string token, string reason = RefreshRevokeReason.Logout)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(token, out var e) && e.RevokedAt is null)
            {
                e.RevokedAt = DateTime.UtcNow;
                e.RevokedReason = reason;
            }
        }
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(long userId, long? actorId, string reason = RefreshRevokeReason.Admin)
    {
        lock (_lock)
        {
            foreach (var e in _store.Values.Where(e => e.UserId == userId && e.RevokedAt is null))
            {
                e.RevokedAt = DateTime.UtcNow;
                e.RevokedReason = reason;
            }
            RevokedUsers.Add((userId, reason));
        }
        return Task.CompletedTask;
    }

    /// <summary>Test hook: back-date a token's last use so the idle window can be exercised without waiting.</summary>
    public void SetLastUsed(string token, DateTime lastUsedUtc)
    {
        lock (_lock)
            if (_store.TryGetValue(token, out var e)) e.LastUsedAt = lastUsedUtc;
    }

    /// <summary>Test hook: back-date a token's rotation so the grace window can be exercised without waiting.</summary>
    public void SetRevokedAt(string token, DateTime revokedUtc)
    {
        lock (_lock)
            if (_store.TryGetValue(token, out var e)) e.RevokedAt = revokedUtc;
    }

    private void PurgeExpired()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var key in _store.Where(kv => kv.Value.ExpiresAt < cutoff).Select(kv => kv.Key).ToList())
            _store.Remove(key);
    }
}
