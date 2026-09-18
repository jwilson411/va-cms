using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Outbox;

/// <summary>
/// Delivers the transactional outbox (issue #171, NFR-OPS-04). Runs on every API node:
/// each poll claims a batch of due [OutboundEvent] rows for this node (usp_OutboundEvent_Claim,
/// UPDLOCK/READPAST — no two nodes take the same row), hands each row to the
/// <see cref="IOutboxConsumer"/> registered for its type in its own DI scope, and records
/// the outcome. A row a node claimed and never finished (recycle, crash) is claimable again
/// once its lease expires. Every knob is a site setting read per poll (outbox.*).
///
/// <see cref="RunOnceAsync"/> is the unit of work the loop repeats; tests call it directly.
/// </summary>
public sealed class OutboxDispatcherWorker : BackgroundService
{
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PurgeInterval   = TimeSpan.FromHours(1);

    private readonly IOutboxRepository _outbox;
    private readonly IServiceScopeFactory _scopes;
    private readonly ISiteSettingsService _settings;
    private readonly ILogger<OutboxDispatcherWorker> _logger;
    private DateTime _lastPurgeUtc = DateTime.MinValue;

    /// <summary>machine:pid:instance — recorded on every row this worker claims.</summary>
    public string LockedBy { get; }

    public OutboxDispatcherWorker(
        IOutboxRepository outbox,
        IServiceScopeFactory scopes,
        ISiteSettingsService settings,
        ILogger<OutboxDispatcherWorker> logger,
        string? instanceId = null)
    {
        _outbox   = outbox;
        _scopes   = scopes;
        _settings = settings;
        _logger   = logger;
        LockedBy  = Truncate($"{Environment.MachineName}:{Environment.ProcessId}:{instanceId ?? Guid.NewGuid().ToString("N")[..8]}", 100);
    }

    private TimeSpan PollInterval
    {
        get
        {
            var configured = TimeSpan.FromSeconds(_settings.GetInt(SiteSettingKeys.OutboxPollSeconds));
            return configured < MinPollInterval ? MinPollInterval : configured;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcherWorker started as {LockedBy}; polling every {Interval}s.",
            LockedBy, PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await RunOnceAsync(stoppingToken);
                await MaybePurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox poll failed; will retry in {Interval}s.", PollInterval.TotalSeconds);
            }

            // A full batch means there is more waiting: go straight back for it.
            if (processed >= BatchSize) continue;

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("OutboxDispatcherWorker {LockedBy} stopped.", LockedBy);
    }

    private int BatchSize     => Math.Clamp(_settings.GetInt(SiteSettingKeys.OutboxBatchSize), 1, 500);
    private int LeaseSeconds  => Math.Max(10, _settings.GetInt(SiteSettingKeys.OutboxLeaseSeconds));
    private int RetentionDays => Math.Max(1, _settings.GetInt(SiteSettingKeys.OutboxRetentionDays));

    /// <summary>Claim one batch and process it; returns how many rows were claimed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var batch = await _outbox.ClaimAsync(LockedBy, BatchSize, LeaseSeconds, ct);
        if (batch.Count == 0) return 0;

        _logger.LogDebug("Outbox {LockedBy} claimed {Count} row(s).", LockedBy, batch.Count);
        await Task.WhenAll(batch.Select(evt => ProcessAsync(evt, ct)));
        return batch.Count;
    }

    private async Task ProcessAsync(OutboundEvent evt, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        OutboxOutcome outcome;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var consumer = scope.ServiceProvider.GetServices<IOutboxConsumer>()
                .FirstOrDefault(c => string.Equals(c.Type, evt.Type, StringComparison.OrdinalIgnoreCase));
            outcome = consumer is null
                ? OutboxOutcome.Failed($"No consumer registered for outbox type '{evt.Type}'.")
                : await consumer.HandleAsync(evt, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: leave the row locked; the lease expiry hands it to another node.
            throw;
        }
        catch (Exception ex)
        {
            // A consumer bug must not poison the row forever: back off and let it retry, giving
            // up after the generic ceiling.
            _logger.LogError(ex, "Outbox row {Id} ({Type}) threw on attempt {Attempt}.", evt.Id, evt.Type, evt.Attempts);
            outcome = evt.Attempts >= 5
                ? OutboxOutcome.Failed(ex.Message)
                : OutboxOutcome.Retry(TimeSpan.FromSeconds(30 * evt.Attempts), ex.Message);
        }

        switch (outcome)
        {
            case OutboxOutcome.SucceededOutcome:
                await _outbox.CompleteAsync(evt.Id, LockedBy, ct);
                _logger.LogInformation("Outbox row {Id} ({Type}{Event}) delivered on attempt {Attempt} in {Ms} ms.",
                    evt.Id, evt.Type, evt.EventName is null ? "" : " " + evt.EventName, evt.Attempts, sw.ElapsedMilliseconds);
                break;

            case OutboxOutcome.RetryOutcome retry:
                await _outbox.RescheduleAsync(evt.Id, LockedBy, DateTime.UtcNow + retry.Delay, retry.Error, ct);
                _logger.LogWarning("Outbox row {Id} ({Type}) attempt {Attempt} failed: {Error}; retry in {Delay}s.",
                    evt.Id, evt.Type, evt.Attempts, retry.Error, retry.Delay.TotalSeconds);
                break;

            case OutboxOutcome.FailedOutcome failed:
                await _outbox.FailAsync(evt.Id, LockedBy, failed.Error, ct);
                _logger.LogError("Outbox row {Id} ({Type}) gave up after {Attempt} attempt(s): {Error}",
                    evt.Id, evt.Type, evt.Attempts, failed.Error);
                break;
        }
    }

    private async Task MaybePurgeAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastPurgeUtc < PurgeInterval) return;
        _lastPurgeUtc = DateTime.UtcNow;
        var deleted = await _outbox.PurgeAsync(RetentionDays, ct);
        if (deleted > 0)
            _logger.LogInformation("Outbox purge removed {Count} completed row(s) older than {Days} day(s).", deleted, RetentionDays);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
