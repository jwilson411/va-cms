using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Fire-and-forget front door for <see cref="IWebhookDispatcher"/>.
///
/// The dispatcher awaits every delivery including retries (5 s, then 25 s), so it
/// must not run on the request thread — a publish would hang while a dead
/// subscriber times out. Enqueue() runs the dispatch on the thread pool in its own
/// DI scope (the dispatcher and its repository are scoped services) and logs
/// failures instead of surfacing them to the caller. Enqueue is a no-op while the
/// features.webhooks site setting is off (issue #144).
/// </summary>
public interface IWebhookBackgroundDispatcher
{
    void Enqueue(string eventName, object payload);
}

public sealed class WebhookBackgroundDispatcher : IWebhookBackgroundDispatcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookBackgroundDispatcher> _logger;
    private readonly ISiteSettingsService _settings;

    public WebhookBackgroundDispatcher(
        IServiceScopeFactory scopes,
        ILogger<WebhookBackgroundDispatcher> logger,
        ISiteSettingsService settings)
    {
        _scopes   = scopes;
        _logger   = logger;
        _settings = settings;
    }

    public void Enqueue(string eventName, object payload)
    {
        if (!_settings.GetBool(SiteSettingKeys.FeatureWebhooks))
        {
            _logger.LogDebug("features.webhooks is off; {Event} not dispatched.", eventName);
            return;
        }

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
