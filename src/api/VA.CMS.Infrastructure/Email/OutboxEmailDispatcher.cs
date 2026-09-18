using System.Text.Json;
using Microsoft.Extensions.Logging;
using VA.CMS.Infrastructure.Outbox;

namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// Front door for workflow email (issue #39). Since #171 it is durable: the messages are
/// written to the transactional outbox on the caller's connection and delivered by
/// <see cref="OutboxDispatcherWorker"/> through <see cref="OutboxEmailConsumer"/> on any node,
/// so a slow relay never stalls the request and a recycle never loses a message. The
/// workflow transition has already been committed when this is called; an outbox write
/// failure is logged, not surfaced — a broken queue must not turn a successful transition
/// into a 500.
/// </summary>
public interface IEmailDispatcher
{
    Task EnqueueAsync(IReadOnlyList<EmailMessage> messages, CancellationToken ct = default);
}

public sealed class OutboxEmailDispatcher : IEmailDispatcher
{
    private readonly IOutboxRepository _outbox;
    private readonly ILogger<OutboxEmailDispatcher> _logger;

    public OutboxEmailDispatcher(IOutboxRepository outbox, ILogger<OutboxEmailDispatcher> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public async Task EnqueueAsync(IReadOnlyList<EmailMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;
        try
        {
            await _outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, JsonSerializer.Serialize(messages), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not queue {Count} email(s) ({Subject}) to the outbox.",
                messages.Count, messages[0].Subject);
        }
    }
}
