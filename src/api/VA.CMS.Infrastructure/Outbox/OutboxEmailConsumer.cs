using System.Text.Json;
using Microsoft.Extensions.Logging;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Outbox;

/// <summary>
/// Outbox consumer for <see cref="OutboundEventTypes.Email"/> rows: one SMTP session per row
/// through <see cref="IEmailSender"/>. A relay failure is retried on the
/// notifications.emailRetryDelaysSeconds schedule up to notifications.emailMaxAttempts.
/// </summary>
public sealed class OutboxEmailConsumer : IOutboxConsumer
{
    private readonly IEmailSender _sender;
    private readonly ISiteSettingsService _settings;
    private readonly ILogger<OutboxEmailConsumer> _logger;

    public OutboxEmailConsumer(IEmailSender sender, ISiteSettingsService settings, ILogger<OutboxEmailConsumer> logger)
    {
        _sender   = sender;
        _settings = settings;
        _logger   = logger;
    }

    public string Type => OutboundEventTypes.Email;

    public async Task<OutboxOutcome> HandleAsync(OutboundEvent evt, CancellationToken ct)
    {
        EmailMessage[]? messages;
        try
        {
            messages = JsonSerializer.Deserialize<EmailMessage[]>(evt.PayloadJson);
        }
        catch (JsonException ex)
        {
            return OutboxOutcome.Failed("Payload is not an EmailMessage[]: " + ex.Message);
        }
        if (messages is null || messages.Length == 0) return OutboxOutcome.Succeeded;

        try
        {
            await _sender.SendAsync(messages, ct);
            return OutboxOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email delivery of {Count} message(s) ({Subject}) failed on attempt {Attempt}.",
                messages.Length, messages[0].Subject, evt.Attempts);
            var maxAttempts = Math.Max(1, _settings.GetInt(SiteSettingKeys.NotificationsEmailMaxAttempts));
            return evt.Attempts >= maxAttempts
                ? OutboxOutcome.Failed(ex.Message)
                : OutboxOutcome.Retry(RetryDelay(evt.Attempts), ex.Message);
        }
    }

    /// <summary>Delay before retry number <paramref name="attempt"/> (1-based); the last configured value repeats.</summary>
    private TimeSpan RetryDelay(int attempt)
    {
        var delays = _settings.GetIntList(SiteSettingKeys.NotificationsEmailRetryDelaysSeconds);
        if (delays.Count == 0) return TimeSpan.FromSeconds(30);
        var idx = Math.Min(Math.Max(attempt, 1) - 1, delays.Count - 1);
        return TimeSpan.FromSeconds(Math.Max(0, delays[idx]));
    }
}
