using Microsoft.Extensions.Logging;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Email;

namespace VA.CMS.Infrastructure.Notifications;

/// <summary>
/// Records in-app notifications and sends the matching emails for workflow transitions
/// (issues #38 / #39, BRD FR-WORKFLOW-02/03):
///   submit-review → reviewers of the entry's section
///   approve        → the entry's author
///   return         → the entry's author (with the reviewer's comment)
///   publish        → the entry's author (direct, approved or scheduled)
///
/// Recipient resolution lives in usp_Notification_CreateForWorkflowEvent; the rows it
/// returns carry each recipient's address, so email goes to exactly the people whose inbox
/// got the row. Emails are handed to <see cref="IEmailDispatcher"/> and delivered off the
/// request thread. A failure here is logged and swallowed: the transition has already been
/// committed and a broken inbox or mail relay must never turn a successful workflow action
/// into a 500.
/// </summary>
public interface IWorkflowNotifier
{
    Task NotifyAsync(long contentEntryId, string eventType, long actorId, string? comment = null);
}

public sealed class WorkflowNotifier : IWorkflowNotifier
{
    private readonly INotificationRepository _notifications;
    private readonly IEmailDispatcher _email;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<WorkflowNotifier> _logger;

    public WorkflowNotifier(
        INotificationRepository notifications,
        IEmailDispatcher email,
        EmailOptions emailOptions,
        ILogger<WorkflowNotifier> logger)
    {
        _notifications = notifications;
        _email         = email;
        _emailOptions  = emailOptions;
        _logger        = logger;
    }

    public async Task NotifyAsync(long contentEntryId, string eventType, long actorId, string? comment = null)
    {
        IReadOnlyList<NotificationRecipient> recipients;
        try
        {
            recipients = await _notifications.CreateForWorkflowEventAsync(contentEntryId, eventType, actorId, comment);
            _logger.LogDebug("Notification {EventType} for entry {EntryId}: {Count} recipient(s).",
                eventType, contentEntryId, recipients.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record {EventType} notification for entry {EntryId}.",
                eventType, contentEntryId);
            return;
        }

        if (recipients.Count == 0) return;

        try
        {
            var messages = new List<EmailMessage>(recipients.Count);
            foreach (var recipient in recipients)
            {
                var message = WorkflowEmailComposer.Compose(recipient, _emailOptions);
                if (message is null)
                {
                    _logger.LogWarning("No email for {EventType} on entry {EntryId}: user {UserId} has no address.",
                        eventType, contentEntryId, recipient.RecipientUserId);
                    continue;
                }
                messages.Add(message);
            }

            _email.Enqueue(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue {EventType} emails for entry {EntryId}.",
                eventType, contentEntryId);
        }
    }
}
