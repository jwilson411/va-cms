using System.Text.Json;
using VA.CMS.Infrastructure.Outbox;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Front door for raising a webhook event from a request (controllers, <see cref="NotifyWebhookAttribute"/>).
///
/// Since #171 the event is written to the transactional outbox — one [OutboundEvent] row per
/// active subscriber, fanned out by usp_OutboundEvent_Enqueue — and delivered by
/// <see cref="OutboxDispatcherWorker"/> through <see cref="OutboxWebhookConsumer"/> on whichever
/// node claims it. The request never waits on a subscriber, and an app-pool recycle between the
/// domain change and the delivery no longer loses the event. Enqueue is a no-op while the
/// features.webhooks site setting is off (issue #144). An outbox write failure is logged, not
/// surfaced: the domain change has already been committed.
///
/// The row is written on the request thread right after the domain stored procedure returns,
/// i.e. durably before the response — but not inside that procedure's transaction (the
/// scheduler's claim SPs do write theirs inside; see usp_ContentEntry_ClaimScheduledForPublish).
/// The residual window is a process crash between the two statements.
/// </summary>
public interface IWebhookBackgroundDispatcher
{
    Task EnqueueAsync(string eventName, object payload, CancellationToken ct = default);
}

public sealed class OutboxWebhookDispatcher : IWebhookBackgroundDispatcher
{
    private readonly IOutboxRepository _outbox;
    private readonly ILogger<OutboxWebhookDispatcher> _logger;
    private readonly ISiteSettingsService _settings;

    public OutboxWebhookDispatcher(
        IOutboxRepository outbox,
        ILogger<OutboxWebhookDispatcher> logger,
        ISiteSettingsService settings)
    {
        _outbox   = outbox;
        _logger   = logger;
        _settings = settings;
    }

    public async Task EnqueueAsync(string eventName, object payload, CancellationToken ct = default)
    {
        if (!_settings.GetBool(SiteSettingKeys.FeatureWebhooks))
        {
            _logger.LogDebug("features.webhooks is off; {Event} not dispatched.", eventName);
            return;
        }

        try
        {
            var rows = await _outbox.EnqueueAsync(OutboundEventTypes.Webhook, eventName, JsonSerializer.Serialize(payload), ct);
            _logger.LogDebug("Webhook event {Event} queued for {Count} subscriber(s).", eventName, rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not queue webhook event {Event} to the outbox.", eventName);
        }
    }
}
