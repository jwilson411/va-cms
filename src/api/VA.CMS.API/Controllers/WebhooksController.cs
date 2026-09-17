using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using VA.CMS.API.Auth;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Webhook registration endpoints.
///
///   POST   /api/v1/webhooks        — register a new webhook (CanDevelop)
///   GET    /api/v1/webhooks        — list registered webhooks (CanDevelop)
///   DELETE /api/v1/webhooks/{id}   — remove a webhook (CanDevelop)
///
/// Issue #54 — BRD FR-DEV-07.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookRepository _webhooks;

    public WebhooksController(IWebhookRepository webhooks)
    {
        _webhooks = webhooks;
    }

    // ── POST /api/v1/webhooks ─────────────────────────────────────────────────

    /// <summary>Register a new webhook endpoint.</summary>
    [HttpPost]
    [Authorize(Policy = CmsRoles.Policies.CanDevelop)]
    [ProducesResponseType(typeof(WebhookRegistrationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] WebhookRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { error = "Url is required." });

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return BadRequest(new { error = "Url must be a valid HTTP/HTTPS URL." });

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
            secret:      secret,
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

    // ── Helpers ──────────────────────────────────────────────────────────────

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
    public string? Name { get; init; }
    public string Url { get; init; } = string.Empty;
    /// <summary>Optional — server generates one if omitted.</summary>
    public string? Secret { get; init; }
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
