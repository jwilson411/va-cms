using VA.CMS.Infrastructure.Outbox;

namespace VA.CMS.Tests;

/// <summary>
/// Single-process stand-in for [OutboundEvent] (#171): same claim/complete/reschedule/fail
/// semantics as usp_OutboundEvent_*, without SQL Server. Fan-out for webhook rows is the
/// caller's job here (<see cref="Subscribers"/>: webhook ids subscribed to every event).
/// </summary>
internal sealed class InMemoryOutboxRepository : IOutboxRepository
{
    private readonly object _gate = new();
    private long _nextId = 1;

    public List<OutboundEvent> Rows { get; } = [];
    public List<long> Subscribers { get; } = [];
    public bool ThrowOnEnqueue { get; init; }

    public Task<int> EnqueueAsync(string type, string? eventName, string payloadJson, CancellationToken ct = default)
    {
        if (ThrowOnEnqueue) throw new InvalidOperationException("outbox unavailable");
        lock (_gate)
        {
            if (type == OutboundEventTypes.Webhook)
            {
                foreach (var id in Subscribers) Add(type, eventName, id, payloadJson);
                return Task.FromResult(Subscribers.Count);
            }
            Add(type, eventName, null, payloadJson);
            return Task.FromResult(1);
        }
    }

    private void Add(string type, string? eventName, long? webhookId, string payloadJson) =>
        Rows.Add(new OutboundEvent
        {
            Id = _nextId++, Type = type, EventName = eventName, WebhookId = webhookId, PayloadJson = payloadJson,
            NextAttemptAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        });

    public Task<IReadOnlyList<OutboundEvent>> ClaimAsync(string lockedBy, int batchSize, int leaseSeconds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var due = Rows
                .Where(r => r.Status == OutboundEventStatus.Pending && r.NextAttemptAt <= now
                            && (r.LockedAt is null || r.LockedAt <= now.AddSeconds(-leaseSeconds)))
                .OrderBy(r => r.NextAttemptAt).ThenBy(r => r.Id)
                .Take(batchSize)
                .ToList();
            foreach (var r in due)
            {
                r.LockedBy = lockedBy; r.LockedAt = now; r.Attempts++;
            }
            // Copies: the worker mutates nothing, but a test may inspect Rows while a batch is in flight.
            return Task.FromResult<IReadOnlyList<OutboundEvent>>(due.Select(Clone).ToList());
        }
    }

    public Task CompleteAsync(long id, string lockedBy, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = Owned(id, lockedBy);
            if (r is null) return Task.CompletedTask;
            r.Status = OutboundEventStatus.Succeeded; r.CompletedAt = DateTime.UtcNow; r.LastError = null; r.LockedBy = null; r.LockedAt = null;
        }
        return Task.CompletedTask;
    }

    public Task RescheduleAsync(long id, string lockedBy, DateTime nextAttemptAtUtc, string? lastError, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = Owned(id, lockedBy);
            if (r is null) return Task.CompletedTask;
            r.NextAttemptAt = nextAttemptAtUtc; r.LastError = lastError; r.LockedBy = null; r.LockedAt = null;
        }
        return Task.CompletedTask;
    }

    public Task FailAsync(long id, string lockedBy, string? lastError, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = Owned(id, lockedBy);
            if (r is null) return Task.CompletedTask;
            r.Status = OutboundEventStatus.Failed; r.CompletedAt = DateTime.UtcNow; r.LastError = lastError; r.LockedBy = null; r.LockedAt = null;
        }
        return Task.CompletedTask;
    }

    public Task<int> PurgeAsync(int olderThanDays, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
            return Task.FromResult(Rows.RemoveAll(r => r.Status != OutboundEventStatus.Pending && r.CompletedAt < cutoff));
        }
    }

    public Task<OutboxStats> GetStatsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new OutboxStats(
                Rows.Count(r => r.Status == OutboundEventStatus.Pending),
                Rows.Count(r => r.Status == OutboundEventStatus.Succeeded),
                Rows.Count(r => r.Status == OutboundEventStatus.Failed),
                Rows.Where(r => r.Status == OutboundEventStatus.Pending).Select(r => (DateTime?)r.NextAttemptAt).Min()));
        }
    }

    private OutboundEvent? Owned(long id, string lockedBy) =>
        Rows.FirstOrDefault(r => r.Id == id && r.Status == OutboundEventStatus.Pending && r.LockedBy == lockedBy);

    private static OutboundEvent Clone(OutboundEvent r) => new()
    {
        Id = r.Id, Type = r.Type, EventName = r.EventName, WebhookId = r.WebhookId, PayloadJson = r.PayloadJson,
        Status = r.Status, Attempts = r.Attempts, NextAttemptAt = r.NextAttemptAt, LockedBy = r.LockedBy,
        LockedAt = r.LockedAt, LastError = r.LastError, CreatedAt = r.CreatedAt, CompletedAt = r.CompletedAt,
    };
}
