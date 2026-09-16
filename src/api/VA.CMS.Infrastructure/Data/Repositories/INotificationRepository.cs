using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the in-app notification center.
/// Issue #38 — BRD FR-WORKFLOW-02 / FR-WORKFLOW-03.
/// All methods call EXEC usp_Notification_* stored procedures (V039).
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Fan one workflow event out to its recipients (see V039 for the recipient rules).
    /// Returns the number of notifications created — 0 when nobody but the actor qualifies.
    /// Calls usp_Notification_CreateForWorkflowEvent.
    /// </summary>
    Task<int> CreateForWorkflowEventAsync(long contentEntryId, string eventType, long actorId, string? comment = null);

    /// <summary>Newest-first inbox for <paramref name="userId"/>. Calls usp_Notification_ListForUser.</summary>
    Task<IReadOnlyList<Notification>> ListForUserAsync(long userId, bool unreadOnly = false, int limit = 50);

    /// <summary>Unread count for the bell badge. Calls usp_Notification_UnreadCount.</summary>
    Task<int> UnreadCountAsync(long userId);

    /// <summary>
    /// Mark the given notifications read. Rows that belong to another user are ignored.
    /// Returns the number of rows updated. Calls usp_Notification_MarkRead.
    /// </summary>
    Task<int> MarkReadAsync(long userId, IReadOnlyCollection<long> ids);

    /// <summary>Mark every unread notification read. Returns the number updated. Calls usp_Notification_MarkAllRead.</summary>
    Task<int> MarkAllReadAsync(long userId);
}
