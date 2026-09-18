using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

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
    /// <summary>A site setting was changed or reset (epic #141). Payload: { key, scope }.</summary>
    public const string SettingsUpdated    = "settings.updated";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ContentPublished, ContentUnpublished, ContentArchived, MediaUploaded, NavigationUpdated, SettingsUpdated,
    };
}

/// <summary>
/// Dispatches webhook deliveries for CMS events.
/// Signs each payload with HMAC-SHA256 in X-CMS-Signature header.
/// Retries on non-2xx responses; attempts, delays and timeout are the webhooks.* site
/// settings (issue #147), defaults 3 attempts / 5 s, 25 s / 15 s.
/// Every delivery re-checks the destination against <see cref="WebhookDestinationPolicy"/>
/// (#168): a webhook whose host fell off webhooks.allowedHosts, or that resolves to a
/// private address, is logged as refused and not retried. Secrets come out of the table
/// protected and are unprotected only for the moment of signing.
/// Issue #54 — BRD FR-DEV-07.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>
    /// Fanout-deliver the event payload to all active webhooks subscribed to <paramref name="eventName"/>.
    /// Awaits all deliveries (including retries) before returning.
    /// </summary>
    Task DispatchAsync(string eventName, object payload, CancellationToken ct = default);

    /// <summary>
    /// Operator-triggered resend of a logged delivery (#168): one attempt, no retry, recorded
    /// as a new delivery row that points back at <paramref name="original"/>.
    /// </summary>
    Task<WebhookDelivery> RedeliverAsync(Webhook target, WebhookDelivery original, CancellationToken ct = default);
}

/// <inheritdoc />
public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly IWebhookRepository _repo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly ISiteSettingsService _settings;
    private readonly WebhookDestinationPolicy _policy;
    private readonly IWebhookSecretProtector _secrets;

    public WebhookDispatcher(
        IWebhookRepository repo,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcher> logger,
        ISiteSettingsService? settings = null,
        WebhookDestinationPolicy? policy = null,
        IWebhookSecretProtector? secrets = null)
    {
        _repo              = repo;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
        _settings          = settings ?? StaticSiteSettings.Defaults;
        // The null defaults exist for unit tests that construct the dispatcher by hand; DI always
        // supplies the real policy (environment-aware) and the Data Protection-backed protector.
        _policy            = policy  ?? new WebhookDestinationPolicy(_settings, isDevelopment: true);
        _secrets           = secrets ?? PassthroughSecretProtector.Instance;
    }

    private int MaxAttempts => Math.Max(1, _settings.GetInt(SiteSettingKeys.WebhooksMaxAttempts));

    /// <summary>Delay before retry number <paramref name="attempt"/> (1-based). The last configured delay repeats.</summary>
    private TimeSpan RetryDelay(int attempt)
    {
        var delays = _settings.GetIntList(SiteSettingKeys.WebhooksRetryDelaysSeconds);
        if (delays.Count == 0) return TimeSpan.FromSeconds(5);
        var idx = Math.Min(attempt - 1, delays.Count - 1);
        return TimeSpan.FromSeconds(Math.Max(0, delays[idx]));
    }

    private TimeSpan Timeout => TimeSpan.FromSeconds(Math.Max(1, _settings.GetInt(SiteSettingKeys.WebhooksTimeoutSeconds)));

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

    /// <inheritdoc />
    public async Task<WebhookDelivery> RedeliverAsync(Webhook target, WebhookDelivery original, CancellationToken ct = default)
    {
        var delivery = await AttemptAsync(target, original.EventName, original.PayloadJson, attempt: 1, redeliveryOfId: original.Id, ct);
        _logger.LogInformation(
            "Webhook {WebhookId} redelivery of delivery {OriginalDeliveryId} — HTTP {Status} {Error}",
            target.Id, original.Id, delivery.ResponseStatusCode, delivery.ErrorMessage);
        return delivery;
    }

    private async Task DeliverWithRetryAsync(
        Webhook target,
        string eventName,
        string payloadJson,
        CancellationToken ct)
    {
        var attempt     = 0;
        var maxAttempts = MaxAttempts;   // snapshot so one delivery sees a consistent policy
        while (attempt < maxAttempts)
        {
            attempt++;
            var delivery = await AttemptAsync(target, eventName, payloadJson, attempt, redeliveryOfId: null, ct);
            var statusCode = delivery.ResponseStatusCode;

            var success = statusCode is >= 200 and <= 299;
            if (success)
            {
                _logger.LogInformation(
                    "Webhook {WebhookId} delivered event {Event} on attempt {Attempt} — HTTP {Status}",
                    target.Id, eventName, attempt, statusCode);
                return;
            }

            if (delivery.Refused)
            {
                // Policy refusals do not change with time; retrying would only hammer the log.
                _logger.LogWarning(
                    "Webhook {WebhookId} delivery of {Event} refused by the destination policy: {Error}",
                    target.Id, eventName, delivery.ErrorMessage);
                return;
            }

            _logger.LogWarning(
                "Webhook {WebhookId} delivery failed on attempt {Attempt}/{MaxAttempts} — HTTP {Status}: {Error}",
                target.Id, attempt, maxAttempts, statusCode, delivery.ErrorMessage);

            if (attempt < maxAttempts)
            {
                await Task.Delay(RetryDelay(attempt), ct);   // defaults: attempt 1→5s, attempt 2→25s
            }
        }

        _logger.LogError(
            "Webhook {WebhookId} exhausted {MaxAttempts} attempts for event {Event}. Giving up.",
            target.Id, maxAttempts, eventName);
    }

    /// <summary>One send, one delivery row. <see cref="WebhookDelivery.Refused"/> marks a policy refusal (never retried).</summary>
    private async Task<WebhookDelivery> AttemptAsync(
        Webhook target, string eventName, string payloadJson, int attempt, long? redeliveryOfId, CancellationToken ct)
    {
        int? statusCode; string? errorMsg; bool refused;

        if (_policy.ValidateUrl(target.Url, out _) is { } policyError)
        {
            (statusCode, errorMsg, refused) = (null, "Refused: " + policyError, true);
        }
        else
        {
            var secret = _secrets.Unprotect(target.Secret ?? string.Empty);
            (statusCode, errorMsg, refused) = await TrySendAsync(target.Url, secret, eventName, payloadJson, ct);
        }

        var delivery = new WebhookDelivery
        {
            WebhookId          = target.Id,
            EventName          = eventName,
            PayloadJson        = payloadJson,
            ResponseStatusCode = statusCode,
            AttemptNumber      = attempt,
            DeliveredAt        = DateTime.UtcNow,
            ErrorMessage       = errorMsg,
            RedeliveryOfId     = redeliveryOfId,
            Refused            = refused,
        };
        delivery.Id = await _repo.CreateDeliveryAsync(delivery);
        return delivery;
    }

    private async Task<(int? StatusCode, string? ErrorMessage, bool Refused)> TrySendAsync(
        string url, string secret, string eventName, string payloadJson, CancellationToken ct)
    {
        try
        {
            var signature = ComputeSignature(secret, payloadJson);
            var client    = _httpClientFactory.CreateClient(WebhookHttpHandler.ClientName);
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("X-CMS-Signature", $"sha256={signature}");
            request.Headers.TryAddWithoutValidation("X-CMS-Event", eventName);   // lets one endpoint handle several events

            // Per-delivery timeout (webhooks.timeoutSeconds) — the named client is created once at
            // startup, so its Timeout cannot follow the setting; a linked token can.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);
            // Headers only: the body is never read (MaxResponseContentBufferSize is the backstop).
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            return ((int)response.StatusCode, null, false);
        }
        catch (WebhookDestinationException ex)
        {
            _logger.LogWarning("Webhook destination refused for {Url}: {Reason}", url, ex.Message);
            return (null, "Refused: " + ex.Message, true);
        }
        catch (HttpRequestException ex) when (ex.InnerException is WebhookDestinationException inner)
        {
            _logger.LogWarning("Webhook destination refused for {Url}: {Reason}", url, inner.Message);
            return (null, "Refused: " + inner.Message, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception sending webhook to {Url}", url);
            return (null, ex.Message, false);
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

    /// <summary>Secrets stored as given — unit tests only; DI registers <see cref="WebhookSecretProtector"/>.</summary>
    private sealed class PassthroughSecretProtector : IWebhookSecretProtector
    {
        public static readonly PassthroughSecretProtector Instance = new();
        public string Protect(string secret) => secret;
        public string Unprotect(string stored) => stored;
        public bool IsProtected(string stored) => false;
    }
}
