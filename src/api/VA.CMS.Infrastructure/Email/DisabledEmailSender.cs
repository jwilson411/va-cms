using Microsoft.Extensions.Logging;

namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// Stand-in used when Email:Smtp:Host is not configured: every message is logged at
/// Information level instead of sent, so local development sees what would have gone out.
/// Issue #39.
/// </summary>
public sealed class DisabledEmailSender : IEmailSender
{
    private readonly ILogger<DisabledEmailSender> _logger;

    public DisabledEmailSender(ILogger<DisabledEmailSender> logger) => _logger = logger;

    public Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
            _logger.LogInformation("Email not sent (Email:Smtp:Host is not configured) to {To}: {Subject}",
                message.ToAddress, message.Subject);
        return Task.CompletedTask;
    }
}
