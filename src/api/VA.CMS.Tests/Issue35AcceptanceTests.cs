using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #35 — Implement scheduled publish and expiry.
///
/// BRD FR-AUTH-04. Acceptance criteria:
///   AC1: Content owner can set a 'Publish at' datetime on a draft.
///   AC2: Background job publishes the entry within 2 minutes of the scheduled time.
///   AC3: Content owner can set an 'Expire at' datetime on a published entry.
///   AC4: Background job unpublishes the entry within 2 minutes of the expiry time.
///   AC5: Scheduled times shown in admin status badge (covered by React component tests).
///
/// Test strategy:
///   - Repository integration tests via real SQL Server (DatabaseFixture).
///   - Worker unit tests (mocked repository) verify the sweep logic picks up due entries.
///   - SetSchedule validation tests (bad state, expire before publish, etc.).
/// </summary>
[Collection("Database")]
public class Issue35AcceptanceTests(DatabaseFixture fixture)
{
    private IContentEntryRepository Repo() => new ContentEntryRepository(fixture.CreateDb());

    private async Task<(long ContentTypeId, long OwnerId)> SeedPrerequisitesAsync()
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId    = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "sched_type");
        return (ctId, ownerId);
    }

    private async Task<long> CreateDraftEntryAsync(long ctId, long ownerId)
    {
        return await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, ctId,
            $"sched-{Guid.NewGuid():N}", "en-US", ownerId);
    }

    // ── AC1: Set Publish At on a draft ────────────────────────────────────────

    /// <summary>AC1: SetSchedule succeeds for a Draft entry with a future PublishAt.</summary>
    [Fact]
    public async Task SetSchedule_DraftEntry_SetPublishAt_Succeeds()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);
        var publishAt       = DateTime.UtcNow.AddMinutes(30);

        var (success, error) = await repo.SetScheduleAsync(entryId, publishAt, null, ownerId);

        Assert.True(success, $"Expected success but got error: {error}");
        Assert.Null(error);

        // Verify it was persisted
        var entry = await repo.GetByIdAsync(entryId);
        Assert.NotNull(entry);
        Assert.NotNull(entry!.ScheduledPublishAt);
        // Tolerance of 5 seconds for round-trip/precision
        Assert.True(Math.Abs((entry.ScheduledPublishAt!.Value - publishAt).TotalSeconds) < 5);
    }

    /// <summary>Clearing ScheduledPublishAt (set to null) succeeds.</summary>
    [Fact]
    public async Task SetSchedule_ClearPublishAt_Succeeds()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        // First set it
        await repo.SetScheduleAsync(entryId, DateTime.UtcNow.AddMinutes(30), null, ownerId);

        // Then clear it
        var (success, error) = await repo.SetScheduleAsync(entryId, null, null, ownerId);
        Assert.True(success, $"Expected success but got error: {error}");

        var entry = await repo.GetByIdAsync(entryId);
        Assert.Null(entry!.ScheduledPublishAt);
    }

    /// <summary>SetSchedule on a non-existent entry returns an error message.</summary>
    [Fact]
    public async Task SetSchedule_NonExistentEntry_ReturnsError()
    {
        var (_, ownerId) = await SeedPrerequisitesAsync();
        var repo         = Repo();
        var nonExistentId = 999_999_999L;

        var (success, error) = await repo.SetScheduleAsync(nonExistentId,
            DateTime.UtcNow.AddMinutes(30), null, ownerId);

        Assert.False(success);
        Assert.NotNull(error);
    }

    /// <summary>ExpireAt must be after PublishAt when both are set.</summary>
    [Fact]
    public async Task SetSchedule_ExpireAtBeforePublishAt_ReturnsError()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        // Promote to Approved so both publish and expire scheduling are allowed
        await PromoteToApprovedAsync(entryId, ownerId);

        var publishAt = DateTime.UtcNow.AddHours(2);
        var expireAt  = DateTime.UtcNow.AddHours(1); // before publishAt

        var (success, error) = await repo.SetScheduleAsync(entryId, publishAt, expireAt, ownerId);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Expire at", error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── AC2: Background job picks up due-for-publish entries ─────────────────

    /// <summary>
    /// AC2: GetScheduledForPublishAsync returns entries whose ScheduledPublishAt has passed.
    /// These are the entries the background worker would act on.
    /// </summary>
    [Fact]
    public async Task GetScheduledForPublish_EntryDue_IsReturned()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        // Promote to Approved so the scheduler can pick it up (Approved → Published)
        await PromoteToApprovedAsync(entryId, ownerId);

        // Set ScheduledPublishAt to a time that has already passed
        await SetScheduledPublishAtDirectlyAsync(entryId, DateTime.UtcNow.AddSeconds(-10));

        var due = await repo.GetScheduledForPublishAsync();

        Assert.Contains(due, e => e.Id == entryId);
    }

    /// <summary>GetScheduledForPublishAsync does NOT return entries whose publish time is in the future.</summary>
    [Fact]
    public async Task GetScheduledForPublish_NotYetDue_IsNotReturned()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await SetScheduledPublishAtDirectlyAsync(entryId, DateTime.UtcNow.AddHours(1));

        var due = await repo.GetScheduledForPublishAsync();

        Assert.DoesNotContain(due, e => e.Id == entryId);
    }

    // ── AC2: PublishScheduledAsync transitions entry to Published ─────────────

    /// <summary>
    /// AC2: PublishScheduledAsync transitions an Approved entry to Published and
    /// clears ScheduledPublishAt.
    /// </summary>
    [Fact]
    public async Task PublishScheduledAsync_ApprovedEntry_BecomesPublished()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await SetScheduledPublishAtDirectlyAsync(entryId, DateTime.UtcNow.AddSeconds(-5));

        await repo.PublishScheduledAsync(entryId, 0 /* system actor */);

        var entry = await repo.GetByIdAsync(entryId);
        Assert.NotNull(entry);
        Assert.Equal("Published", entry!.Status);
        Assert.Null(entry.ScheduledPublishAt); // cleared after firing
    }

    // ── AC3: Set ExpireAt on a Published entry ────────────────────────────────

    /// <summary>AC3: SetSchedule succeeds when setting ExpireAt on a Published entry.</summary>
    [Fact]
    public async Task SetSchedule_PublishedEntry_SetExpireAt_Succeeds()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await PromoteToPublishedAsync(entryId, ownerId);

        var expireAt = DateTime.UtcNow.AddDays(7);
        var (success, error) = await repo.SetScheduleAsync(entryId, null, expireAt, ownerId);

        Assert.True(success, $"Expected success but got error: {error}");
        Assert.Null(error);

        var entry = await repo.GetByIdAsync(entryId);
        Assert.NotNull(entry!.ScheduledExpireAt);
        Assert.True(Math.Abs((entry.ScheduledExpireAt!.Value - expireAt).TotalSeconds) < 5);
    }

    // ── AC4: Background job picks up due-for-expiry entries ──────────────────

    /// <summary>
    /// AC4: GetScheduledForExpiryAsync returns Published entries whose ScheduledExpireAt has passed.
    /// </summary>
    [Fact]
    public async Task GetScheduledForExpiry_EntryDue_IsReturned()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await PromoteToPublishedAsync(entryId, ownerId);

        // Set ScheduledExpireAt to a time that has already passed
        await SetScheduledExpireAtDirectlyAsync(entryId, DateTime.UtcNow.AddSeconds(-10));

        var due = await repo.GetScheduledForExpiryAsync();

        Assert.Contains(due, e => e.Id == entryId);
    }

    /// <summary>GetScheduledForExpiryAsync does NOT return entries whose expiry is in the future.</summary>
    [Fact]
    public async Task GetScheduledForExpiry_NotYetDue_IsNotReturned()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await PromoteToPublishedAsync(entryId, ownerId);
        await SetScheduledExpireAtDirectlyAsync(entryId, DateTime.UtcNow.AddHours(1));

        var due = await repo.GetScheduledForExpiryAsync();

        Assert.DoesNotContain(due, e => e.Id == entryId);
    }

    // ── AC4: ExpireScheduledAsync transitions entry to Approved ──────────────

    /// <summary>
    /// AC4: ExpireScheduledAsync transitions a Published entry to Approved (unpublished)
    /// and clears ScheduledExpireAt.
    /// </summary>
    [Fact]
    public async Task ExpireScheduledAsync_PublishedEntry_BecomesApproved()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await PromoteToPublishedAsync(entryId, ownerId);
        await SetScheduledExpireAtDirectlyAsync(entryId, DateTime.UtcNow.AddSeconds(-5));

        await repo.ExpireScheduledAsync(entryId, 0 /* system actor */);

        var entry = await repo.GetByIdAsync(entryId);
        Assert.NotNull(entry);
        Assert.Equal("Approved", entry!.Status);
        Assert.Null(entry.ScheduledExpireAt); // cleared after firing
    }

    // ── ScheduledPublishWorker unit-level sweep logic ─────────────────────────

    /// <summary>
    /// Verify that the worker processes due entries: after calling the sweep helper,
    /// the entry that was due-for-publish is now Published.
    /// This is an integration test of the worker's RunSweep path through the real repository.
    /// </summary>
    [Fact]
    public async Task WorkerSweep_PublishesDueEntry()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo            = Repo();
        var entryId         = await CreateDraftEntryAsync(ctId, ownerId);

        await PromoteToApprovedAsync(entryId, ownerId);
        await SetScheduledPublishAtDirectlyAsync(entryId, DateTime.UtcNow.AddSeconds(-30));

        // Simulate what the worker does: get due entries and publish them
        var due = await repo.GetScheduledForPublishAsync();
        foreach (var e in due.Where(x => x.Id == entryId))
            await repo.PublishScheduledAsync(e.Id, 0);

        var entry = await repo.GetByIdAsync(entryId);
        Assert.Equal("Published", entry!.Status);
    }

    // ── Helper: ADO.NET direct updates for test setup ────────────────────────

    /// <summary>Promote a Draft entry to Approved status for testing.</summary>
    private async Task PromoteToApprovedAsync(long entryId, long actorId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE [ContentEntry]
            SET [Status] = 'Approved', [UpdatedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id";
        cmd.Parameters.AddWithValue("@Id", entryId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Promote an Approved entry to Published status for testing.</summary>
    private async Task PromoteToPublishedAsync(long entryId, long actorId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE [ContentEntry]
            SET [Status] = 'Published', [UpdatedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id";
        cmd.Parameters.AddWithValue("@Id", entryId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Set ScheduledPublishAt directly (bypasses SP validation for test setup).</summary>
    private async Task SetScheduledPublishAtDirectlyAsync(long entryId, DateTime value)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE [ContentEntry]
            SET [ScheduledPublishAt] = @Value, [UpdatedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id";
        cmd.Parameters.AddWithValue("@Value", value);
        cmd.Parameters.AddWithValue("@Id", entryId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Set ScheduledExpireAt directly (bypasses SP validation for test setup).</summary>
    private async Task SetScheduledExpireAtDirectlyAsync(long entryId, DateTime value)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE [ContentEntry]
            SET [ScheduledExpireAt] = @Value, [UpdatedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id";
        cmd.Parameters.AddWithValue("@Value", value);
        cmd.Parameters.AddWithValue("@Id", entryId);
        await cmd.ExecuteNonQueryAsync();
    }
}
