using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using VA.CMS.API.Auth;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Webhook registration endpoints.
///
///   POST   /api/v1/webhooks                                   — register a new webhook (CanDevelop)
///   GET    /api/v1/webhooks                                   — list registered webhooks (CanDevelop)
///   DELETE /api/v1/webhooks/{id}                              — remove a webhook (CanDevelop)
///   GET    /api/v1/webhooks/{id}/deliveries                   — delivery log, newest first (CanDevelop)
///   POST   /api/v1/webhooks/{id}/deliveries/{deliveryId}/redeliver — resend a logged payload once (CanDevelop)
///
/// Registration runs the URL through <see cref="WebhookDestinationPolicy"/> (#168): https
/// outside Development, host on webhooks.allowedHosts, no loopback/link-local/private
/// literal addresses. The secret is returned once and stored protected.
///
/// Issue #54 — BRD FR-DEV-07.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookRepository _webhooks;
    private readonly WebhookDestinationPolicy _policy;
    private readonly IWebhookSecretProtector _secrets;
    private readonly IWebhookDispatcher _dispatcher;

    public WebhooksController(
        IWebhookRepository webhooks,
        WebhookDestinationPolicy policy,
        IWebhookSecretProtector secrets,
        IWebhookDispatcher dispatcher)
    {
        _webhooks   = webhooks;
        _policy     = policy;
        _secrets    = secrets;
        _dispatcher = dispatcher;
    }

    // ── POST /api/v1/webhooks ─────────────────────────────────────────────────

    /// <summary>Register a new webhook endpoint.</summary>
    [HttpPost]
    [Authorize(Policy = CmsRoles.Policies.CanDevelop)]
    [ProducesResponseType(typeof(WebhookRegistrationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] WebhookRegistrationRequest request)
    {
        if (_policy.ValidateUrl(request.Url, out _) is { } urlError)
            return BadRequest(new { error = urlError });

        if (request.Events is null || request.Events.Length == 0)
            return BadRequest(new { error = "At least one event is required." });

        // Validate event names
        var invalidEvents = request.Events
            .Where(e => !KnownEvents.Contains(e))
            .ToArray();
        if (invalidEvents.Length > 0)
            return BadRequest(new { error = $"Unknown events: {string.Join(", ", invalidEvents)}." });

        // Auto-generate a secret if the caller did not provide one
        var secret = string.IsNullOrWhiteSpace(request.Secret)
            ? GenerateSecret()
            : request.Secret;

        var eventsJson = JsonSerializer.Serialize(request.Events);
        var name       = string.IsNullOrWhiteSpace(request.Name) ? request.Url : request.Name;

        var actorId = GetCurrentUserId();
        var id = await _webhooks.CreateAsync(
            name:        name,
            url:         request.Url,
            secret:      _secrets.Protect(secret),
            eventsJson:  eventsJson,
            createdById: actorId);

        return CreatedAtAction(
            nameof(Register),
            new WebhookRegistrationResponse
            {
                Id     = id,
                Name   = name,
                Url    = request.Url,
                // Return the secret ONCE at registration time — not subsequently
                Secret = secret,
                Events = request.Events,
            });
    }

    // ── GET /api/v1/webhooks ──────────────────────────────────────────────────

    /// <summary>List all registered webhooks. Secret is not returned.</summary>
    [HttpGet]
    [Authorize(Policy = CmsRoles.Policies.CanDevelop)]
    [ProducesResponseType(typeof(IEnumerable<WebhookListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var webhooks = await _webhooks.ListAllAsync();
        var items = webhooks.Select(w => new WebhookListItem
        {
            Id        = w.Id,
            Name      = w.Name,
            Url       = w.Url,
            Events    = JsonSerializer.Deserialize<string[]>(w.EventsJson) ?? Array.Empty<string>(),
            IsActive  = w.IsActive,
            CreatedAt = w.CreatedAt,
        });
        return Ok(items);
    }

    // ── DELETE /api/v1/webhooks/{id} ──────────────────────────────────────────

    /// <summary>Remove (soft-delete) a webhook by id.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = CmsRoles.Policies.CanDevelop)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id)
    {
        var existing = await _webhooks.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        await _webhooks.DeleteAsync(id);
        return NoContent();
    }

    // ── GET /api/v1/webhooks/{id}/deliveries ──────────────────────────────────

    /// <summary>Delivery log for one webhook, newest first (issue #168).</summary>
    [HttpGet("{id:long}/deliveries")]
    [Authorize(Policy = CmsRoles.Policies.CanDevelop)]
    [ProducesResponseType(typeof(WebhookDeliveryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListDeliveries(long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        if (await _webhooks.GetByIdAsync(id) is null)
            return NotFound();

        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (items, total) = await _webhooks.ListDeliveriesAsync(id, page, pageSize);

        return Ok(new WebhookDeliveryListResponse
        {
            Items     = items.Select(ToDto).ToArray(),
            Page      = page,
            PageSize  = pageSize,
            TotalRows = total,
        });
    }

    // ── POST /api/v1/webhooks/{id}/deliveries/{deliveryId}/redeliver ─────────

    /// <summary>
    /// Resend the payload of a logged delivery once (issue #168). The destination is
    /// re-validated; the outcome is recorded as a new delivery row and returned.
    /// </summary>
    [HttpPost("{id:long}/deliveries/{deliveryId:long}/redeliver")]
    [Authorize(Policy = CmsRoles.Policies.CanDevelop)]
    [ProducesResponseType(typeof(WebhookDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Redeliver(long id, long deliveryId, CancellationToken ct)
    {
        var webhook = await _webhooks.GetByIdAsync(id);
        if (webhook is null)
            return NotFound();
        if (!webhook.IsActive)
            return Conflict(new { error = "Webhook is inactive." });

        var original = await _webhooks.GetDeliveryAsync(deliveryId);
        if (original is null || original.WebhookId != id)
            return NotFound();

        var delivery = await _dispatcher.RedeliverAsync(webhook, original, ct);
        return Ok(ToDto(delivery));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WebhookDeliveryDto ToDto(WebhookDelivery d) => new()
    {
        Id                 = d.Id,
        WebhookId          = d.WebhookId,
        EventName          = d.EventName,
        PayloadJson        = d.PayloadJson,
        ResponseStatusCode = d.ResponseStatusCode,
        AttemptNumber      = d.AttemptNumber,
        DeliveredAt        = d.DeliveredAt ?? DateTime.UtcNow,
        ErrorMessage       = d.ErrorMessage,
        RedeliveryOfId     = d.RedeliveryOfId,
        Succeeded          = d.ResponseStatusCode is >= 200 and <= 299,
    };

    private long GetCurrentUserId()
    {
        var claim = User.FindFirst("cms_user_id")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(claim, out var id) ? id : 0L;
    }

    private static string GenerateSecret()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static readonly HashSet<string> KnownEvents = new(WebhookEvents.All, StringComparer.OrdinalIgnoreCase);
}

// ── Request / Response DTOs ─────────────────────────────────────────────────

public record WebhookRegistrationRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }
    [MaxLength(2000)]
    public string Url { get; init; } = string.Empty;
    /// <summary>Optional — server generates one if omitted. Stored protected at rest (issue #168).</summary>
    [MaxLength(200)]
    public string? Secret { get; init; }
    [MaxLength(20)]
    public string[] Events { get; init; } = Array.Empty<string>();
}

public record WebhookRegistrationResponse
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    /// <summary>Returned once at registration. Store it — the server will not show it again.</summary>
    public string Secret { get; init; } = string.Empty;
    public string[] Events { get; init; } = Array.Empty<string>();
}

public record WebhookListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string[] Events { get; init; } = Array.Empty<string>();
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>One row of the delivery log (issue #168).</summary>
public record WebhookDeliveryDto
{
    public long Id { get; init; }
    public long WebhookId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = "{}";
    public int? ResponseStatusCode { get; init; }
    public int AttemptNumber { get; init; }
    public DateTime DeliveredAt { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>Set when this row is an operator redelivery of an earlier one.</summary>
    public long? RedeliveryOfId { get; init; }
    public bool Succeeded { get; init; }
}

public record WebhookDeliveryListResponse
{
    public WebhookDeliveryDto[] Items { get; init; } = Array.Empty<WebhookDeliveryDto>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalRows { get; init; }
}
