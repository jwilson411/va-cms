using Microsoft.Extensions.DependencyInjection;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Fire-and-forget front door for <see cref="IWebhookDispatcher"/>.
///
/// The dispatcher awaits every delivery including retries (5 s, then 25 s), so it
/// must not run on the request thread — a publish would hang while a dead
/// subscriber times out. Enqueue() runs the dispatch on the thread pool in its own
/// DI scope (the dispatcher and its repository are scoped services) and logs
/// failures instead of surfacing them to the caller.
/// </summary>
public interface IWebhookBackgroundDispatcher
{
    void Enqueue(string eventName, object payload);
}

public sealed class WebhookBackgroundDispatcher : IWebhookBackgroundDispatcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookBackgroundDispatcher> _logger;

    public WebhookBackgroundDispatcher(IServiceScopeFactory scopes, ILogger<WebhookBackgroundDispatcher> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Enqueue(string eventName, object payload)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
                await dispatcher.DispatchAsync(eventName, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background webhook dispatch for {Event} failed.", eventName);
            }
        });
    }
}
