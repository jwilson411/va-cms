using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Notifications;

/// <summary>
/// Records in-app notifications for workflow transitions (issue #38, BRD FR-WORKFLOW-02/03):
///   submit-review → reviewers of the entry's section
///   approve        → the entry's author
///   return         → the entry's author (with the reviewer's comment)
///
/// Recipient resolution lives in usp_Notification_CreateForWorkflowEvent. A failure here
/// is logged and swallowed: the transition has already been committed and a broken
/// inbox must never turn a successful workflow action into a 500.
/// </summary>
public interface IWorkflowNotifier
{
    Task NotifyAsync(long contentEntryId, string eventType, long actorId, string? comment = null);
}

public sealed class WorkflowNotifier : IWorkflowNotifier
{
    private readonly INotificationRepository _notifications;
    private readonly ILogger<WorkflowNotifier> _logger;

    public WorkflowNotifier(INotificationRepository notifications, ILogger<WorkflowNotifier> logger)
    {
        _notifications = notifications;
        _logger        = logger;
    }

    public async Task NotifyAsync(long contentEntryId, string eventType, long actorId, string? comment = null)
    {
        try
        {
            var count = await _notifications.CreateForWorkflowEventAsync(contentEntryId, eventType, actorId, comment);
            _logger.LogDebug("Notification {EventType} for entry {EntryId}: {Count} recipient(s).",
                eventType, contentEntryId, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record {EventType} notification for entry {EntryId}.",
                eventType, contentEntryId);
        }
    }
}
