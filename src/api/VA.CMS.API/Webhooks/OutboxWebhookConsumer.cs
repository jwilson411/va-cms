using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Outbox;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Outbox consumer for <see cref="OutboundEventTypes.Webhook"/> rows (#171): one HTTP attempt
/// per claim through <see cref="IWebhookDispatcher.DeliverOnceAsync"/>, which writes the
/// [WebhookDelivery] row the admin delivery log shows. A non-2xx or transport failure is
/// rescheduled on the webhooks.retryDelaysSeconds schedule until webhooks.maxAttempts; a
/// destination-policy refusal (#168) is final. A subscriber that was deactivated after the
/// event was queued is skipped, not delivered.
/// </summary>
public sealed class OutboxWebhookConsumer : IOutboxConsumer
{
    private readonly IWebhookRepository _webhooks;
    private readonly IWebhookDispatcher _dispatcher;
    private readonly ISiteSettingsService _settings;

    public OutboxWebhookConsumer(IWebhookRepository webhooks, IWebhookDispatcher dispatcher, ISiteSettingsService settings)
    {
        _webhooks   = webhooks;
        _dispatcher = dispatcher;
        _settings   = settings;
    }

    public string Type => OutboundEventTypes.Webhook;

    public async Task<OutboxOutcome> HandleAsync(OutboundEvent evt, CancellationToken ct)
    {
        if (evt.WebhookId is not { } webhookId || string.IsNullOrEmpty(evt.EventName))
            return OutboxOutcome.Failed("Webhook outbox row has no WebhookId/EventName.");

        if (!_settings.GetBool(SiteSettingKeys.FeatureWebhooks))
            return OutboxOutcome.Failed("features.webhooks is off.");

        var target = await _webhooks.GetByIdAsync(webhookId);
        if (target is null || !target.IsActive)
            return OutboxOutcome.Failed($"Webhook {webhookId} is no longer active.");

        var delivery = await _dispatcher.DeliverOnceAsync(target, evt.EventName, evt.PayloadJson, evt.Attempts, ct);

        if (delivery.ResponseStatusCode is >= 200 and <= 299)
            return OutboxOutcome.Succeeded;

        var error = $"HTTP {delivery.ResponseStatusCode?.ToString() ?? "-"}: {delivery.ErrorMessage}";
        if (delivery.Refused)
            return OutboxOutcome.Failed(error);   // policy refusals do not change with time

        return evt.Attempts >= WebhookRetryPolicy.MaxAttempts(_settings)
            ? OutboxOutcome.Failed(error)
            : OutboxOutcome.Retry(WebhookRetryPolicy.Delay(_settings, evt.Attempts), error);
    }
}
