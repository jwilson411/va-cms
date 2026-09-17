using Microsoft.Extensions.Logging;

namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// Fire-and-forget front door for <see cref="IEmailSender"/> (issue #39).
///
/// An SMTP round trip to Exchange takes a second or more and may stall on a dead relay, so
/// it must not run on the request thread — a reviewer's "return" click should not wait on
/// the mail server. Enqueue() sends on the thread pool and logs failures instead of
/// surfacing them; the workflow transition has already been committed by then.
/// Same shape as the webhook background dispatcher.
/// </summary>
public interface IEmailDispatcher
{
    void Enqueue(IReadOnlyList<EmailMessage> messages);
}

public sealed class BackgroundEmailDispatcher : IEmailDispatcher
{
    private readonly IEmailSender _sender;
    private readonly ILogger<BackgroundEmailDispatcher> _logger;

    public BackgroundEmailDispatcher(IEmailSender sender, ILogger<BackgroundEmailDispatcher> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public void Enqueue(IReadOnlyList<EmailMessage> messages)
    {
        if (messages.Count == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _sender.SendAsync(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background email delivery of {Count} message(s) failed ({Subject}).",
                    messages.Count, messages[0].Subject);
            }
        });
    }
}
