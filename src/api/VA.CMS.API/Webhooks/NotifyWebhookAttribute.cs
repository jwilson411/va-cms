using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Fires a webhook event after any successful (2xx) non-GET action on the decorated
/// controller/action. The payload is the request's route values (e.g. { handle }),
/// which is enough for subscribers such as the public site's cache revalidation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NotifyWebhookAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _eventName;

    public NotifyWebhookAttribute(string eventName)
    {
        _eventName = eventName;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        if (HttpMethods.IsGet(context.HttpContext.Request.Method)) return;
        if (executed.Exception is not null && !executed.ExceptionHandled) return;

        var status = executed.Result switch
        {
            IStatusCodeActionResult s => s.StatusCode ?? StatusCodes.Status200OK,
            null                      => context.HttpContext.Response.StatusCode,
            _                         => StatusCodes.Status200OK,
        };
        if (status is < 200 or > 299) return;

        var payload = context.RouteData.Values
            .Where(kv => kv.Key is not ("controller" or "action"))
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());

        await context.HttpContext.RequestServices
            .GetRequiredService<IWebhookBackgroundDispatcher>()
            .EnqueueAsync(_eventName, payload, context.HttpContext.RequestAborted);
    }
}
