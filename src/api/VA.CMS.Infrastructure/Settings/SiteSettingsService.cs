using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VA.CMS.Infrastructure.Settings;

/// <summary>
/// Read side of runtime configuration (issue #142, epic #141).
///
/// Reads never touch the database: they come from an in-memory snapshot that the hosted
/// implementation loads at startup, refreshes on a timer, and reloads immediately after an
/// admin write. Until the first successful load — or whenever the database is unreachable —
/// every getter answers with the code default from <see cref="SiteSettingDefinitions"/>, so
/// startup, request handling and tests never block on configuration.
/// </summary>
public interface ISiteSettingsService
{
    /// <summary>Every row from the last successful load, ordered by category/sort/key. Empty before the first load.</summary>
    IReadOnlyList<SiteSettingRow> Snapshot { get; }

    /// <summary>UTC time of the last successful load, or null if the database has never answered.</summary>
    DateTime? LoadedAtUtc { get; }

    string GetString(string key);
    int GetInt(string key);
    long GetLong(string key);
    bool GetBool(string key);
    /// <summary>Deserialize a json setting; falls back to the default value, then to <paramref name="fallback"/>.</summary>
    T GetJson<T>(string key, T fallback);
    IReadOnlyList<string> GetStringList(string key);
    IReadOnlyList<int> GetIntList(string key);

    /// <summary>Reload the snapshot from the database now (after an admin write).</summary>
    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>Parsing shared by the hosted service and the static test double.</summary>
public static class SiteSettingParser
{
    public static bool ParseBool(string? raw, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return raw.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on"  => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback,
        };
    }

    public static long ParseLong(string? raw, long fallback) =>
        long.TryParse(raw?.Trim(), out var v) ? v : fallback;

    public static T ParseJson<T>(string? raw, T fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(raw) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Validate a candidate value for a declared setting before it is stored.
    /// Returns null when valid, otherwise a message suitable for a 400 response.
    /// </summary>
    public static string? Validate(SiteSettingDefinition definition, string? value)
    {
        switch (definition.Type)
        {
            case SiteSettingType.Bool:
                return value?.Trim().ToLowerInvariant() is "true" or "false"
                    ? null
                    : $"'{definition.Key}' must be true or false.";

            case SiteSettingType.Int:
                if (!long.TryParse(value?.Trim(), out var n))
                    return $"'{definition.Key}' must be a whole number.";
                return n < 0 ? $"'{definition.Key}' must not be negative." : null;

            case SiteSettingType.Json:
                if (string.IsNullOrWhiteSpace(value)) return $"'{definition.Key}' must be valid JSON.";
                try
                {
                    using var _ = JsonDocument.Parse(value);
                    return null;
                }
                catch (JsonException ex)
                {
                    return $"'{definition.Key}' must be valid JSON: {ex.Message}";
                }

            default:
                return value is null ? $"'{definition.Key}' must be a string." : null;
        }
    }
}

/// <summary>
/// Base with the typed getters. <see cref="Lookup"/> returns the raw effective value for a key or null.
/// </summary>
public abstract class SiteSettingsBase : ISiteSettingsService
{
    public abstract IReadOnlyList<SiteSettingRow> Snapshot { get; }
    public abstract DateTime? LoadedAtUtc { get; }
    public abstract Task RefreshAsync(CancellationToken ct = default);

    protected abstract string? Lookup(string key);

    private static string DefaultOf(string key) => SiteSettingDefinitions.Get(key).Default;

    public string GetString(string key) => Lookup(key) ?? DefaultOf(key);

    public long GetLong(string key)
    {
        var fallback = SiteSettingParser.ParseLong(DefaultOf(key), 0);
        return SiteSettingParser.ParseLong(Lookup(key), fallback);
    }

    public int GetInt(string key)
    {
        var v = GetLong(key);
        return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }

    public bool GetBool(string key)
    {
        var fallback = SiteSettingParser.ParseBool(DefaultOf(key), false);
        return SiteSettingParser.ParseBool(Lookup(key), fallback);
    }

    public T GetJson<T>(string key, T fallback)
    {
        var def = SiteSettingParser.ParseJson(DefaultOf(key), fallback);
        return SiteSettingParser.ParseJson(Lookup(key), def);
    }

    public IReadOnlyList<string> GetStringList(string key) => GetJson<string[]>(key, Array.Empty<string>());
    public IReadOnlyList<int> GetIntList(string key) => GetJson<int[]>(key, Array.Empty<int>());
}

/// <summary>
/// Production implementation: singleton + hosted service. Registered once and resolved as
/// <see cref="ISiteSettingsService"/> and <see cref="IHostedService"/>.
/// </summary>
public sealed class SiteSettingsService : SiteSettingsBase, IHostedService, IDisposable
{
    /// <summary>Bootstrap TTL for the timer refresh. Not itself a database setting (chicken and egg).</summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly ISiteSettingRepository _repo;
    private readonly ILogger<SiteSettingsService> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    private volatile IReadOnlyDictionary<string, SiteSettingRow> _byKey =
        new Dictionary<string, SiteSettingRow>(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<SiteSettingRow> _snapshot = Array.Empty<SiteSettingRow>();
    private bool _definitionsSynced;
    private Task? _loop;

    public SiteSettingsService(
        ISiteSettingRepository repo,
        ILogger<SiteSettingsService>? logger = null,
        TimeSpan? refreshInterval = null)
    {
        _repo            = repo;
        _logger          = logger ?? NullLogger<SiteSettingsService>.Instance;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
    }

    public override IReadOnlyList<SiteSettingRow> Snapshot => _snapshot;
    private DateTime? _loadedAt;
    public override DateTime? LoadedAtUtc => _loadedAt;

    protected override string? Lookup(string key) =>
        _byKey.TryGetValue(key, out var row) ? row.EffectiveValue : null;

    /// <inheritdoc />
    public override async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!_definitionsSynced)
            {
                await _repo.EnsureDefinitionsAsync(SiteSettingDefinitions.All, ct);
                _definitionsSynced = true;
            }

            var rows = await _repo.ListAsync(ct);
            _byKey      = rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
            _snapshot   = rows;
            _loadedAt   = DateTime.UtcNow;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // ── IHostedService ─────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Never block host startup on the database: load in the background and keep
        // polling. Until the first load succeeds the getters answer with code defaults.
        _loop = Task.Run(() => RunLoopAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // WebApplicationFactory.DisposeAsync can dispose services before stopping hosted
        // services; tolerate either order.
        if (!_disposed)
        {
            try { _stopping.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
                _logger.LogDebug("SiteSettings snapshot refreshed ({Count} keys).", _snapshot.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SiteSettings could not be loaded from the database; using {State} until the next attempt.",
                    LoadedAtUtc is null ? "code defaults" : "the previous snapshot");
            }

            try { await Task.Delay(_refreshInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        // The same instance is registered as ISiteSettingsService and IHostedService, so the
        // container disposes it more than once.
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
        _refreshLock.Dispose();
    }
}

/// <summary>
/// In-memory implementation for unit tests and for code paths that must run before the
/// hosted service exists. Answers with code defaults unless overridden via <see cref="With"/>.
/// </summary>
public sealed class StaticSiteSettings : SiteSettingsBase
{
    private readonly Dictionary<string, string?> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public static StaticSiteSettings Defaults => new();

    public StaticSiteSettings With(string key, string? value)
    {
        _overrides[key] = value;
        return this;
    }

    public StaticSiteSettings With(string key, bool value) => With(key, value ? "true" : "false");
    public StaticSiteSettings With(string key, long value) => With(key, value.ToString());
    public StaticSiteSettings WithJson(string key, object value) => With(key, JsonSerializer.Serialize(value));

    public override IReadOnlyList<SiteSettingRow> Snapshot =>
        SiteSettingDefinitions.All.Select(d => new SiteSettingRow
        {
            Key          = d.Key,
            Value        = _overrides.TryGetValue(d.Key, out var v) ? v : null,
            DefaultValue = d.Default,
            DataType     = d.Type.ToString().ToLowerInvariant(),
            Category     = d.Category,
            Scope        = d.Scope.ToString(),
            Description  = d.Description,
            SortOrder    = d.SortOrder,
        }).ToList();

    // A static source is "loaded" from the moment it exists: the readiness check (#166)
    // treats a null LoadedAtUtc as "the database has never answered".
    private readonly DateTime _createdAt = DateTime.UtcNow;
    public override DateTime? LoadedAtUtc => _createdAt;

    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    protected override string? Lookup(string key) =>
        _overrides.TryGetValue(key, out var v) ? v : null;
}

/// <summary>Convenience reads shared by controllers and GraphQL resolvers.</summary>
public static class SiteSettingsExtensions
{
    /// <summary>Clamp an admin/API page size to [1, api.maxPageSize] (issue #147).</summary>
    public static int ClampPageSize(this ISiteSettingsService settings, int pageSize) =>
        Math.Clamp(pageSize, 1, Math.Max(1, settings.GetInt(SiteSettingKeys.ApiMaxPageSize)));

    /// <summary>Clamp a public search page size to [1, search.maxPageSize], defaulting to search.defaultPageSize.</summary>
    public static int ClampSearchPageSize(this ISiteSettingsService settings, int? pageSize)
    {
        var requested = pageSize ?? settings.GetInt(SiteSettingKeys.SearchDefaultPageSize);
        return Math.Clamp(requested, 1, Math.Max(1, settings.GetInt(SiteSettingKeys.SearchMaxPageSize)));
    }

    // ── Session policy (#163/#164) ───────────────────────────────────────────

    /// <summary>auth.accessTokenMinutes, at least 1.</summary>
    public static int AccessTokenMinutes(this ISiteSettingsService settings) =>
        Math.Max(1, settings.GetInt(SiteSettingKeys.AuthAccessTokenMinutes));

    /// <summary>
    /// Server-side idle window in minutes: auth.idleTimeoutMinutes, but never less than the
    /// access token lifetime plus one minute — the SPA refreshes a minute before expiry, so a
    /// smaller window would reject every silent refresh of an active user.
    /// </summary>
    public static int IdleTimeoutMinutes(this ISiteSettingsService settings) =>
        Math.Max(settings.GetInt(SiteSettingKeys.AuthIdleTimeoutMinutes), settings.AccessTokenMinutes() + 1);

    /// <summary>auth.absoluteSessionHours clamped to [1, 12] (VA 6500 AC-12).</summary>
    public static int AbsoluteSessionHours(this ISiteSettingsService settings) =>
        Math.Clamp(settings.GetInt(SiteSettingKeys.AuthAbsoluteSessionHours), 1, 12);

    /// <summary>auth.refreshTokenHours, at least 1 and never past the absolute session cap.</summary>
    public static int RefreshTokenHours(this ISiteSettingsService settings) =>
        Math.Clamp(settings.GetInt(SiteSettingKeys.AuthRefreshTokenHours), 1, settings.AbsoluteSessionHours());

    /// <summary>auth.revocationCheckSeconds clamped to [0, 300].</summary>
    public static int RevocationCheckSeconds(this ISiteSettingsService settings) =>
        Math.Clamp(settings.GetInt(SiteSettingKeys.AuthRevocationCheckSeconds), 0, 300);

    /// <summary>auth.refreshRotationGraceSeconds clamped to [0, 300].</summary>
    public static int RefreshRotationGraceSeconds(this ISiteSettingsService settings) =>
        Math.Clamp(settings.GetInt(SiteSettingKeys.AuthRefreshRotationGraceSeconds), 0, 300);
}
