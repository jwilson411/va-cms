using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

// ────────────────────────────────────────────────────────────────────────────
// §6.7 Database Monitoring SPs — usp_Monitor_IndexFragmentation,
//      usp_Monitor_TableSizes, usp_Monitor_LongRunningQueries
//
// These SPs query DMVs (sys.dm_db_index_physical_stats, sys.dm_exec_requests).
// They are tested against the live TestContainers SQL Server instance, which
// has all migrations applied (V009__monitoring_sps.sql creates the SPs).
//
// Index fragmentation: new test DB has no fragmentation yet — SP returns
//   zero rows meeting the page_count > 50 threshold. We verify it executes
//   without error and returns a non-null collection.
// Table sizes: test DB has all tables from V001 — SP always returns rows.
// Long-running queries: no long-running queries in a quiet test DB — SP
//   returns zero rows. We verify it executes without error.
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class DbMonitorRepositoryTests(DatabaseFixture fixture)
{
    // ── usp_Monitor_IndexFragmentation ────────────────────────────────────

    [Fact]
    public async Task GetIndexFragmentationAsync_DoesNotThrow_And_ReturnsCollection()
    {
        var repo = new DbMonitorRepository(fixture.CreateDb());

        var rows = (await repo.GetIndexFragmentationAsync()).ToList();

        // May be empty in a fresh test DB (no fragmentation above threshold yet).
        Assert.NotNull(rows);
        // Every returned row must have a non-empty TableName and a valid percentage.
        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.TableName));
            Assert.True(r.FragmentationPct >= 0 && r.FragmentationPct <= 100);
            Assert.True(r.PageCount > 0);
        });
    }

    // ── usp_Monitor_TableSizes ────────────────────────────────────────────

    [Fact]
    public async Task GetTableSizesAsync_ReturnsAllUserTables()
    {
        var repo = new DbMonitorRepository(fixture.CreateDb());

        var rows = (await repo.GetTableSizesAsync()).ToList();

        // The test DB has the full schema from V001 — at least ContentType and
        // ContentEntry tables must appear.
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.TableName == "ContentType");
        Assert.Contains(rows, r => r.TableName == "ContentEntry");

        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.TableName));
            Assert.True(r.RowCount >= 0);
            Assert.True(r.TotalSizeMB >= 0);
            Assert.True(r.UsedSizeMB >= 0);
        });
    }

    [Fact]
    public async Task GetTableSizesAsync_RowCountReflectsSeededData()
    {
        // Seed at least one ContentType row so the row count > 0.
        var ctId = await TestSeeder.EnsureContentTypeAsync(
            fixture.ConnectionString, $"ct-monitor-{Guid.NewGuid():N}");

        var repo = new DbMonitorRepository(fixture.CreateDb());
        var rows = (await repo.GetTableSizesAsync()).ToList();

        var ctRow = rows.FirstOrDefault(r => r.TableName == "ContentType");
        Assert.NotNull(ctRow);
        Assert.True(ctRow!.RowCount > 0);
    }

    // ── usp_Monitor_LongRunningQueries ────────────────────────────────────

    [Fact]
    public async Task GetLongRunningQueriesAsync_DoesNotThrow_And_ReturnsCollection()
    {
        var repo = new DbMonitorRepository(fixture.CreateDb());

        // A quiet test DB will have no queries running > 5 seconds.
        var rows = (await repo.GetLongRunningQueriesAsync()).ToList();

        Assert.NotNull(rows);
        // Verify column mapping: all returned rows must have valid fields.
        Assert.All(rows, r =>
        {
            Assert.True(r.SessionId > 0);
            Assert.False(string.IsNullOrWhiteSpace(r.Status));
            Assert.True(r.DurationSec >= 5); // SP filters WHERE DurationSec > 5
        });
    }
}
