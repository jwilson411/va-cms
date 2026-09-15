using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #57 — Build audit log viewer in admin (BRD FR-USERS-06).
///
/// Acceptance criteria:
///   AC1: /admin/audit lists all AuditLog rows, newest first
///   AC2: Filters: User (actorId), Action Type (action), Entity Type (entityType), Date Range
///   AC3: Exports filtered results to CSV
///
/// Migration: V030 adds usp_AuditLog_ListPaged, usp_AuditLog_ExportCsv.
/// </summary>
[Collection("Database")]
public class Issue57AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private IAuditLogRepository AuditRepo() => new AuditLogRepository(fixture.CreateDb());

    private async Task<long> SeedActorAsync()
        => await TestSeeder.UpsertUserAsync(fixture.ConnectionString);

    private async Task WriteAuditAsync(long actorId, string entityType, string action,
        long entityId = 1L)
    {
        await AuditRepo().WriteAsync(actorId, entityType, entityId, action);
    }

    private AuditLogController Controller()
    {
        var ctrl = new AuditLogController(AuditRepo());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return ctrl;
    }

    // ── V030 migration: SPs exist ──────────────────────────────────────────────

    [Fact]
    public async Task V030_usp_AuditLog_ListPaged_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_AuditLog_ListPaged';";
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V030_usp_AuditLog_ExportCsv_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_AuditLog_ExportCsv';";
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    // ── AC1: /admin/audit lists all AuditLog rows, newest first ───────────────

    [Fact]
    public async Task AC1_ListPaged_ReturnsRows_NewestFirst()
    {
        var actorId = await SeedActorAsync();
        await WriteAuditAsync(actorId, "ContentEntry", "Publish",   entityId: 1001L);
        await WriteAuditAsync(actorId, "ContentEntry", "Unpublish", entityId: 1002L);

        var result = await AuditRepo().ListPagedAsync(page: 1, pageSize: 50);

        Assert.NotNull(result);
        Assert.True(result.Items.Count >= 2);
        Assert.True(result.TotalItems >= 2);

        // Verify newest-first ordering
        for (int i = 1; i < result.Items.Count; i++)
            Assert.True(result.Items[i - 1].CreatedAt >= result.Items[i].CreatedAt);
    }

    [Fact]
    public async Task AC1_Controller_ListAuditLog_Returns200()
    {
        var actorId = await SeedActorAsync();
        await WriteAuditAsync(actorId, "User", "Deactivate");

        var result = await Controller().ListAuditLog();

        Assert.IsType<OkObjectResult>(result);
        var ok = (OkObjectResult)result;
        Assert.IsType<AuditLogPage>(ok.Value);
    }

    [Fact]
    public async Task AC1_TotalRows_ReflectsAllMatchingRows()
    {
        var actorId = await SeedActorAsync();
        // Write a distinctive action that is unlikely to appear elsewhere
        var uniqueAction = $"TestAction_{Guid.NewGuid():N}";

        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction);
        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction);
        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction);

        // Get first page with only 1 item per page
        var page1 = await AuditRepo().ListPagedAsync(action: uniqueAction, page: 1, pageSize: 1);

        Assert.Equal(3, page1.TotalItems);
        Assert.Single(page1.Items);
    }

    // ── AC2: Filters ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Filter_ByActorId_ReturnsOnlyThatActor()
    {
        var actorA = await SeedActorAsync();
        var actorB = await SeedActorAsync();
        var uniqueAction = $"FilterActor_{Guid.NewGuid():N}";

        await WriteAuditAsync(actorA, "ContentEntry", uniqueAction);
        await WriteAuditAsync(actorB, "ContentEntry", uniqueAction);

        var result = await AuditRepo().ListPagedAsync(actorId: actorA, action: uniqueAction,
            page: 1, pageSize: 50);

        Assert.All(result.Items, r => Assert.Equal(actorA, r.ActorId));
        Assert.DoesNotContain(result.Items, r => r.ActorId == actorB);
    }

    [Fact]
    public async Task AC2_Filter_ByAction_ReturnsOnlyThatAction()
    {
        var actorId = await SeedActorAsync();
        var uniqueActionA = $"ActionA_{Guid.NewGuid():N}";
        var uniqueActionB = $"ActionB_{Guid.NewGuid():N}";

        await WriteAuditAsync(actorId, "User", uniqueActionA);
        await WriteAuditAsync(actorId, "User", uniqueActionB);

        var result = await AuditRepo().ListPagedAsync(action: uniqueActionA, page: 1, pageSize: 50);

        Assert.All(result.Items, r => Assert.Equal(uniqueActionA, r.Action));
        Assert.DoesNotContain(result.Items, r => r.Action == uniqueActionB);
    }

    [Fact]
    public async Task AC2_Filter_ByEntityType_ReturnsOnlyThatType()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"EntType_{Guid.NewGuid():N}";

        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction, entityId: 1L);
        await WriteAuditAsync(actorId, "MediaAsset", uniqueAction, entityId: 2L);

        var result = await AuditRepo().ListPagedAsync(entityType: "ContentEntry",
            action: uniqueAction, page: 1, pageSize: 50);

        Assert.All(result.Items, r => Assert.Equal("ContentEntry", r.EntityType));
        Assert.DoesNotContain(result.Items, r => r.EntityType == "MediaAsset");
    }

    [Fact]
    public async Task AC2_Filter_ByDateRange_ReturnsOnlyRowsInRange()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"DateRange_{Guid.NewGuid():N}";

        await WriteAuditAsync(actorId, "User", uniqueAction);

        var fromDate = DateTime.UtcNow.AddMinutes(-5);
        var toDate   = DateTime.UtcNow.AddMinutes(5);

        var result = await AuditRepo().ListPagedAsync(
            action: uniqueAction, fromDate: fromDate, toDate: toDate,
            page: 1, pageSize: 50);

        Assert.True(result.Items.Count >= 1);
        Assert.All(result.Items, r =>
        {
            Assert.True(r.CreatedAt >= fromDate);
            Assert.True(r.CreatedAt <= toDate);
        });
    }

    [Fact]
    public async Task AC2_Controller_ListAuditLog_Filters_Returns200()
    {
        var actorId = await SeedActorAsync();
        await WriteAuditAsync(actorId, "ContentEntry", "Publish");

        var result = await Controller().ListAuditLog(
            actorId: actorId, entityType: "ContentEntry", action: "Publish",
            page: 1, pageSize: 50);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AC2_Pagination_SecondPage_HasDifferentItems()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"Paging_{Guid.NewGuid():N}";

        for (int i = 0; i < 5; i++)
            await WriteAuditAsync(actorId, "ContentEntry", uniqueAction, entityId: (long)(i + 100));

        var page1 = await AuditRepo().ListPagedAsync(action: uniqueAction, page: 1, pageSize: 2);
        var page2 = await AuditRepo().ListPagedAsync(action: uniqueAction, page: 2, pageSize: 2);

        Assert.Equal(5, page1.TotalItems);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);

        // No overlap between pages
        var page1Ids = page1.Items.Select(r => r.Id).ToHashSet();
        Assert.DoesNotContain(page2.Items, r => page1Ids.Contains(r.Id));
    }

    // ── AC3: Export filtered results to CSV ───────────────────────────────────

    [Fact]
    public async Task AC3_ExportAsync_ReturnsCsvRows()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"Export_{Guid.NewGuid():N}";

        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction, entityId: 999L);

        var rows = await AuditRepo().ExportAsync(action: uniqueAction);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(uniqueAction, r.Action));
    }

    [Fact]
    public async Task AC3_Controller_ExportCsv_Returns200_WithCsvContentType()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"CsvExport_{Guid.NewGuid():N}";
        await WriteAuditAsync(actorId, "User", uniqueAction);

        var result = await Controller().ExportCsv(action: uniqueAction);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", fileResult.ContentType);
        Assert.Equal("audit-log.csv", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task AC3_ExportCsv_ContainsCsvHeader()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"CsvHdr_{Guid.NewGuid():N}";
        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction);

        var result = await Controller().ExportCsv(action: uniqueAction);

        var fileResult = Assert.IsType<FileContentResult>(result);
        var csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);

        Assert.StartsWith("Id,CreatedAt,ActorEmail,ActorDisplayName,EntityType,EntityId,Action", csv);
    }

    [Fact]
    public async Task AC3_ExportCsv_ContainsDataRows()
    {
        var actorId = await SeedActorAsync();
        var uniqueAction = $"CsvData_{Guid.NewGuid():N}";
        await WriteAuditAsync(actorId, "ContentEntry", uniqueAction, entityId: 777L);

        var result = await Controller().ExportCsv(action: uniqueAction);

        var fileResult = Assert.IsType<FileContentResult>(result);
        var csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // At least header + 1 data row
        Assert.True(lines.Length >= 2);
        // Data line should contain the entity type
        Assert.Contains("ContentEntry", csv);
    }

    [Fact]
    public async Task AC3_ExportCsv_EmptyResult_ReturnsHeaderOnly()
    {
        var uniqueAction = $"EmptyCsv_{Guid.NewGuid():N}_never";
        // Don't write any audit row with this action

        var result = await Controller().ExportCsv(action: uniqueAction);

        var fileResult = Assert.IsType<FileContentResult>(result);
        var csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Only the header line
        Assert.Single(lines);
        Assert.StartsWith("Id,", lines[0]);
    }
}
