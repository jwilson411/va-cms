using VA.CMS.Infrastructure.Data;

namespace VA.CMS.API.Middleware;

/// <summary>
/// Fills the scope's <see cref="AuditContext"/> once authentication has run (#165,
/// NIST AU-3 "where"): actor from the bearer token, client address after
/// forwarded-header resolution, User-Agent and a correlation id. CmsDatabase pushes
/// the values into SESSION_CONTEXT on every connection it opens, and
/// AuditLogRepository stamps them on rows written from C#.
///
/// The correlation id is the one <see cref="CorrelationIdMiddleware"/> resolved at the top
/// of the pipeline (#166), so audit rows, log lines and the response header all agree.
/// </summary>
public sealed class AuditContextMiddleware
{
    public const string CorrelationHeader = CorrelationIdMiddleware.Header;

    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, AuditContext audit)
    {
        audit.ActorId       = long.TryParse(context.User.FindFirst("cms_user_id")?.Value, out var id) ? id : null;
        audit.SourceIp      = AuditContext.Truncate(context.Connection.RemoteIpAddress?.ToString(), AuditContext.SourceIpMaxLength);
        audit.UserAgent     = AuditContext.Truncate(context.Request.Headers.UserAgent.ToString(), AuditContext.UserAgentMaxLength);
        audit.CorrelationId = AuditContext.Truncate(context.GetCorrelationId(), AuditContext.CorrelationIdMaxLength);

        return _next(context);
    }
}

public static class AuditContextMiddlewareExtensions
{
    /// <summary>Registers the audit-context middleware. Place it after UseAuthentication() so the actor claim is available.</summary>
    public static IApplicationBuilder UseAuditContext(this IApplicationBuilder app)
        => app.UseMiddleware<AuditContextMiddleware>();
}
