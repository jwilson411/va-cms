using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Admin Settings — runtime site settings and feature flags (issue #143, epic #141).
///
/// Every value here is a [SiteSetting] row; changing one takes effect on this node as soon as
/// the write returns (the snapshot is refreshed) and on other nodes within the refresh
/// interval. No build or restart is involved.
///
/// Endpoints (all require SystemAdmin):
///   GET  /api/v1/admin/settings              — every setting, grouped by category
///   PUT  /api/v1/admin/settings/{key}        — set a value (validated against its DataType)
///   POST /api/v1/admin/settings/{key}/reset  — restore the code default
/// </summary>
[ApiController]
[Route("api/v1/admin/settings")]
[Authorize(Policy = CmsRoles.Policies.CanAdminSystem)]
public class SiteSettingsController : ControllerBase
{
    private readonly ISiteSettingRepository       _repo;
    private readonly ISiteSettingsService         _settings;
    private readonly IAuditLogRepository          _audit;
    private readonly IRbacService                 _rbac;
    private readonly IWebhookBackgroundDispatcher _webhooks;

    public SiteSettingsController(
        ISiteSettingRepository       repo,
        ISiteSettingsService         settings,
        IAuditLogRepository          audit,
        IRbacService                 rbac,
        IWebhookBackgroundDispatcher webhooks)
    {
        _repo     = repo;
        _settings = settings;
        _audit    = audit;
        _rbac     = rbac;
        _webhooks = webhooks;
    }

    /// <summary>List every setting with its current value, default, type and description.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SiteSettingsListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        // Read straight from the database so the admin screen never shows a stale snapshot.
        var rows = await _repo.ListAsync(ct);
        return Ok(new SiteSettingsListResponse(
            rows.Select(SiteSettingDto.From).ToList(),
            _settings.LoadedAtUtc));
    }

    /// <summary>Set a value. Bool must be "true"/"false", int a non-negative whole number, json valid JSON.</summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(SiteSettingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Set(string key, [FromBody] SetSiteSettingRequest request, CancellationToken ct)
    {
        var definition = SiteSettingDefinitions.Find(key);
        if (definition is null)
            return NotFound(new { error = $"Unknown setting '{key}'." });

        var value = definition.Type == SiteSettingType.Bool
            ? request.Value?.Trim().ToLowerInvariant()
            : request.Value;

        var validationError = SiteSettingParser.Validate(definition, value);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        var actorId  = _rbac.GetUserId(User) ?? 0;
        var previous = _settings.GetString(definition.Key);

        if (!await _repo.SetValueAsync(definition.Key, value, actorId, ct))
            return NotFound(new { error = $"Setting '{key}' has not been provisioned yet; restart the API." });

        await _settings.RefreshAsync(ct);
        await AuditAndNotifyAsync(actorId, definition, "SiteSettingUpdated", previous, value);

        return Ok(await CurrentDtoAsync(definition.Key, ct));
    }

    /// <summary>Restore the code default for a setting.</summary>
    [HttpPost("{key}/reset")]
    [ProducesResponseType(typeof(SiteSettingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(string key, CancellationToken ct)
    {
        var definition = SiteSettingDefinitions.Find(key);
        if (definition is null)
            return NotFound(new { error = $"Unknown setting '{key}'." });

        var actorId  = _rbac.GetUserId(User) ?? 0;
        var previous = _settings.GetString(definition.Key);

        if (!await _repo.ResetAsync(definition.Key, actorId, ct))
            return NotFound(new { error = $"Setting '{key}' has not been provisioned yet; restart the API." });

        await _settings.RefreshAsync(ct);
        await AuditAndNotifyAsync(actorId, definition, "SiteSettingReset", previous, definition.Default);

        return Ok(await CurrentDtoAsync(definition.Key, ct));
    }

    private async Task<SiteSettingDto?> CurrentDtoAsync(string key, CancellationToken ct)
    {
        var row = (await _repo.ListAsync(ct)).FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return row is null ? null : SiteSettingDto.From(row);
    }

    private async Task AuditAndNotifyAsync(long actorId, SiteSettingDefinition definition, string action, string? from, string? to)
    {
        await _audit.WriteAsync(
            actorId,
            entityType: "SiteSetting",
            entityId:   0,
            action:     action,
            diffJson:   JsonSerializer.Serialize(new { key = definition.Key, from, to }));

        // Lets the public site drop its cached settings (and anything else listening).
        _webhooks.Enqueue(WebhookEvents.SettingsUpdated, new { key = definition.Key, scope = definition.Scope.ToString() });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record SetSiteSettingRequest(string? Value);

public sealed record SiteSettingsListResponse(IReadOnlyList<SiteSettingDto> Items, DateTime? SnapshotLoadedAtUtc);

public sealed record SiteSettingDto(
    string    Key,
    string?   Value,
    string?   DefaultValue,
    string?   EffectiveValue,
    bool      IsOverridden,
    string    DataType,
    string    Category,
    string    Scope,
    string?   Description,
    int       SortOrder,
    long?     UpdatedById,
    string?   UpdatedByName,
    DateTime  UpdatedAt)
{
    public static SiteSettingDto From(SiteSettingRow r) => new(
        r.Key, r.Value, r.DefaultValue, r.EffectiveValue, r.Value is not null,
        r.DataType, r.Category, r.Scope, r.Description, r.SortOrder,
        r.UpdatedById, r.UpdatedByName, r.UpdatedAt);
}
