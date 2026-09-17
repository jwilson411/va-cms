using System.Net;
using System.Text;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Email;

namespace VA.CMS.Infrastructure.Notifications;

/// <summary>
/// Turns one workflow notification into the email its recipient gets (issue #39,
/// BRD FR-WORKFLOW-02). Plain-language subject and body, the same description the
/// in-app inbox shows, what the recipient should do next, and a link to the content.
///
/// Both a text/plain and a text/html part are produced; the text part is the source of
/// truth and reads fine on its own.
/// </summary>
public static class WorkflowEmailComposer
{
    /// <summary>Path the admin SPA serves the entry editor at — matches NotificationBell.entryLink.</summary>
    public static string EntryPath(long contentEntryId) => $"/admin/content/{contentEntryId}/edit";

    /// <summary>Absolute link to the entry editor, or just the path when no AdminBaseUrl is configured.</summary>
    public static string EntryLink(EmailOptions options, long contentEntryId)
    {
        var path = EntryPath(contentEntryId);
        return string.IsNullOrWhiteSpace(options.AdminBaseUrl)
            ? path
            : options.AdminBaseUrl.TrimEnd('/') + path;
    }

    /// <summary>
    /// Compose the email for <paramref name="recipient"/>, or null when they have no address
    /// or the event type is unknown.
    /// </summary>
    public static EmailMessage? Compose(NotificationRecipient recipient, EmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(recipient.RecipientEmail)) return null;

        var (subject, nextStep, footer) = recipient.EventType switch
        {
            NotificationEventTypes.ReviewRequested => (
                $"Review requested: {recipient.ContentTitle}",
                "It's waiting for your review. Open it in the CMS to approve it, or return it to the author with a comment:",
                "You're receiving this because you can review content in this section."),
            NotificationEventTypes.ContentReturned => (
                $"Returned to draft: {recipient.ContentTitle}",
                "Make the requested changes and submit it for review again:",
                "You're receiving this because you own this content."),
            NotificationEventTypes.ContentApproved => (
                $"Approved: {recipient.ContentTitle}",
                "It's approved and ready to publish. Open it in the CMS:",
                "You're receiving this because you own this content."),
            NotificationEventTypes.ContentPublished => (
                $"Published: {recipient.ContentTitle}",
                "It's now live on the site. View or update it in the CMS:",
                "You're receiving this because you own this content."),
            _ => (null, null, null),
        };
        if (subject is null) return null;

        var link    = EntryLink(options, recipient.ContentEntryId);
        var name    = string.IsNullOrWhiteSpace(recipient.RecipientDisplayName) ? "there" : recipient.RecipientDisplayName;
        var comment = string.IsNullOrWhiteSpace(recipient.Comment) ? null : recipient.Comment.Trim();

        return new EmailMessage(
            ToAddress: recipient.RecipientEmail,
            ToName:    recipient.RecipientDisplayName,
            Subject:   subject,
            TextBody:  TextBody(name, recipient.Message, comment, nextStep!, link, footer!),
            HtmlBody:  HtmlBody(name, recipient.Message, comment, nextStep!, link, footer!));
    }

    private static string TextBody(string name, string message, string? comment, string nextStep, string link, string footer)
    {
        var sb = new StringBuilder();
        sb.Append("Hi ").Append(name).Append(",\n\n");
        sb.Append(message).Append(".\n\n");
        if (comment is not null)
            sb.Append("Reviewer's comment:\n").Append(comment).Append("\n\n");
        sb.Append(nextStep).Append('\n').Append(link).Append("\n\n");
        sb.Append("-- \nVA CMS. ").Append(footer).Append('\n');
        return sb.ToString();
    }

    private static string HtmlBody(string name, string message, string? comment, string nextStep, string link, string footer)
    {
        static string H(string s) => WebUtility.HtmlEncode(s);

        var sb = new StringBuilder();
        sb.Append("<p>Hi ").Append(H(name)).Append(",</p>");
        sb.Append("<p>").Append(H(message)).Append(".</p>");
        if (comment is not null)
            sb.Append("<p><strong>Reviewer's comment:</strong></p><blockquote>")
              .Append(H(comment).Replace("\n", "<br>")).Append("</blockquote>");
        sb.Append("<p>").Append(H(nextStep)).Append("<br><a href=\"").Append(H(link)).Append("\">")
          .Append(H(link)).Append("</a></p>");
        sb.Append("<p style=\"color:#71767a;font-size:smaller\">VA CMS. ").Append(H(footer)).Append("</p>");
        return sb.ToString();
    }
}
