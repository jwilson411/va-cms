using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Notifications;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Services;

/// <summary>
/// Background worker that polls every workflow.scheduledPublishPollSeconds (default 60) and:
///   - Publishes Approved entries whose ScheduledPublishAt has passed.
///   - Unpublishes (expires) Published entries whose ScheduledExpireAt has passed.
///
/// Issue #35: BRD FR-AUTH-04. Acceptance criteria:
///   - Background job publishes the entry within 2 minutes of the scheduled time.
///   - Background job unpublishes the entry within 2 minutes of the expiry time.
///
/// Uses a system actor ID of 0 (indicating automated action) for audit log entries.
///
/// The poll interval and the features.scheduledPublishing switch are site settings
/// (issue #144/#147) read on every loop, so an admin change applies without a restart.
/// Issue #39: a scheduled publish notifies the entry owner (in-app + email) like a manual one.
///
/// Issue #171 (NFR-OPS-04): this worker runs on every API node. A sweep is one call to
/// usp_ContentEntry_ClaimScheduledForPublish / _ClaimScheduledForExpiry, which transitions the
/// due rows it can lock (UPDLOCK, READPAST), audits them and queues their content.published /
/// content.unpublished webhook rows in one transaction, and returns only those rows. Two nodes
/// sweeping at the same instant therefore split the due entries between them; nothing is
/// published or notified twice. <see cref="SweepOnceAsync"/> is that unit of work.
/// </summary>
public sealed class ScheduledPublishWorker : BackgroundService
{
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(5);

    // System actor ID 0 is used for automated scheduler actions in the audit log.
    private const long SystemActorId = 0;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledPublishWorker> _logger;
    private readonly ISiteSettingsService _settings;

    public ScheduledPublishWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledPublishWorker> logger,
        ISiteSettingsService settings)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _settings     = settings;
    }

    private TimeSpan PollInterval
    {
        get
        {
            var configured = TimeSpan.FromSeconds(_settings.GetInt(SiteSettingKeys.WorkflowScheduledPublishPollSeconds));
            return configured < MinPollInterval ? MinPollInterval : configured;
        }
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
                if (_settings.GetBool(SiteSettingKeys.FeatureScheduledPublishing))
                    await RunSweepAsync(stoppingToken);
                else
                    _logger.LogDebug("ScheduledPublishWorker: features.scheduledPublishing is off; sweep skipped.");
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

    /// <summary>One sweep: claim and publish every due entry, then claim and expire. Public for tests.</summary>
    public Task SweepOnceAsync(CancellationToken ct = default) => RunSweepAsync(ct);

    private async Task RunSweepAsync(CancellationToken ct)
    {
        // IContentEntryRepository is Scoped — create a fresh scope per sweep.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo     = scope.ServiceProvider.GetRequiredService<IContentEntryRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<IWorkflowNotifier>();

        // ── Publish due entries ──────────────────────────────────────────────
        // The claim SP has already transitioned, audited and queued the webhook for each row
        // it returns; what remains is the owner notification (in-app row + outbox email).
        var published = await repo.ClaimScheduledForPublishAsync();
        if (published.Count > 0)
        {
            _logger.LogInformation(
                "ScheduledPublishWorker: published {Count} scheduled entries.", published.Count);

            foreach (var entry in published)
            {
                ct.ThrowIfCancellationRequested();
                _logger.LogInformation(
                    "Scheduled publish: entry {EntryId} (slug={Slug}) published.", entry.Id, entry.Slug);
                try
                {
                    await notifier.NotifyAsync(entry.Id, NotificationEventTypes.ContentPublished, SystemActorId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Scheduled publish: entry {EntryId} published but its notification failed.", entry.Id);
                }
            }
        }

        // ── Expire due entries ───────────────────────────────────────────────
        var expired = await repo.ClaimScheduledForExpiryAsync();
        if (expired.Count > 0)
        {
            _logger.LogInformation(
                "ScheduledPublishWorker: expired {Count} scheduled entries.", expired.Count);

            foreach (var entry in expired)
                _logger.LogInformation(
                    "Scheduled expiry: entry {EntryId} (slug={Slug}) unpublished.", entry.Id, entry.Slug);
        }
    }
}
