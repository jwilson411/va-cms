namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// Delivers a batch of emails over one connection. Issue #39.
/// Implementations: <see cref="SmtpEmailSender"/> (MailKit) and <see cref="DisabledEmailSender"/> (log only).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default);
}
