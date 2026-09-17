using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VA.CMS.API.Auth;

namespace VA.CMS.API.Middleware;

/// <summary>
/// Outside Development, /swagger (UI and swagger.json) requires a bearer token that
/// satisfies CanDevelop (#162). Swagger runs ahead of the routing/auth middleware, so
/// this gate authenticates the JWT bearer scheme itself. The features.swaggerUi gate
/// (404 while off) runs earlier and still applies.
/// </summary>
public static class SwaggerAccessMiddleware
{
    public static IApplicationBuilder UseSwaggerAccessGate(this IApplicationBuilder app, bool isDevelopment)
    {
        if (isDevelopment)
            return app;

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/swagger"))
            {
                await next(context);
                return;
            }

            var auth = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (auth.Succeeded && auth.Principal is not null)
                context.User = auth.Principal;

            if (context.User?.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }

            var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            if (!(await authorization.AuthorizeAsync(context.User, CmsRoles.Policies.CanDevelop)).Succeeded)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
    }
}
