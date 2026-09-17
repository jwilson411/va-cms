using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// SMTP delivery via MailKit (issue #39, BRD FR-WORKFLOW-02). One connection per batch:
/// connect, STARTTLS/TLS per <see cref="SmtpOptions.Security"/>, AUTH when credentials are
/// configured, one message per recipient, QUIT.
///
/// Works against Exchange Online (smtp.office365.com:587, STARTTLS, SMTP AUTH) and on-prem
/// Exchange (a receive connector on 587 with STARTTLS, or an IP-allow-listed relay on 25).
/// A rejected recipient is logged and skipped so one bad address never blocks the rest.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger  = logger;
    }

    public async Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        var smtp = _options.Smtp;
        using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };

        await client.ConnectAsync(smtp.Host, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    await client.SendAsync(Build(message), cancellationToken);
                    _logger.LogInformation("Email sent to {To}: {Subject}", message.ToAddress, message.Subject);
                }
                catch (SmtpCommandException ex)
                {
                    // Recipient / message-level rejection (bad address, mailbox full, size limit):
                    // the connection is still good, carry on with the next recipient.
                    _logger.LogError(ex, "SMTP rejected email to {To} ({Subject}): {StatusCode} {Message}",
                        message.ToAddress, message.Subject, ex.StatusCode, ex.Message);
                }
            }
        }
        finally
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }

    private MimeMessage Build(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.From));
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
