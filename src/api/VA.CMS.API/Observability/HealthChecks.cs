using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.API.Observability;

/// <summary>
/// Real health checks (#166, BRD NFR-OPS-01):
///
///   GET /health, /health/live   — process liveness: no dependencies, always 200 while the
///                                 host serves requests. Load balancers and IIS use this.
///   GET /health/ready           — readiness: SQL Server reachable as the app login, storage
///                                 root writable, settings snapshot loaded, SMTP relay
///                                 reachable when email is on. 503 when Unhealthy.
///
/// Both are anonymous (monitoring tools have no bearer token) but the readiness body is only
/// <c>{"status":…}</c> unless the caller holds the Developer role — dependency names, timings
/// and failure text are for operators, not for anyone who can reach the port. The same routes
/// are mapped under /api/health for hosts that route only /api/* to the API.
/// </summary>
public static class HealthEndpoints
{
    public const string ReadyTag = "ready";
    public static readonly string[] Prefixes = ["/health", "/api/health"];

    public static IServiceCollection AddCmsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SqlHealthCheck>("sql",           tags: [ReadyTag])
            .AddCheck<StorageHealthCheck>("storage",   tags: [ReadyTag])
            .AddCheck<SettingsHealthCheck>("settings", tags: [ReadyTag])
            .AddCheck<SmtpHealthCheck>("smtp",         tags: [ReadyTag]);
        return services;
    }

    public static void MapCmsHealthChecks(this IEndpointRouteBuilder app)
    {
        var live = new HealthCheckOptions
        {
            Predicate      = _ => false,
            ResponseWriter = WriteAsync,
            AllowCachingResponses = false,
        };
        var ready = new HealthCheckOptions
        {
            Predicate      = r => r.Tags.Contains(ReadyTag),
            ResponseWriter = WriteAsync,
            AllowCachingResponses = false,
        };

        foreach (var prefix in Prefixes)
        {
            app.MapHealthChecks(prefix,            live).AllowAnonymous();
            app.MapHealthChecks($"{prefix}/live",  live).AllowAnonymous();
            app.MapHealthChecks($"{prefix}/ready", ready).AllowAnonymous();
        }
    }

    private static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        var detailed = false;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var authz = context.RequestServices.GetRequiredService<IAuthorizationService>();
            detailed = (await authz.AuthorizeAsync(context.User, CmsRoles.Policies.CanDevelop)).Succeeded;
        }

        context.Response.ContentType = "application/json; charset=utf-8";

        object body = detailed
            ? new
            {
                status        = report.Status.ToString(),
                totalDuration = report.TotalDuration.TotalMilliseconds,
                checks        = report.Entries.Select(e => new
                {
                    name        = e.Key,
                    status      = e.Value.Status.ToString(),
                    duration    = e.Value.Duration.TotalMilliseconds,
                    description = e.Value.Description,
                    data        = e.Value.Data.Count == 0 ? null : e.Value.Data,
                }),
            }
            : new { status = report.Status.ToString() };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions), context.RequestAborted);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
}

/// <summary>SELECT 1 over the application's own connection string, i.e. as vacms_app in production.</summary>
public sealed class SqlHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public SqlHealthCheck(IConfiguration configuration)
        => _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var csb = new SqlConnectionStringBuilder(_connectionString) { ConnectTimeout = 5 };
            await using var conn = new SqlConnection(csb.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText    = "SELECT 1";
            cmd.CommandTimeout = 5;
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("SQL Server reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception text can name servers and logins: operators see it (CanDevelop), nobody else.
            return HealthCheckResult.Unhealthy("SQL Server unreachable.", ex);
        }
    }
}

/// <summary>Writes and deletes a probe file through the configured backend so a lost UNC share or a read-only disk shows up.</summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly IStorageBackend _storage;
    private readonly StorageOptions  _options;

    public StorageHealthCheck(IStorageBackend storage, StorageOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var probe = $".health/{Environment.MachineName}-{Guid.NewGuid():N}.probe";
        try
        {
            await _storage.SaveBytesAsync([0x56, 0x41], probe, ct);
            await _storage.DeleteAsync(probe, ct);
            return HealthCheckResult.Healthy($"Storage root writable ({_options.Backend}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Storage root not writable ({_options.Backend}).", ex);
        }
    }
}

/// <summary>
/// The settings snapshot must have loaded from the database at least once (until then every
/// feature flag is at its code default), and it must not be stale. Also the one #173 rule that
/// lives in the database: notifications.adminBaseUrl is embedded in every workflow email and
/// must be https:// outside Development — reported as Degraded so operators see it without the
/// node leaving the load balancer.
/// </summary>
public sealed class SettingsHealthCheck : IHealthCheck
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly ISiteSettingsService _settings;
    private readonly EmailOptions         _email;
    private readonly IHostEnvironment     _env;

    public SettingsHealthCheck(ISiteSettingsService settings, EmailOptions email, IHostEnvironment env)
    {
        _settings = settings;
        _email    = email;
        _env      = env;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var loadedAt = _settings.LoadedAtUtc;
        if (loadedAt is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Site settings have not been loaded from the database yet; code defaults are in effect."));

        var age = DateTime.UtcNow - loadedAt.Value;
        if (age > StaleAfter)
            return Task.FromResult(HealthCheckResult.Degraded($"Site settings snapshot is {age.TotalMinutes:0} minutes old; the database refresh is failing."));

        if (!_env.IsDevelopment() && _email.IsEnabled && _settings.GetBool(SiteSettingKeys.NotificationsEmailEnabled))
        {
            var baseUrl = _settings.GetString(SiteSettingKeys.NotificationsAdminBaseUrl);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"{SiteSettingKeys.NotificationsAdminBaseUrl} is '{baseUrl}'; workflow emails link there. Set it to the https:// admin origin in Admin → Settings."));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Site settings loaded {age.TotalSeconds:0}s ago."));
    }
}

/// <summary>
/// TCP reachability of the SMTP relay when email delivery is on. A mail outage is Degraded, not
/// Unhealthy: the CMS keeps working (in-app notifications still fire) and pulling every node out
/// of the load balancer for a relay problem would make it worse.
/// </summary>
public sealed class SmtpHealthCheck : IHealthCheck
{
    private readonly EmailOptions         _email;
    private readonly ISiteSettingsService _settings;

    public SmtpHealthCheck(EmailOptions email, ISiteSettingsService settings)
    {
        _email    = email;
        _settings = settings;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!_email.IsEnabled)
            return HealthCheckResult.Healthy("Email delivery not configured (Email:Smtp:Host empty); messages are logged.");
        if (!_settings.GetBool(SiteSettingKeys.NotificationsEmailEnabled))
            return HealthCheckResult.Healthy("Email delivery switched off (notifications.emailEnabled=false).");

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(_email.Smtp.Host!, _email.Smtp.Port, timeout.Token);
            return HealthCheckResult.Healthy($"SMTP relay {_email.Smtp.Host}:{_email.Smtp.Port} reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"SMTP relay {_email.Smtp.Host}:{_email.Smtp.Port} unreachable.", ex);
        }
    }
}
