using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Webhook event names.
/// Issue #54 — BRD FR-DEV-07.
/// </summary>
public static class WebhookEvents
{
    public const string ContentPublished   = "content.published";
    public const string ContentUnpublished = "content.unpublished";
    public const string ContentArchived    = "content.archived";
    public const string MediaUploaded      = "media.uploaded";
    public const string NavigationUpdated  = "navigation.updated";
}

/// <summary>
/// Dispatches webhook deliveries for CMS events.
/// Signs each payload with HMAC-SHA256 in X-CMS-Signature header.
/// Retries up to 3 times with exponential backoff on non-2xx responses.
/// Issue #54 — BRD FR-DEV-07.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>
    /// Fanout-deliver the event payload to all active webhooks subscribed to <paramref name="eventName"/>.
    /// Awaits all deliveries (including retries) before returning.
    /// </summary>
    Task DispatchAsync(string eventName, object payload, CancellationToken ct = default);
}

/// <inheritdoc />
public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly IWebhookRepository _repo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcher> _logger;

    // Retry: up to 3 attempts; delays: 5s, 25s after first failure
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(25),
    };
    private const int MaxAttempts = 3;

    public WebhookDispatcher(
        IWebhookRepository repo,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcher> logger)
    {
        _repo              = repo;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(string eventName, object payload, CancellationToken ct = default)
    {
        var targets = await _repo.GetActiveForEventAsync(eventName);
        var list    = targets.ToList();
        if (list.Count == 0) return;

        var payloadJson = JsonSerializer.Serialize(payload);

        // Fan out deliveries in parallel
        var tasks = list.Select(t => DeliverWithRetryAsync(t, eventName, payloadJson, ct));
        await Task.WhenAll(tasks);
    }

    private async Task DeliverWithRetryAsync(
        Webhook target,
        string eventName,
        string payloadJson,
        CancellationToken ct)
    {
        var attempt = 0;
        while (attempt < MaxAttempts)
        {
            attempt++;
            var (statusCode, errorMsg) = await TrySendAsync(target.Url, target.Secret ?? string.Empty, eventName, payloadJson, ct);

            await _repo.CreateDeliveryAsync(new WebhookDelivery
            {
                WebhookId          = target.Id,
                EventName          = eventName,
                PayloadJson        = payloadJson,
                ResponseStatusCode = statusCode,
                AttemptNumber      = attempt,
                DeliveredAt        = DateTime.UtcNow,
                ErrorMessage       = errorMsg,
            });

            var success = statusCode is >= 200 and <= 299;
            if (success)
            {
                _logger.LogInformation(
                    "Webhook {WebhookId} delivered event {Event} on attempt {Attempt} — HTTP {Status}",
                    target.Id, eventName, attempt, statusCode);
                return;
            }

            _logger.LogWarning(
                "Webhook {WebhookId} delivery failed on attempt {Attempt}/{MaxAttempts} — HTTP {Status}: {Error}",
                target.Id, attempt, MaxAttempts, statusCode, errorMsg);

            if (attempt < MaxAttempts)
            {
                var delay = RetryDelays[attempt - 1]; // attempt 1→5s, attempt 2→25s
                await Task.Delay(delay, ct);
            }
        }

        _logger.LogError(
            "Webhook {WebhookId} exhausted {MaxAttempts} attempts for event {Event}. Giving up.",
            target.Id, MaxAttempts, eventName);
    }

    private async Task<(int? StatusCode, string? ErrorMessage)> TrySendAsync(
        string url, string secret, string eventName, string payloadJson, CancellationToken ct)
    {
        try
        {
            var signature = ComputeSignature(secret, payloadJson);
            var client    = _httpClientFactory.CreateClient("WebhookClient");
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("X-CMS-Signature", $"sha256={signature}");
            request.Headers.TryAddWithoutValidation("X-CMS-Event", eventName);   // lets one endpoint handle several events

            using var response = await client.SendAsync(request, ct);
            return ((int)response.StatusCode, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception sending webhook to {Url}", url);
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Compute HMAC-SHA256 of the payload bytes using the webhook secret.
    /// Returns the lowercase hex digest (without the "sha256=" prefix).
    /// </summary>
    public static string ComputeSignature(string secret, string payload)
    {
        var keyBytes     = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac   = new HMACSHA256(keyBytes);
        var hashBytes    = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
