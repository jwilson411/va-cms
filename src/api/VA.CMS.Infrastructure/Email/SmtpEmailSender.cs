using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using VA.CMS.Infrastructure.Settings;

using VA.CMS.Infrastructure.Logging;

namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// SMTP delivery via MailKit (issue #39, BRD FR-WORKFLOW-02). One connection per batch:
/// connect, STARTTLS/TLS per <see cref="SmtpOptions.Security"/>, AUTH when credentials are
/// configured, one message per recipient, QUIT.
///
/// Works against Exchange Online (smtp.office365.com:587, STARTTLS, SMTP AUTH) and on-prem
/// Exchange (a receive connector on 587 with STARTTLS, or an IP-allow-listed relay on 25).
/// A rejected recipient is logged and skipped so one bad address never blocks the rest.
/// The sender address/name come from the notifications.emailFromAddress / emailFromName site
/// settings, read per batch so an admin change applies to the next email.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ISiteSettingsService _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EmailOptions options, ISiteSettingsService settings, ILogger<SmtpEmailSender> logger)
    {
        _options  = options;
        _settings = settings;
        _logger   = logger;
    }

    public async Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        var fromAddress = _settings.GetString(SiteSettingKeys.NotificationsEmailFromAddress);
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            _logger.LogError("Cannot send {Count} email(s): site setting {Key} is empty.",
                messages.Count, SiteSettingKeys.NotificationsEmailFromAddress);
            return;
        }
        var from = new MailboxAddress(_settings.GetString(SiteSettingKeys.NotificationsEmailFromName), fromAddress);

        var smtp = _options.Smtp;
        using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };

        // EmailOptions.Validate() has already required Host (and Password when Username is set).
        await client.ConnectAsync(smtp.Host!, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    await client.SendAsync(Build(from, message), cancellationToken);
                    _logger.LogInformation("Email sent to {To}: {Subject}", PiiMask.Redact(message.ToAddress), message.Subject);
                }
                catch (SmtpCommandException ex)
                {
                    // Recipient / message-level rejection (bad address, mailbox full, size limit):
                    // the connection is still good, carry on with the next recipient.
                    _logger.LogError(ex, "SMTP rejected email to {To} ({Subject}): {StatusCode} {Message}",
                        PiiMask.Redact(message.ToAddress), message.Subject, ex.StatusCode, ex.Message);
                }
            }
        }
        finally
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }

    private static MimeMessage Build(MailboxAddress from, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(from);
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { TextBody = message.TextBody };
        if (!string.IsNullOrEmpty(message.HtmlBody))
            body.HtmlBody = message.HtmlBody;
        mime.Body = body.ToMessageBody();
        return mime;
    }

    public static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls     => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.Auto         => SecureSocketOptions.Auto,
        SmtpSecurity.None         => SecureSocketOptions.None,
        _                         => SecureSocketOptions.StartTls,
    };
}
