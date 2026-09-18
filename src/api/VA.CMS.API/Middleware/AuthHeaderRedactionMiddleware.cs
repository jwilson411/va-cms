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
        // Replace the Authorization value so nothing downstream (a controller that
        // dumps headers, a future UseHttpLogging) can write it. UseSerilogRequestLogging
        // (#166) runs ahead of this middleware but never logs headers; this is
        // defence-in-depth. Authentication has already read the header by now.
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
