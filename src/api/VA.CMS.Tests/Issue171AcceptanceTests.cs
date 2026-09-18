using System.Collections.Concurrent;
using System.Data;
using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using VA.CMS.API.Observability;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Notifications;
using VA.CMS.Infrastructure.Outbox;
using VA.CMS.Infrastructure.Services;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #171 (epic #152) — multi-instance safety (BRD NFR-OPS-04).
///
///   AC1: the scheduler sweep claims rows WITH (UPDLOCK, READPAST) inside one transaction, so
///        only one node wins each row.
///   AC2: [OutboundEvent] outbox written with the domain change; every node claims batches
///        with READPAST; webhooks and emails are its consumers; retry state lives in the row.
///   AC3: a settings write on one node is picked up by the others without waiting for the
///        full refresh (change-stamp poll).
///   AC5: two ScheduledPublishWorker instances against one database publish each due entry
///        exactly once.
///   (AC4, the deployment topologies, is docs/DEPLOYMENT.md.)
/// </summary>
[Collection("Database")]
public class Issue171AcceptanceTests(DatabaseFixture fixture)
{
    private IOutboxRepository Outbox() => new OutboxRepository(fixture.ConnectionString);

    // ── AC2: outbox stored procedures ────────────────────────────────────────

    [Fact]
    public async Task Enqueue_Webhook_FansOutToActiveSubscribersOnly()
    {
        var marker     = Marker();
        var subscribed = await CreateWebhookAsync("[\"content.published\"]");
        var other      = await CreateWebhookAsync("[\"media.uploaded\"]");
        var inactive   = await CreateWebhookAsync("[\"content.published\"]");
        await ExecAsync("UPDATE [Webhook] SET [IsActive] = 0 WHERE [Id] = @Id", ("@Id", inactive));

        var count = await Outbox().EnqueueAsync(OutboundEventTypes.Webhook, "content.published", marker);

        Assert.True(count >= 1);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [WebhookId] = @Id AND [PayloadJson] = @P", ("@Id", subscribed), ("@P", marker)));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [WebhookId] = @Id AND [PayloadJson] = @P", ("@Id", other),      ("@P", marker)));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [WebhookId] = @Id AND [PayloadJson] = @P", ("@Id", inactive),   ("@P", marker)));

        var row = await FirstRowAsync(marker);
        Assert.Equal(OutboundEventStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Equal("content.published", row.EventName);
    }

    [Fact]
    public async Task Enqueue_Email_WritesExactlyOneRow()
    {
        var marker = Marker();
        Assert.Equal(1, await Outbox().EnqueueAsync(OutboundEventTypes.Smtp, null, marker));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [PayloadJson] = @P", ("@P", marker)));
        Assert.Null((await FirstRowAsync(marker)).WebhookId);
    }

    /// <summary>AC2: two nodes claiming at the same time never receive the same row.</summary>
    [Fact]
    public async Task Claim_TwoNodesConcurrently_NeverShareARow()
    {
        var marker = Marker();
        var outbox = Outbox();
        for (var i = 0; i < 40; i++)
            await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, marker + ":" + i);

        var claimedBy = new ConcurrentDictionary<long, string>();
        var duplicate = 0;

        async Task Node(string name)
        {
            while (true)
            {
                var batch = await outbox.ClaimAsync(name, batchSize: 5, leaseSeconds: 300);
                if (batch.Count == 0) return;
                foreach (var row in batch)
                {
                    if (!claimedBy.TryAdd(row.Id, name)) Interlocked.Increment(ref duplicate);
                    await outbox.CompleteAsync(row.Id, name);   // also clears rows other tests left behind
                }
            }
        }

        await Task.WhenAll(Node("node-a"), Node("node-b"));

        Assert.Equal(0, duplicate);
        var mine = await IdsAsync("SELECT [Id] FROM [OutboundEvent] WHERE [PayloadJson] LIKE @P", ("@P", marker + "%"));
        Assert.Equal(40, mine.Count);
        Assert.All(mine, id => Assert.True(claimedBy.ContainsKey(id), $"row {id} was never claimed"));
        Assert.Equal(40, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [PayloadJson] LIKE @P AND [Status] = 'Succeeded' AND [LockedBy] IS NULL", ("@P", marker + "%")));
    }

    /// <summary>AC2: a row a dead node claimed becomes claimable again when its lease runs out.</summary>
    [Fact]
    public async Task Claim_LeaseExpiry_RowOfDeadNodeIsReclaimed_AndCountsAnotherAttempt()
    {
        var marker = Marker();
        var outbox = Outbox();
        await DrainAsync();
        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, marker);

        var first = await outbox.ClaimAsync("node-a", 50, leaseSeconds: 60);
        var row   = Assert.Single(first, r => r.PayloadJson == marker);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("node-a", row.LockedBy);

        // Lease still running: another node does not see it.
        Assert.DoesNotContain(await outbox.ClaimAsync("node-b", 50, leaseSeconds: 60), r => r.Id == row.Id);

        // node-a never came back; move its lock into the past.
        await ExecAsync("UPDATE [OutboundEvent] SET [LockedAt] = DATEADD(SECOND, -120, SYSUTCDATETIME()) WHERE [Id] = @Id", ("@Id", row.Id));

        var again = Assert.Single(await outbox.ClaimAsync("node-b", 50, leaseSeconds: 60), r => r.Id == row.Id);
        Assert.Equal(2, again.Attempts);
        Assert.Equal("node-b", again.LockedBy);

        // The dead node's late result is ignored: it no longer owns the row.
        await outbox.CompleteAsync(row.Id, "node-a");
        Assert.Equal("Pending", await ScalarAsync<string>("SELECT [Status] FROM [OutboundEvent] WHERE [Id] = @Id", ("@Id", row.Id)));
        await outbox.CompleteAsync(row.Id, "node-b");
        Assert.Equal("Succeeded", await ScalarAsync<string>("SELECT [Status] FROM [OutboundEvent] WHERE [Id] = @Id", ("@Id", row.Id)));
    }

    [Fact]
    public async Task Reschedule_And_Fail_KeepRetryStateInTheRow()
    {
        var marker = Marker();
        var outbox = Outbox();
        await DrainAsync();
        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, marker);

        var row = Assert.Single(await outbox.ClaimAsync("n1", 50, 300), r => r.PayloadJson == marker);
        await outbox.RescheduleAsync(row.Id, "n1", DateTime.UtcNow.AddMinutes(5), "relay down");

        // Not due yet: nobody gets it.
        Assert.DoesNotContain(await outbox.ClaimAsync("n2", 50, 300), r => r.Id == row.Id);
        var after = await FirstRowAsync(marker);
        Assert.Equal("relay down", after.LastError);
        Assert.Null(after.LockedBy);
        Assert.Equal(OutboundEventStatus.Pending, after.Status);

        await ExecAsync("UPDATE [OutboundEvent] SET [NextAttemptAt] = SYSUTCDATETIME() WHERE [Id] = @Id", ("@Id", row.Id));
        var second = Assert.Single(await outbox.ClaimAsync("n2", 50, 300), r => r.Id == row.Id);
        Assert.Equal(2, second.Attempts);

        await outbox.FailAsync(row.Id, "n2", "gave up");
        var failed = await FirstRowAsync(marker);
        Assert.Equal(OutboundEventStatus.Failed, failed.Status);
        Assert.Equal("gave up", failed.LastError);
        Assert.NotNull(failed.CompletedAt);
        Assert.DoesNotContain(await outbox.ClaimAsync("n3", 50, 300), r => r.Id == row.Id);
    }

    [Fact]
    public async Task Purge_RemovesOldCompletedRows_NeverPending()
    {
        var marker = Marker();
        var outbox = Outbox();
        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, marker + ":old");
        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, marker + ":pending");
        await ExecAsync("UPDATE [OutboundEvent] SET [Status] = 'Succeeded', [CompletedAt] = DATEADD(DAY, -30, SYSUTCDATETIME()) WHERE [PayloadJson] = @P", ("@P", marker + ":old"));

        var deleted = await outbox.PurgeAsync(olderThanDays: 14);

        Assert.True(deleted >= 1);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [PayloadJson] = @P", ("@P", marker + ":old")));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [PayloadJson] = @P", ("@P", marker + ":pending")));

        var stats = await outbox.GetStatsAsync();
        Assert.True(stats.Pending >= 1);
        Assert.NotNull(stats.OldestPendingAt);
    }

    // ── AC2: dispatcher worker ───────────────────────────────────────────────

    /// <summary>Two worker instances (two nodes) drain one outbox; every row is handled once.</summary>
    [Fact]
    public async Task Worker_TwoInstances_HandleEveryRowExactlyOnce()
    {
        await DrainAsync();
        var marker  = Marker();
        var outbox  = Outbox();
        var handled = new ConcurrentDictionary<long, int>();
        var consumer = new DelegateConsumer(OutboundEventTypes.Smtp, evt =>
        {
            handled.AddOrUpdate(evt.Id, 1, (_, n) => n + 1);
            return Task.FromResult(OutboxOutcome.Succeeded);
        });
        for (var i = 0; i < 30; i++)
            await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, marker + ":" + i);

        var settings = StaticSiteSettings.Defaults.With(SiteSettingKeys.OutboxBatchSize, 4);
        await using var services = new ServiceCollection().AddScoped<IOutboxConsumer>(_ => consumer).BuildServiceProvider();
        var a = Worker(outbox, services, settings, "a");
        var b = Worker(outbox, services, settings, "b");

        async Task Drain(OutboxDispatcherWorker w) { while (await w.RunOnceAsync(CancellationToken.None) > 0) { } }
        await Task.WhenAll(Drain(a), Drain(b));

        var mine = await IdsAsync("SELECT [Id] FROM [OutboundEvent] WHERE [PayloadJson] LIKE @P", ("@P", marker + "%"));
        Assert.Equal(30, mine.Count);
        Assert.All(mine, id => Assert.Equal(1, handled.GetValueOrDefault(id)));
        Assert.Equal(30, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [PayloadJson] LIKE @P AND [Status] = 'Succeeded'", ("@P", marker + "%")));
    }

    [Fact]
    public async Task Worker_AppliesRetryThenFailed_FromConsumerOutcome()
    {
        var outbox = new InMemoryOutboxRepository();
        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, "[]");
        var outcomes = new Queue<OutboxOutcome>([OutboxOutcome.Retry(TimeSpan.Zero, "first"), OutboxOutcome.Failed("second")]);
        var consumer = new DelegateConsumer(OutboundEventTypes.Smtp, _ => Task.FromResult(outcomes.Dequeue()));
        await using var services = new ServiceCollection().AddScoped<IOutboxConsumer>(_ => consumer).BuildServiceProvider();
        var worker = Worker(outbox, services, StaticSiteSettings.Defaults, "x");

        Assert.Equal(1, await worker.RunOnceAsync(CancellationToken.None));
        var row = Assert.Single(outbox.Rows);
        Assert.Equal(OutboundEventStatus.Pending, row.Status);
        Assert.Equal("first", row.LastError);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.LockedBy);

        Assert.Equal(1, await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(OutboundEventStatus.Failed, row.Status);
        Assert.Equal("second", row.LastError);
        Assert.Equal(2, row.Attempts);
        Assert.NotNull(row.CompletedAt);

        Assert.Equal(0, await worker.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Worker_ConsumerThrows_RowIsRetriedNotPoisoned_UnknownTypeFails()
    {
        var outbox = new InMemoryOutboxRepository();
        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, "[]");
        await outbox.EnqueueAsync("telegram", null, "{}");
        var consumer = new DelegateConsumer(OutboundEventTypes.Smtp, _ => throw new InvalidOperationException("boom"));
        await using var services = new ServiceCollection().AddScoped<IOutboxConsumer>(_ => consumer).BuildServiceProvider();
        var worker = Worker(outbox, services, StaticSiteSettings.Defaults, "x");

        Assert.Equal(2, await worker.RunOnceAsync(CancellationToken.None));

        var email = outbox.Rows.Single(r => r.Type == OutboundEventTypes.Smtp);
        Assert.Equal(OutboundEventStatus.Pending, email.Status);
        Assert.Contains("boom", email.LastError);
        Assert.True(email.NextAttemptAt > DateTime.UtcNow.AddSeconds(20));

        var unknown = outbox.Rows.Single(r => r.Type == "telegram");
        Assert.Equal(OutboundEventStatus.Failed, unknown.Status);
        Assert.Contains("No consumer", unknown.LastError);
    }

    // ── AC2: webhook consumer ────────────────────────────────────────────────

    [Fact]
    public async Task WebhookConsumer_DeliversOnce_RetriesOnFailure_GivesUpAtMaxAttempts()
    {
        var status  = HttpStatusCode.InternalServerError;
        var calls   = 0;
        var handler = new FakeHttpHandler(_ => { calls++; return new HttpResponseMessage(status); });
        var repo    = new InMemoryWebhookRepository();
        var id      = await repo.CreateAsync("t", "https://example.com/cb", "sec", "[\"content.published\"]", 1);
        var settings   = StaticSiteSettings.Defaults;
        var dispatcher = new WebhookDispatcher(repo, new FakeHttpClientFactory(handler), NullLogger<WebhookDispatcher>.Instance, settings);
        var consumer   = new OutboxWebhookConsumer(repo, dispatcher, settings);
        var row = new OutboundEvent { Id = 1, Type = OutboundEventTypes.Webhook, EventName = "content.published", WebhookId = id, PayloadJson = "{\"id\":7}", Attempts = 1 };

        var retry = Assert.IsType<OutboxOutcome.RetryOutcome>(await consumer.HandleAsync(row, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(5), retry.Delay);     // webhooks.retryDelaysSeconds[0]
        Assert.Contains("HTTP 500", retry.Error);

        row.Attempts = 2;
        Assert.Equal(TimeSpan.FromSeconds(25), Assert.IsType<OutboxOutcome.RetryOutcome>(await consumer.HandleAsync(row, CancellationToken.None)).Delay);

        row.Attempts = 3;                                         // webhooks.maxAttempts
        Assert.IsType<OutboxOutcome.FailedOutcome>(await consumer.HandleAsync(row, CancellationToken.None));

        status = HttpStatusCode.OK;
        row.Attempts = 1;
        Assert.IsType<OutboxOutcome.SucceededOutcome>(await consumer.HandleAsync(row, CancellationToken.None));

        Assert.Equal(4, calls);
        // Every attempt is a delivery-log row carrying the outbox attempt number.
        Assert.Equal([1, 2, 3, 1], repo.Deliveries.Where(d => d.WebhookId == id).Select(d => d.AttemptNumber).ToArray());
        Assert.Equal("{\"id\":7}", repo.Deliveries[0].PayloadJson);
    }

    [Fact]
    public async Task WebhookConsumer_RefusedOrInactive_IsFinal()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var repo    = new InMemoryWebhookRepository();
        var refused = await repo.CreateAsync("creds", "https://user:pw@example.com/cb", "sec", "[\"content.published\"]", 1);
        var deleted = await repo.CreateAsync("gone",  "https://example.com/cb", "sec", "[\"content.published\"]", 1);
        await repo.DeleteAsync(deleted);
        var settings   = StaticSiteSettings.Defaults;
        var dispatcher = new WebhookDispatcher(repo, new FakeHttpClientFactory(handler), NullLogger<WebhookDispatcher>.Instance, settings);
        var consumer   = new OutboxWebhookConsumer(repo, dispatcher, settings);

        var r1 = Assert.IsType<OutboxOutcome.FailedOutcome>(await consumer.HandleAsync(Row(refused), CancellationToken.None));
        Assert.Contains("Refused", r1.Error);
        Assert.Contains("credentials", r1.Error);
        var r2 = Assert.IsType<OutboxOutcome.FailedOutcome>(await consumer.HandleAsync(Row(deleted), CancellationToken.None));
        Assert.Contains("no longer active", r2.Error);
        Assert.IsType<OutboxOutcome.FailedOutcome>(await consumer.HandleAsync(Row(999), CancellationToken.None));

        // Off switch is honoured at delivery time too.
        var off = new OutboxWebhookConsumer(repo, dispatcher, StaticSiteSettings.Defaults.With(SiteSettingKeys.FeatureWebhooks, false));
        Assert.Contains("features.webhooks", Assert.IsType<OutboxOutcome.FailedOutcome>(await off.HandleAsync(Row(refused), CancellationToken.None)).Error);

        static OutboundEvent Row(long webhookId) => new()
        {
            Id = 1, Type = OutboundEventTypes.Webhook, EventName = "content.published", WebhookId = webhookId, PayloadJson = "{}", Attempts = 1,
        };
    }

    [Fact]
    public async Task OutboxWebhookDispatcher_HonoursFeatureFlag_AndSwallowsOutboxFailure()
    {
        var outbox = new InMemoryOutboxRepository { Subscribers = { 1, 2 } };
        var on     = new OutboxWebhookDispatcher(outbox, NullLogger<OutboxWebhookDispatcher>.Instance, StaticSiteSettings.Defaults);
        await on.EnqueueAsync("content.published", new { id = 1 });
        Assert.Equal(2, outbox.Rows.Count);
        Assert.All(outbox.Rows, r => Assert.Equal("{\"id\":1}", r.PayloadJson));

        var off = new OutboxWebhookDispatcher(outbox, NullLogger<OutboxWebhookDispatcher>.Instance, StaticSiteSettings.Defaults.With(SiteSettingKeys.FeatureWebhooks, false));
        await off.EnqueueAsync("content.published", new { id = 2 });
        Assert.Equal(2, outbox.Rows.Count);

        var broken = new OutboxWebhookDispatcher(new InMemoryOutboxRepository { ThrowOnEnqueue = true }, NullLogger<OutboxWebhookDispatcher>.Instance, StaticSiteSettings.Defaults);
        await broken.EnqueueAsync("content.published", new { id = 3 });   // logged, never thrown at the controller
    }

    // ── AC1 / AC5: scheduler ─────────────────────────────────────────────────

    /// <summary>
    /// AC5: two ScheduledPublishWorker instances sweep one database at the same instant.
    /// Every due entry ends Published with exactly one audit row, one owner notification,
    /// one content.published outbox row per subscriber and one email row — never two.
    /// </summary>
    [Fact]
    public async Task TwoSchedulerWorkers_PublishEachDueEntryExactlyOnce()
    {
        var (ctId, ownerId) = await SeedAsync();
        var webhookId = await CreateWebhookAsync("[\"content.published\"]");
        var entries   = new List<(long Id, string Slug)>();
        for (var i = 0; i < 24; i++)
            entries.Add(await CreateDueForPublishAsync(ctId, ownerId));

        await using var services = SchedulerServices();
        var a = SchedulerWorker(services);
        var b = SchedulerWorker(services);

        await Task.WhenAll(a.SweepOnceAsync(), b.SweepOnceAsync());
        // A later sweep finds nothing left to do.
        await Task.WhenAll(a.SweepOnceAsync(), b.SweepOnceAsync());

        foreach (var (id, slug) in entries)
        {
            Assert.Equal("Published", await ScalarAsync<string>("SELECT [Status] FROM [ContentEntry] WHERE [Id] = @Id", ("@Id", id)));
            Assert.True(await ScalarAsync<object>("SELECT [ScheduledPublishAt] FROM [ContentEntry] WHERE [Id] = @Id", ("@Id", id)) is DBNull);
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [AuditLog] WHERE [EntityType] = 'ContentEntry' AND [EntityId] = @E AND [Action] = 'ScheduledPublish'", ("@E", id.ToString())));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [Notification] WHERE [ContentEntryId] = @Id AND [EventType] = 'ContentPublished'", ("@Id", id)));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [Type] = 'webhook' AND [WebhookId] = @W AND [EventName] = 'content.published' AND JSON_VALUE([PayloadJson], '$.id') = @E", ("@W", webhookId), ("@E", id.ToString())));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [Type] = 'email' AND [PayloadJson] LIKE @S", ("@S", "%" + slug + "%")));
        }
    }

    /// <summary>AC1: a row another sweep holds under UPDLOCK is skipped (READPAST), not waited for or double-published.</summary>
    [Fact]
    public async Task ClaimScheduledForPublish_SkipsRowsLockedByAnotherSweep()
    {
        var (ctId, ownerId) = await SeedAsync();
        var (held, _)  = await CreateDueForPublishAsync(ctId, ownerId);
        var (free, _)  = await CreateDueForPublishAsync(ctId, ownerId);
        var repo = new ContentEntryRepository(fixture.CreateDb());

        // "Node A" is mid-sweep: it holds an update lock on one due row.
        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();
        await using var tx = (SqlTransaction)await holder.BeginTransactionAsync();
        await using (var lockCmd = holder.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT [Id] FROM [ContentEntry] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = @Id";
            lockCmd.Parameters.AddWithValue("@Id", held);
            await lockCmd.ExecuteScalarAsync();
        }

        // "Node B" sweeps now: it gets the free row and skips the held one without blocking.
        var claimed = await repo.ClaimScheduledForPublishAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Contains(claimed, e => e.Id == free);
        Assert.DoesNotContain(claimed, e => e.Id == held);
        Assert.Equal("Approved", await ScalarAsync<string>("SELECT [Status] FROM [ContentEntry] WHERE [Id] = @Id", ("@Id", held)));

        await tx.CommitAsync();

        // Once node A lets go, the next sweep takes it — once.
        var next = await repo.ClaimScheduledForPublishAsync();
        Assert.Contains(next, e => e.Id == held);
        Assert.DoesNotContain(await repo.ClaimScheduledForPublishAsync(), e => e.Id == held);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [AuditLog] WHERE [EntityType] = 'ContentEntry' AND [EntityId] = @E AND [Action] = 'ScheduledPublish'", ("@E", held.ToString())));
    }

    [Fact]
    public async Task ClaimScheduledForPublish_SetsPublishedVersion_AndQueuesPayloadLikeManualPublish()
    {
        var (ctId, ownerId) = await SeedAsync();
        var webhookId = await CreateWebhookAsync("[\"content.published\"]");
        var (id, slug) = await CreateDueForPublishAsync(ctId, ownerId);
        var versionId  = await CreateVersionAsync(id, ownerId);

        var claimed = await new ContentEntryRepository(fixture.CreateDb()).ClaimScheduledForPublishAsync();
        var entry   = Assert.Single(claimed, e => e.Id == id);
        Assert.Equal("Published", entry.Status);
        Assert.Equal(slug, entry.Slug);

        Assert.Equal(versionId, await ScalarAsync<long>("SELECT [PublishedVersionId] FROM [ContentEntry] WHERE [Id] = @Id", ("@Id", id)));

        var payload = await ScalarAsync<string>("SELECT [PayloadJson] FROM [OutboundEvent] WHERE [WebhookId] = @W AND JSON_VALUE([PayloadJson], '$.id') = @E", ("@W", webhookId), ("@E", id.ToString()));
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal(id,            doc.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(slug,          doc.RootElement.GetProperty("slug").GetString());
        Assert.Equal("en-US",       doc.RootElement.GetProperty("locale").GetString());
        Assert.Equal("issue171",    doc.RootElement.GetProperty("contentTypeName").GetString());
        Assert.Equal("Published",   doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ClaimScheduledForExpiry_UnpublishesOnce_AndQueuesContentUnpublished()
    {
        var (ctId, ownerId) = await SeedAsync();
        var webhookId = await CreateWebhookAsync("[\"content.unpublished\"]");
        var slug = $"i171-exp-{Guid.NewGuid():N}";
        var id   = await TestSeeder.CreateEntryAsync(fixture.ConnectionString, ctId, slug, "en-US", ownerId);
        await ExecAsync("UPDATE [ContentEntry] SET [Status] = 'Published', [ScheduledExpireAt] = DATEADD(SECOND, -5, SYSUTCDATETIME()) WHERE [Id] = @Id", ("@Id", id));
        var repo = new ContentEntryRepository(fixture.CreateDb());

        var first = await repo.ClaimScheduledForExpiryAsync();
        Assert.Contains(first, e => e.Id == id && e.Status == "Approved");
        Assert.DoesNotContain(await repo.ClaimScheduledForExpiryAsync(), e => e.Id == id);

        Assert.Equal("Approved", await ScalarAsync<string>("SELECT [Status] FROM [ContentEntry] WHERE [Id] = @Id", ("@Id", id)));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [AuditLog] WHERE [EntityType] = 'ContentEntry' AND [EntityId] = @E AND [Action] = 'ScheduledExpire'", ("@E", id.ToString())));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM [OutboundEvent] WHERE [WebhookId] = @W AND [EventName] = 'content.unpublished' AND JSON_VALUE([PayloadJson], '$.id') = @E", ("@W", webhookId), ("@E", id.ToString())));
    }

    // ── AC3: settings cache bust across nodes ────────────────────────────────

    [Fact]
    public async Task SettingsWrite_OnOneNode_IsSeenByAnotherNode_OnItsNextChangePoll()
    {
        var repo  = new SiteSettingRepository(fixture.ConnectionString);
        var nodeA = new SiteSettingsService(repo);
        var nodeB = new SiteSettingsService(repo);
        await nodeA.RefreshAsync();
        await nodeB.RefreshAsync();
        var before = nodeB.GetInt(SiteSettingKeys.AdminContentListPageSize);

        try
        {
            Assert.False(await nodeB.RefreshIfChangedAsync());   // nothing moved: no reload

            await repo.SetValueAsync(SiteSettingKeys.AdminContentListPageSize, "37", updatedById: 0);
            await nodeA.RefreshAsync();                          // the writing node reloads itself (controller)
            Assert.Equal(37, nodeA.GetInt(SiteSettingKeys.AdminContentListPageSize));
            Assert.Equal(before, nodeB.GetInt(SiteSettingKeys.AdminContentListPageSize));

            Assert.True(await nodeB.RefreshIfChangedAsync());    // stamp moved: reload
            Assert.Equal(37, nodeB.GetInt(SiteSettingKeys.AdminContentListPageSize));
            Assert.Equal(nodeA.ChangeStamp, nodeB.ChangeStamp);
            Assert.False(await nodeB.RefreshIfChangedAsync());
        }
        finally
        {
            await repo.ResetAsync(SiteSettingKeys.AdminContentListPageSize, updatedById: 0);
        }
    }

    // ── Readiness ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OutboxHealthCheck_DegradedWhenOldestDueRowIsStale()
    {
        var outbox = new InMemoryOutboxRepository();
        var check  = new OutboxHealthCheck(outbox, StaticSiteSettings.Defaults.With(SiteSettingKeys.OutboxStaleAfterSeconds, 60));
        var ctx    = new HealthCheckContext();

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(ctx)).Status);

        await outbox.EnqueueAsync(OutboundEventTypes.Smtp, null, "[]");
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(ctx)).Status);

        outbox.Rows[0].NextAttemptAt = DateTime.UtcNow.AddMinutes(-5);
        var stale = await check.CheckHealthAsync(ctx);
        Assert.Equal(HealthStatus.Degraded, stale.Status);
        Assert.Contains("no node is delivering", stale.Description);
        Assert.Equal(1L, stale.Data["pending"]);

        var off = new OutboxHealthCheck(outbox, StaticSiteSettings.Defaults.With(SiteSettingKeys.OutboxStaleAfterSeconds, 0));
        Assert.Equal(HealthStatus.Healthy, (await off.CheckHealthAsync(ctx)).Status);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Marker() => $"i171-{Guid.NewGuid():N}";

    private static OutboxDispatcherWorker Worker(IOutboxRepository outbox, IServiceProvider services, ISiteSettingsService settings, string instance) =>
        new(outbox, services.GetRequiredService<IServiceScopeFactory>(), settings, NullLogger<OutboxDispatcherWorker>.Instance, instance);

    private ServiceProvider SchedulerServices()
    {
        var settings = StaticSiteSettings.Defaults
            .With(SiteSettingKeys.FeatureScheduledPublishing, true)
            .With(SiteSettingKeys.FeatureNotifications, true)
            .With(SiteSettingKeys.NotificationsEmailEnabled, true);
        var services = new ServiceCollection();
        services.AddSingleton<ISiteSettingsService>(settings);
        services.AddSingleton<IOutboxRepository>(new OutboxRepository(fixture.ConnectionString));
        services.AddScoped(_ => fixture.CreateDb());
        services.AddScoped<IContentEntryRepository, ContentEntryRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IEmailDispatcher>(sp => new OutboxEmailDispatcher(sp.GetRequiredService<IOutboxRepository>(), NullLogger<OutboxEmailDispatcher>.Instance));
        services.AddScoped<IWorkflowNotifier>(sp => new WorkflowNotifier(
            sp.GetRequiredService<INotificationRepository>(), sp.GetRequiredService<IEmailDispatcher>(),
            NullLogger<WorkflowNotifier>.Instance, settings));
        return services.BuildServiceProvider();
    }

    private static ScheduledPublishWorker SchedulerWorker(IServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScheduledPublishWorker>.Instance, services.GetRequiredService<ISiteSettingsService>());

    private async Task<(long ContentTypeId, long OwnerId)> SeedAsync()
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId    = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "issue171");
        return (ctId, ownerId);
    }

    private async Task<(long Id, string Slug)> CreateDueForPublishAsync(long ctId, long ownerId)
    {
        var slug = $"i171-{Guid.NewGuid():N}";
        var id   = await TestSeeder.CreateEntryAsync(fixture.ConnectionString, ctId, slug, "en-US", ownerId);
        await ExecAsync("UPDATE [ContentEntry] SET [Status] = 'Approved', [ScheduledPublishAt] = DATEADD(SECOND, -5, SYSUTCDATETIME()) WHERE [Id] = @Id", ("@Id", id));
        return (id, slug);
    }

    private async Task<long> CreateVersionAsync(long entryId, long authorId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentVersion_Create @ContentEntryId, @FieldsJson, NULL, @Status, @AuthorId, NULL, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", entryId);
        cmd.Parameters.AddWithValue("@FieldsJson", "{\"title\":\"Issue 171\"}");
        cmd.Parameters.AddWithValue("@Status", "Draft");
        cmd.Parameters.AddWithValue("@AuthorId", authorId);
        var newId = cmd.Parameters.Add("@NewId", SqlDbType.BigInt);
        newId.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)newId.Value;
    }

    private async Task<long> CreateWebhookAsync(string eventsJson)
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Webhook_Create @Name, @Url, @Secret, @EventsJson, @CreatedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@Name", "issue171");
        cmd.Parameters.AddWithValue("@Url", $"https://hooks.example.test/{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("@Secret", "dp1:test");
        cmd.Parameters.AddWithValue("@EventsJson", eventsJson);
        cmd.Parameters.AddWithValue("@CreatedById", userId);
        var newId = cmd.Parameters.Add("@NewId", SqlDbType.BigInt);
        newId.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)newId.Value;
    }

    /// <summary>Claim-and-complete everything due, so a test that asserts on its own claims is not handed leftovers.</summary>
    private async Task DrainAsync()
    {
        var outbox = Outbox();
        while (true)
        {
            var batch = await outbox.ClaimAsync("drain", 500, 300);
            if (batch.Count == 0) return;
            foreach (var r in batch) await outbox.CompleteAsync(r.Id, "drain");
        }
    }

    private async Task<OutboundEvent> FirstRowAsync(string payload)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 * FROM [OutboundEvent] WHERE [PayloadJson] = @P ORDER BY [Id]";
        cmd.Parameters.AddWithValue("@P", payload);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), "row not found");
        return new OutboundEvent
        {
            Id          = r.GetInt64(r.GetOrdinal("Id")),
            Type        = r.GetString(r.GetOrdinal("Type")),
            EventName   = r.IsDBNull(r.GetOrdinal("EventName")) ? null : r.GetString(r.GetOrdinal("EventName")),
            WebhookId   = r.IsDBNull(r.GetOrdinal("WebhookId")) ? null : r.GetInt64(r.GetOrdinal("WebhookId")),
            Status      = r.GetString(r.GetOrdinal("Status")),
            Attempts    = r.GetInt32(r.GetOrdinal("Attempts")),
            LockedBy    = r.IsDBNull(r.GetOrdinal("LockedBy")) ? null : r.GetString(r.GetOrdinal("LockedBy")),
            LastError   = r.IsDBNull(r.GetOrdinal("LastError")) ? null : r.GetString(r.GetOrdinal("LastError")),
            CompletedAt = r.IsDBNull(r.GetOrdinal("CompletedAt")) ? null : r.GetDateTime(r.GetOrdinal("CompletedAt")),
        };
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        var value = await cmd.ExecuteScalarAsync();
        return typeof(T) == typeof(object) ? (T)value! : (T)Convert.ChangeType(value, typeof(T))!;
    }

    private async Task<List<long>> IdsAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        var ids = new List<long>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) ids.Add(r.GetInt64(0));
        return ids;
    }

    private sealed class DelegateConsumer(string type, Func<OutboundEvent, Task<OutboxOutcome>> handle) : IOutboxConsumer
    {
        public string Type => type;
        public Task<OutboxOutcome> HandleAsync(OutboundEvent evt, CancellationToken ct) => handle(evt);
    }
}
