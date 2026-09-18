using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Middleware;

/// <summary>
/// Security response headers for every API response (#162, BRD FR-SECURITY-01/03,
/// IIS/ASP.NET STIG). Runs first in the pipeline so 401/404/500 responses carry
/// the headers too; later code may override a header for a specific response
/// (media serve sets its own Content-Security-Policy).
///
///   Strict-Transport-Security   1 year, includeSubDomains, preload per security.hstsPreload;
///                               only on HTTPS responses outside Development
///   Content-Security-Policy     API: default-src 'none' (nothing renders here); /swagger gets a
///                               policy that lets Swagger UI run. Report-Only while
///                               security.cspReportOnly is on; violations POST to the report endpoint
///   X-Content-Type-Options      nosniff
///   X-Frame-Options / frame-ancestors 'none'
///   Referrer-Policy             strict-origin-when-cross-origin
///   Permissions-Policy          every powerful feature off
///   Cross-Origin-Opener-Policy  same-origin
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    public const string ReportPath = "/api/v1/security/csp-report";

    public const string ApiCsp =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Swagger UI ships inline bootstrap script and styles; keep it to self + inline, no frames.</summary>
    public const string SwaggerCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public const string PermissionsPolicy =
        "accelerometer=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), " +
        "microphone=(), midi=(), payment=(), usb=(), xr-spatial-tracking=()";

    public async Task InvokeAsync(HttpContext context, ISiteSettingsService settings)
    {
        Apply(context, settings, onlyIfMissing: false);

        // The exception handler (#166) clears the response before writing ProblemDetails,
        // which drops everything set above; OnStarting survives that and puts the headers
        // back without overriding a policy an endpoint chose deliberately (media serve CSP).
        context.Response.OnStarting(state =>
        {
            var (ctx, cfg) = ((HttpContext, ISiteSettingsService))state;
            Apply(ctx, cfg, onlyIfMissing: true);
            return Task.CompletedTask;
        }, (context, settings));

        await next(context);
    }

    private void Apply(HttpContext context, ISiteSettingsService settings, bool onlyIfMissing)
    {
        var headers = context.Response.Headers;

        Set(headers, "X-Content-Type-Options", "nosniff", onlyIfMissing);
        Set(headers, "X-Frame-Options",        "DENY", onlyIfMissing);
        Set(headers, "Referrer-Policy",        "strict-origin-when-cross-origin", onlyIfMissing);
        Set(headers, "Permissions-Policy",     PermissionsPolicy, onlyIfMissing);
        Set(headers, "Cross-Origin-Opener-Policy", "same-origin", onlyIfMissing);

        var csp = context.Request.Path.StartsWithSegments("/swagger") ? SwaggerCsp : ApiCsp;
        Set(headers, "Reporting-Endpoints", $"csp=\"{ReportPath}\"", onlyIfMissing);
        var cspWithReporting = $"{csp}; report-to csp; report-uri {ReportPath}";
        if (!onlyIfMissing || (!headers.ContainsKey("Content-Security-Policy") && !headers.ContainsKey("Content-Security-Policy-Report-Only")))
        {
            headers[settings.GetBool(SiteSettingKeys.SecurityCspReportOnly)
                ? "Content-Security-Policy-Report-Only"
                : "Content-Security-Policy"] = cspWithReporting;
        }

        if (context.Request.IsHttps && !env.IsDevelopment())
        {
            Set(headers, "Strict-Transport-Security", settings.GetBool(SiteSettingKeys.SecurityHstsPreload)
                ? "max-age=31536000; includeSubDomains; preload"
                : "max-age=31536000; includeSubDomains", onlyIfMissing);
        }
    }

    private static void Set(IHeaderDictionary headers, string name, string value, bool onlyIfMissing)
    {
        if (onlyIfMissing && headers.ContainsKey(name)) return;
        headers[name] = value;
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
