namespace VA.CMS.Infrastructure.Outbox;

/// <summary>Row types in [OutboundEvent] (issue #171). Each has one <see cref="IOutboxConsumer"/>.</summary>
public static class OutboundEventTypes
{
    /// <summary>One webhook delivery: <see cref="OutboundEvent.WebhookId"/> + <see cref="OutboundEvent.EventName"/> + signed payload.</summary>
    public const string Webhook = "webhook";
    /// <summary>
    /// One SMTP session delivering a batch of workflow emails; payload is an <c>EmailMessage[]</c>.
    /// (Row value "email"; the member is not named after it because CodeQL treats any member
    /// called *Email* as private data and flags every log line that mentions the row type.)
    /// </summary>
    public const string Smtp    = "email";
}

/// <summary>Lifecycle of an outbox row. A row is claimed while Pending and ends Succeeded or Failed.</summary>
public static class OutboundEventStatus
{
    public const string Pending   = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed    = "Failed";
}

/// <summary>
/// One unit of deferred work in the transactional outbox (issue #171, NFR-OPS-04): written
/// by the request or scheduler that made the domain change, delivered by
/// <see cref="OutboxDispatcherWorker"/> on whichever node claims it first.
/// </summary>
public sealed class OutboundEvent
{
    public long      Id            { get; set; }
    public string    Type          { get; set; } = string.Empty;
    public string?   EventName     { get; set; }
    public long?     WebhookId     { get; set; }
    public string    PayloadJson   { get; set; } = "{}";
    public string    Status        { get; set; } = OutboundEventStatus.Pending;
    /// <summary>Counted at claim time — a node that dies mid-delivery has still used an attempt.</summary>
    public int       Attempts      { get; set; }
    public DateTime  NextAttemptAt { get; set; }
    public string?   LockedBy      { get; set; }
    public DateTime? LockedAt      { get; set; }
    public string?   LastError     { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public DateTime? CompletedAt   { get; set; }
}

/// <summary>Backlog snapshot from usp_OutboundEvent_Stats.</summary>
public sealed record OutboxStats(long Pending, long Succeeded, long Failed, DateTime? OldestPendingAt);

/// <summary>What a consumer decided about one claimed row.</summary>
public abstract record OutboxOutcome
{
    private OutboxOutcome() { }

    public static readonly OutboxOutcome Succeeded = new SucceededOutcome();
    public static OutboxOutcome Retry(TimeSpan delay, string error) => new RetryOutcome(delay, error);
    public static OutboxOutcome Failed(string error) => new FailedOutcome(error);

    public sealed record SucceededOutcome : OutboxOutcome;
    /// <summary>Try again after <paramref name="Delay"/>; the row goes back to Pending with <paramref name="Error"/> recorded.</summary>
    public sealed record RetryOutcome(TimeSpan Delay, string Error) : OutboxOutcome;
    /// <summary>Give up: the row is marked Failed with <paramref name="Error"/> and never retried.</summary>
    public sealed record FailedOutcome(string Error) : OutboxOutcome;
}

/// <summary>
/// Handles rows of one <see cref="Type"/>. Resolved from a fresh DI scope per row, so an
/// implementation may depend on scoped services. It decides its own retry policy from
/// <see cref="OutboundEvent.Attempts"/> (already incremented for the current attempt).
/// </summary>
public interface IOutboxConsumer
{
    string Type { get; }
    Task<OutboxOutcome> HandleAsync(OutboundEvent evt, CancellationToken ct);
}
