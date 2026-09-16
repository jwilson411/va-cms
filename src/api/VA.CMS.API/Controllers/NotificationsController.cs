using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers;

/// <summary>
/// In-app notification center — issue #38 (BRD FR-WORKFLOW-02 / FR-WORKFLOW-03).
///
/// Every endpoint is scoped to the signed-in user's own inbox (cms_user_id claim);
/// there is no way to read or mark another user's notifications.
///
///   GET  /api/v1/notifications?unreadOnly=false&amp;limit=50 — inbox + unread count (bell panel)
///   GET  /api/v1/notifications/unread-count               — badge only (cheap poll)
///   POST /api/v1/notifications/read      { ids: [] }       — mark the listed ids read
///   POST /api/v1/notifications/read-all                    — mark everything read
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = CmsRoles.Policies.CanRead)]
public class NotificationsController : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit     = 200;

    private readonly INotificationRepository _notifications;
    private readonly IRbacService _rbac;

    public NotificationsController(INotificationRepository notifications, IRbacService rbac)
    {
        _notifications = notifications;
        _rbac          = rbac;
    }

    /// <summary>Newest-first inbox for the current user, with the unread count for the badge.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool unreadOnly = false, [FromQuery] int limit = DefaultLimit)
    {
        var userId = _rbac.GetUserId(User);
        if (userId is null) return Unauthorized();

        limit = Math.Clamp(limit, 1, MaxLimit);
        var items  = await _notifications.ListForUserAsync(userId.Value, unreadOnly, limit);
        var unread = await _notifications.UnreadCountAsync(userId.Value);

        return Ok(new NotificationListResponse(
            items.Select(NotificationItem.From).ToList(),
            unread));
    }

    /// <summary>Unread count only — for the bell badge poll.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(NotificationUnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = _rbac.GetUserId(User);
        if (userId is null) return Unauthorized();

        return Ok(new NotificationUnreadCountResponse(await _notifications.UnreadCountAsync(userId.Value)));
    }

    /// <summary>Mark the listed notifications read. Ids that are not the caller's are ignored.</summary>
    [HttpPost("read")]
    [ProducesResponseType(typeof(NotificationMarkReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MarkRead([FromBody] NotificationMarkReadRequest? request)
    {
        var userId = _rbac.GetUserId(User);
        if (userId is null) return Unauthorized();

        if (request?.Ids is null || request.Ids.Count == 0)
            return BadRequest(new { error = "ids must contain at least one notification id." });

        var updated = await _notifications.MarkReadAsync(userId.Value, request.Ids);
        return Ok(new NotificationMarkReadResponse(updated));
    }

    /// <summary>Mark every unread notification read.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(typeof(NotificationMarkReadResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _rbac.GetUserId(User);
        if (userId is null) return Unauthorized();

        var updated = await _notifications.MarkAllReadAsync(userId.Value);
        return Ok(new NotificationMarkReadResponse(updated));
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record NotificationItem(
    long      Id,
    string    EventType,
    long      ContentEntryId,
    string    ContentTitle,
    string    Message,
    long?     ActorId,
    string?   ActorDisplayName,
    string?   Comment,
    bool      IsRead,
    DateTime? ReadAt,
    DateTime  CreatedAt,
    string?   EntrySlug,
    string?   EntryStatus)
{
    public static NotificationItem From(Notification n) => new(
        n.Id, n.EventType, n.ContentEntryId, n.ContentTitle, n.Message,
        n.ActorId, n.ActorDisplayName, n.Comment, n.IsRead, n.ReadAt, n.CreatedAt,
        n.EntrySlug, n.EntryStatus);
}

public sealed record NotificationListResponse(IReadOnlyList<NotificationItem> Items, int UnreadCount);
public sealed record NotificationUnreadCountResponse(int UnreadCount);
public sealed record NotificationMarkReadRequest(List<long>? Ids);
public sealed record NotificationMarkReadResponse(int Updated);
