using Serilog.Context;
using VA.CMS.Infrastructure.Data;

namespace VA.CMS.API.Middleware;

/// <summary>
/// One id per request, everywhere (#166, NIST AU-3): accepted from an inbound
/// <c>X-Correlation-Id</c> (IIS ARR or the load balancer can set one) or taken from the
/// request's trace identifier, echoed on the response, pushed onto the Serilog
/// LogContext so every log line of the request carries it, and read by
/// <see cref="AuditContextMiddleware"/> so AuditLog.CorrelationId matches the logs.
///
/// Registered first in the pipeline so the exception handler's ProblemDetails, the
/// request-completion log line and any 5xx all carry the same id a support ticket
/// can quote. The header is (re)applied in OnStarting because the exception handler
/// clears the response before re-executing.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string Header  = "X-Correlation-Id";
    public const string ItemKey = "VA.CMS.CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = Resolve(context);
        context.Items[ItemKey] = id;
        context.Response.Headers[Header] = id;
        context.Response.OnStarting(static state =>
        {
            var (ctx, value) = ((HttpContext, string))state;
            ctx.Response.Headers[Header] = value;
            return Task.CompletedTask;
        }, (context, id));

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await _next(context);
        }
    }

    /// <summary>The request's correlation id, or the trace identifier when the middleware has not run (tests, background work).</summary>
    public static string Get(HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var v) && v is string s ? s : context.TraceIdentifier;

    private static string Resolve(HttpContext context)
    {
        // An inbound id is untrusted: keep only token characters so it can be echoed as a
        // response header, logged and stored without becoming an injection vector.
        var inbound = Sanitize(context.Request.Headers[Header].ToString());
        return AuditContext.Truncate(inbound.Length == 0 ? context.TraceIdentifier : inbound, AuditContext.CorrelationIdMaxLength)!;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var chars = value.Trim().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':').ToArray();
        return new string(chars);
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>Registers the correlation-id middleware. Place it first so every later component sees the id.</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();

    /// <summary>The request's correlation id (see <see cref="CorrelationIdMiddleware"/>).</summary>
    public static string GetCorrelationId(this HttpContext context) => CorrelationIdMiddleware.Get(context);
}
