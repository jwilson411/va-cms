using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Infrastructure.Services;

/// <summary>
/// Background worker that polls every 60 seconds and:
///   - Publishes Approved entries whose ScheduledPublishAt has passed.
///   - Unpublishes (expires) Published entries whose ScheduledExpireAt has passed.
///
/// Issue #35: BRD FR-AUTH-04. Acceptance criteria:
///   - Background job publishes the entry within 2 minutes of the scheduled time.
///   - Background job unpublishes the entry within 2 minutes of the expiry time.
///
/// Uses a system actor ID of 0 (indicating automated action) for audit log entries.
/// </summary>
public sealed class ScheduledPublishWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    // System actor ID 0 is used for automated scheduler actions in the audit log.
    private const long SystemActorId = 0;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledPublishWorker> _logger;

    public ScheduledPublishWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledPublishWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ScheduledPublishWorker started. Polling every {Interval}s.",
            PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — exit loop cleanly.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScheduledPublishWorker sweep failed; will retry in {Interval}s.",
                    PollInterval.TotalSeconds);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ScheduledPublishWorker stopped.");
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        // IContentEntryRepository is Scoped — create a fresh scope per sweep.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IContentEntryRepository>();

        // ── Publish due entries ──────────────────────────────────────────────
        var toPublish = await repo.GetScheduledForPublishAsync();
        if (toPublish.Count > 0)
        {
            _logger.LogInformation(
                "ScheduledPublishWorker: publishing {Count} scheduled entries.", toPublish.Count);

            foreach (var entry in toPublish)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await repo.PublishScheduledAsync(entry.Id, SystemActorId);
                    _logger.LogInformation(
                        "Scheduled publish: entry {EntryId} (slug={Slug}) published.", entry.Id, entry.Slug);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Scheduled publish: failed to publish entry {EntryId}.", entry.Id);
                }
            }
        }

        // ── Expire due entries ───────────────────────────────────────────────
        var toExpire = await repo.GetScheduledForExpiryAsync();
        if (toExpire.Count > 0)
        {
            _logger.LogInformation(
                "ScheduledPublishWorker: expiring {Count} scheduled entries.", toExpire.Count);

            foreach (var entry in toExpire)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await repo.ExpireScheduledAsync(entry.Id, SystemActorId);
                    _logger.LogInformation(
                        "Scheduled expiry: entry {EntryId} (slug={Slug}) unpublished.", entry.Id, entry.Slug);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Scheduled expiry: failed to expire entry {EntryId}.", entry.Id);
                }
            }
        }
    }
}
