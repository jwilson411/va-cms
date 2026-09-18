using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;
using VA.CMS.API.Middleware;
using VA.CMS.Infrastructure.Logging;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.RateLimiting;

/// <summary>
/// Request rate limiting (#167, epic #152). Four named policies, each a per-client
/// allowance per minute read from the api.rateLimits.* site settings:
///
///   public-read      anonymous reads (public content, search, navigation, media serve,
///                    GraphQL, health) — sliding window per client IP
///   auth             /api/auth/* (login, callback, refresh, logout) — fixed window per IP,
///                    the credential-stuffing / refresh-replay throttle
///   analytics-write  anonymous writes (search click tracking, CSP reports) — token bucket
///                    per IP so a burst from one page load is fine but a loop is not
///   admin            authenticated calls — sliding window per user id
///
/// <see cref="RateLimitPolicyConvention"/> assigns a policy to every controller action that
/// does not carry one explicitly, so a new endpoint is limited by default. The client key is
/// the user id when the bearer token is present, otherwise the connection's remote address
/// after forwarded-header resolution (#162), i.e. the real client behind IIS ARR.
///
/// The limit value is part of the partition key: changing a setting in Admin → Settings
/// starts fresh partitions with the new allowance on the next request, no restart.
/// api.rateLimits.enabled=false (or a per-policy 0) turns a policy into a no-op.
/// </summary>
public static class RateLimitPolicies
{
    public const string PublicRead     = "public-read";
    public const string Auth           = "auth";
    public const string AnalyticsWrite = "analytics-write";
    public const string Admin          = "admin";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddCmsRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;

            options.AddPolicy(PublicRead, http => Partition(http, PublicRead, SiteSettingKeys.ApiRateLimitsPublicReadPerMinute,
                (key, limit) => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limit, Window = Window, SegmentsPerWindow = 6, QueueLimit = 0,
                })));

            options.AddPolicy(Auth, http => Partition(http, Auth, SiteSettingKeys.ApiRateLimitsAuthPerMinute,
                (key, limit) => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit, Window = Window, QueueLimit = 0,
                })));

            options.AddPolicy(AnalyticsWrite, http => Partition(http, AnalyticsWrite, SiteSettingKeys.ApiRateLimitsAnalyticsWritePerMinute,
                (key, limit) => RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limit, TokensPerPeriod = limit, ReplenishmentPeriod = Window, QueueLimit = 0, AutoReplenishment = true,
                })));

            options.AddPolicy(Admin, http => Partition(http, Admin, SiteSettingKeys.ApiRateLimitsAdminPerMinute,
                (key, limit) => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limit, Window = Window, SegmentsPerWindow = 6, QueueLimit = 0,
                })));
        });

        return services;
    }

    /// <summary>The identity a request is throttled under: "u:{userId}" when authenticated, else "ip:{address}".</summary>
    public static string ClientKey(HttpContext http)
    {
        var userId = http.User.FindFirst("cms_user_id")?.Value;
        if (!string.IsNullOrEmpty(userId)) return "u:" + userId;
        return "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private static RateLimitPartition<string> Partition(
        HttpContext http, string policy, string settingKey,
        Func<string, int, RateLimitPartition<string>> create)
    {
        var settings = http.RequestServices.GetRequiredService<ISiteSettingsService>();
        if (!settings.GetBool(SiteSettingKeys.ApiRateLimitsEnabled))
            return RateLimitPartition.GetNoLimiter("off");

        var limit = settings.GetInt(settingKey);
        if (limit <= 0)
            return RateLimitPartition.GetNoLimiter("off:" + policy);

        return create($"{policy}|{limit}|{ClientKey(http)}", limit);
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : Window;
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        http.Response.Headers.RetryAfter = seconds.ToString();
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "(global)";
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("VA.CMS.API.RateLimiting")
            .LogWarning("Rate limit {Policy} exceeded by {Client} on {Method} {Path}; retry after {RetryAfterSeconds}s.",
                policy, LogSanitizer.Scrub(ClientKey(http)), LogSanitizer.Scrub(http.Request.Method),
                LogSanitizer.Scrub(http.Request.Path.Value), seconds);

        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title  = "Too many requests.",
                Detail = $"Rate limit exceeded; retry after {seconds} seconds.",
                Extensions = { ["retryAfterSeconds"] = seconds, ["correlationId"] = http.GetCorrelationId() },
            },
        });
    }
}

/// <summary>
/// Gives every controller action a rate-limit policy unless it (or its controller) already
/// declares one with [EnableRateLimiting]/[DisableRateLimiting]:
///   route under api/auth  → auth
///   [AllowAnonymous]      → public-read
///   everything else       → admin (it needs a bearer token, so it is throttled per user)
/// Anonymous *writes* (click tracking, CSP reports) carry an explicit analytics-write attribute.
/// </summary>
public sealed class RateLimitPolicyConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        if (HasExplicitPolicy(action.Attributes) || HasExplicitPolicy(action.Controller.Attributes))
            return;

        var policy = ResolvePolicy(action);
        foreach (var selector in action.Selectors)
            selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(policy));
    }

    public static string ResolvePolicy(ActionModel action)
    {
        var templates = action.Selectors.Select(s => s.AttributeRouteModel?.Template)
            .Concat(action.Controller.Selectors.Select(s => s.AttributeRouteModel?.Template))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!.TrimStart('/'));
        if (templates.Any(t => t.StartsWith("api/auth", StringComparison.OrdinalIgnoreCase)))
            return RateLimitPolicies.Auth;

        var anonymous = action.Attributes.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any()
                        || (action.Controller.Attributes.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any()
                            && !action.Attributes.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any());
        return anonymous ? RateLimitPolicies.PublicRead : RateLimitPolicies.Admin;
    }

    private static bool HasExplicitPolicy(IReadOnlyList<object> attributes)
        => attributes.Any(a => a is EnableRateLimitingAttribute or DisableRateLimitingAttribute);
}
