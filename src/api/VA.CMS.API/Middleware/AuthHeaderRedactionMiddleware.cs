namespace VA.CMS.API.Middleware;

/// <summary>
/// Strips the Authorization header from log output.
/// This middleware runs before the logging pipeline sees the request,
/// replacing the header value with a redacted sentinel so no bearer
/// tokens appear in application logs.
///
/// Acceptance criteria: "Auth headers never logged (middleware strips
/// Authorization header from logs)."
/// </summary>
public sealed class AuthHeaderRedactionMiddleware
{
    private readonly RequestDelegate _next;

    public AuthHeaderRedactionMiddleware(RequestDelegate next)
        => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        // Remove the Authorization header so the downstream logging middleware
        // (e.g. UseHttpLogging, Serilog RequestLogging) never writes it.
        // The request still works because authentication reads the header before
        // any logging occurs if wired correctly — but the safest approach for
        // defence-in-depth is to replace rather than remove (keeps the header
        // for auth middleware, removes its value from log visibility).
        if (context.Request.Headers.ContainsKey("Authorization"))
            context.Request.Headers["Authorization"] = "[REDACTED]";

        return _next(context);
    }
}

public static class AuthHeaderRedactionMiddlewareExtensions
{
    /// <summary>
    /// Registers the Authorization header redaction middleware.
    /// Must be called AFTER UseAuthentication() so auth middleware already
    /// read the header before we redact it.
    /// </summary>
    public static IApplicationBuilder UseAuthHeaderRedaction(this IApplicationBuilder app)
        => app.UseMiddleware<AuthHeaderRedactionMiddleware>();
}
