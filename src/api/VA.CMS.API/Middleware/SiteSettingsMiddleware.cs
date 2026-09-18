using Microsoft.AspNetCore.Http.Features;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Middleware;

/// <summary>
/// Request-time enforcement of site settings that cannot be applied at startup because the
/// endpoints they govern are mapped once (epic #141, issues #144 / #145):
///
///   features.graphql    — /api/graphql answers 404 while off
///   features.swaggerUi  — /swagger answers 404 while off (always on in Development)
///   media.maxUploadBytes — request body limit for POST /api/v1/media/upload, replacing the
///                          compile-time [RequestSizeLimit]; must run before the form is read
///   api.maxRequestBodyBytes — request body limit for every other request (#167), well below
///                          Kestrel's / IIS's 30 MB default; a bigger body is 413 before any
///                          model binding reads it
/// </summary>
public static class SiteSettingsMiddleware
{
    public static IApplicationBuilder UseSiteSettingGates(this IApplicationBuilder app, bool isDevelopment)
    {
        return app.Use(async (context, next) =>
        {
            var settings = context.RequestServices.GetRequiredService<ISiteSettingsService>();
            var path     = context.Request.Path;

            if (path.StartsWithSegments("/api/graphql") && !settings.GetBool(SiteSettingKeys.FeatureGraphQl))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (path.StartsWithSegments("/swagger") && !isDevelopment && !settings.GetBool(SiteSettingKeys.FeatureSwaggerUi))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var bodyFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyFeature is not null && !bodyFeature.IsReadOnly)
            {
                if (HttpMethods.IsPost(context.Request.Method) && path.StartsWithSegments("/api/v1/media/upload"))
                {
                    var maxBytes = settings.GetLong(SiteSettingKeys.MediaMaxUploadBytes);
                    // Small headroom for multipart boundaries/headers so a file exactly at the
                    // limit is rejected by the service's clear message, not by a 413 from Kestrel.
                    if (maxBytes > 0)
                        bodyFeature.MaxRequestBodySize = maxBytes + 64 * 1024;
                }
                else
                {
                    var maxBytes = settings.GetLong(SiteSettingKeys.ApiMaxRequestBodyBytes);
                    if (maxBytes > 0)
                        bodyFeature.MaxRequestBodySize = maxBytes;
                }
            }

            await next(context);
        });
    }
}
