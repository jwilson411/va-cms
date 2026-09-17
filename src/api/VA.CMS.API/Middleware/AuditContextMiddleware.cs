using VA.CMS.Infrastructure.Data;

namespace VA.CMS.API.Middleware;

/// <summary>
/// Fills the scope's <see cref="AuditContext"/> once authentication has run (#165,
/// NIST AU-3 "where"): actor from the bearer token, client address after
/// forwarded-header resolution, User-Agent and a correlation id. CmsDatabase pushes
/// the values into SESSION_CONTEXT on every connection it opens, and
/// AuditLogRepository stamps them on rows written from C#.
///
/// The correlation id honours an inbound X-Correlation-Id (an IIS ARR or load
/// balancer can set one) and otherwise uses the request's trace identifier; it is
/// echoed back on the response so a support ticket can quote it. #166 will route
/// the same id through structured logging.
/// </summary>
public sealed class AuditContextMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, AuditContext audit)
    {
        var inbound = context.Request.Headers[CorrelationHeader].ToString();
        var correlation = AuditContext.Truncate(
            string.IsNullOrWhiteSpace(inbound) ? context.TraceIdentifier : inbound.Trim(),
            AuditContext.CorrelationIdMaxLength);

        audit.ActorId       = long.TryParse(context.User.FindFirst("cms_user_id")?.Value, out var id) ? id : null;
        audit.SourceIp      = AuditContext.Truncate(context.Connection.RemoteIpAddress?.ToString(), AuditContext.SourceIpMaxLength);
        audit.UserAgent     = AuditContext.Truncate(context.Request.Headers.UserAgent.ToString(), AuditContext.UserAgentMaxLength);
        audit.CorrelationId = correlation;

        if (correlation is not null)
            context.Response.Headers[CorrelationHeader] = correlation;

        return _next(context);
    }
}

public static class AuditContextMiddlewareExtensions
{
    /// <summary>Registers the audit-context middleware. Place it after UseAuthentication() so the actor claim is available.</summary>
    public static IApplicationBuilder UseAuditContext(this IApplicationBuilder app)
        => app.UseMiddleware<AuditContextMiddleware>();
}
