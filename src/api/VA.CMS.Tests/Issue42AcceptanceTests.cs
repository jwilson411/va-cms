using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #42 — Build media library browser in admin.
///
/// BRD FR-MEDIA-01.
///
/// Acceptance criteria:
///   AC1: GET /api/v1/media returns paginated list (grid/list view is client-side toggle).
///   AC2: Search by filename, alt text — searchTerm param filters results.
///   AC3: Filter by MIME type prefix (mimeTypePrefix param).
///   AC4: GET /api/v1/media/{id} returns detail: preview, alt text, usage list, metadata.
///   AC5: PATCH /api/v1/media/{id} updates alt text, title, description, tags.
///   AC6: DELETE /api/v1/media/{id} — safe delete: returns 0 when no usages, 1 when in use.
///
/// Test strategy:
///   - MediaAssetRepository integration tests: real DB (DatabaseFixture).
///   - MediaExtendedRepository integration tests: usage list returns full detail rows.
///   - Safe delete behaviour: blocked when MediaUsage row exists.
/// </summary>
[Collection("Database")]
public class Issue42AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private IMediaAssetRepository AssetRepo() =>
        new MediaAssetRepository(fixture.CreateDb());

    private IMediaExtendedRepository ExtendedRepo() =>
        new MediaExtendedRepository(fixture.CreateDb());

    /// <summary>
    /// Seeds a MediaAsset row directly via the stored procedure.
    /// Optionally sets alt text via usp_MediaAsset_UpdateMetadata.
    /// </summary>
    private static async Task<long> SeedAssetAsync(
        string connStr,
        long userId,
        string fileName = "photo.jpg",
        string mimeType = "image/jpeg",
        string? altText = null)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_MediaAsset_Create " +
            "@FileName, @StoragePath, @StorageBackend, @MimeType, " +
            "@FileSizeBytes, NULL, NULL, @UploadedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@FileName",      fileName);
        cmd.Parameters.AddWithValue("@StoragePath",   "uploads/" + Guid.NewGuid() + "/" + fileName);
        cmd.Parameters.AddWithValue("@StorageBackend","local");
        cmd.Parameters.AddWithValue("@MimeType",      mimeType);
        cmd.Parameters.AddWithValue("@FileSizeBytes", 1024L);
        cmd.Parameters.AddWithValue("@UploadedById",  userId);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var assetId = (long)outParam.Value;

        if (altText is not null)
        {
            await using var conn2 = new SqlConnection(connStr);
            await conn2.OpenAsync();
            await using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "EXEC usp_MediaAsset_UpdateMetadata @Id, @AltText, NULL, NULL, NULL";
            cmd2.Parameters.AddWithValue("@Id",      assetId);
            cmd2.Parameters.AddWithValue("@AltText", altText);
            await cmd2.ExecuteNonQueryAsync();
        }

        return assetId;
    }

    // ── AC1: list returns paginated results ──────────────────────────────────

    /// <summary>AC1: ListAsync returns at least the seeded items.</summary>
    [Fact]
    public async Task List_ReturnsPaginatedAssets()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo   = AssetRepo();

        await SeedAssetAsync(fixture.ConnectionString, userId, "list-a.jpg");
        await SeedAssetAsync(fixture.ConnectionString, userId, "list-b.png");
        await SeedAssetAsync(fixture.ConnectionString, userId, "list-c.pdf", "application/pdf");

        var page = await repo.ListAsync(1, 200);

        Assert.NotNull(page);
        // At least the 3 we seeded (shared DB may have more from earlier tests)
        Assert.True(page.Items.Count >= 3);
    }

    /// <summary>AC1: pageSize clamped — page 1 with pageSize 1 returns exactly 1 item.</summary>
    [Fact]
    public async Task List_PageSize1_ReturnsOneItem()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        await SeedAssetAsync(fixture.ConnectionString, userId);

        var page = await AssetRepo().ListAsync(1, 1);

        Assert.Equal(1, page.Items.Count);
    }

    // ── AC2: search by filename / alt text ───────────────────────────────────

    /// <summary>AC2: searchTerm param filters by filename substring.</summary>
    [Fact]
    public async Task List_SearchByFilename_FiltersResults()
    {
        var userId      = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var uniqueName   = $"srch-fn-{uniqueSuffix}.jpg";

        await SeedAssetAsync(fixture.ConnectionString, userId, uniqueName);

        var page = await AssetRepo().ListAsync(1, 50, searchTerm: uniqueSuffix);

        Assert.Contains(page.Items, a => a.FileName.Contains(uniqueSuffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>AC2: searchTerm param filters by alt text content.</summary>
    [Fact]
    public async Task List_SearchByAltText_FiltersResults()
    {
        var userId    = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var uniqueAlt = "unique-alt-" + Guid.NewGuid().ToString("N")[..8];

        await SeedAssetAsync(fixture.ConnectionString, userId, "photo.jpg", "image/jpeg", uniqueAlt);

        var page = await AssetRepo().ListAsync(1, 50, searchTerm: uniqueAlt);

        Assert.Contains(page.Items, a => a.AltText == uniqueAlt);
    }

    // ── AC3: filter by MIME type ─────────────────────────────────────────────

    /// <summary>AC3: mimeTypePrefix filter returns only matching assets.</summary>
    [Fact]
    public async Task List_FilterByMimeTypePrefix_ReturnsOnlyMatching()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var pdfName = "mime-filter-" + Guid.NewGuid().ToString("N")[..8] + ".pdf";
        await SeedAssetAsync(fixture.ConnectionString, userId, pdfName, "application/pdf");

        var page = await AssetRepo().ListAsync(1, 200, mimeTypePrefix: "application/pdf");

        Assert.All(page.Items, a =>
            Assert.StartsWith("application/pdf", a.MimeType, StringComparison.OrdinalIgnoreCase));
    }

    // ── AC4: detail returns asset + usage list ────────────────────────────────

    /// <summary>
    /// AC4: GetByIdAsync returns the full asset row;
    /// GetUsageAsync returns joined rows with Slug, Status, ContentTypeId.
    /// </summary>
    [Fact]
    public async Task Detail_IncludesUsageListWithJoinedEntryFields()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "media_browser_type_42");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "media-browser-slug-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId);

        // Record the usage
        await ExtendedRepo().UpsertUsageAsync(assetId, entryId, "featuredImage");

        // Verify detail
        var asset = await AssetRepo().GetByIdAsync(assetId);
        Assert.NotNull(asset);

        // Verify usage list has joined fields
        var usages = (await ExtendedRepo().GetUsageAsync(assetId)).ToList();
        Assert.Single(usages);
        Assert.Equal(entryId, usages[0].ContentEntryId);
        Assert.Equal("featuredImage", usages[0].FieldName);
        Assert.False(string.IsNullOrEmpty(usages[0].Slug),   "Slug must be populated from ContentEntry");
        Assert.False(string.IsNullOrEmpty(usages[0].Status), "Status must be populated from ContentEntry");
        Assert.Equal(typeId, usages[0].ContentTypeId);
    }

    /// <summary>AC4: Asset with no usages returns empty usage list.</summary>
    [Fact]
    public async Task Detail_NoUsages_ReturnsEmptyList()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId);
        var usages  = await ExtendedRepo().GetUsageAsync(assetId);

        Assert.Empty(usages);
    }

    // ── AC5: update metadata ─────────────────────────────────────────────────

    /// <summary>AC5: UpdateAsync persists alt text change to DB.</summary>
    [Fact]
    public async Task UpdateMetadata_PersistsAltText()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId);
        var repo    = AssetRepo();

        var asset = await repo.GetByIdAsync(assetId);
        Assert.NotNull(asset);

        asset!.AltText = "Updated alt text for accessibility";
        await repo.UpdateAsync(asset);

        var fromDb = await repo.GetByIdAsync(assetId);
        Assert.Equal("Updated alt text for accessibility", fromDb!.AltText);
    }

    /// <summary>AC5: UpdateAsync persists title and description.</summary>
    [Fact]
    public async Task UpdateMetadata_PersistsTitleAndDescription()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId);
        var repo    = AssetRepo();

        var asset = await repo.GetByIdAsync(assetId);
        Assert.NotNull(asset);

        asset!.Title       = "Test Title 42";
        asset.Description  = "Test Description 42";
        await repo.UpdateAsync(asset);

        var fromDb = await repo.GetByIdAsync(assetId);
        Assert.Equal("Test Title 42",        fromDb!.Title);
        Assert.Equal("Test Description 42",  fromDb.Description);
    }

    // ── AC6: safe delete ─────────────────────────────────────────────────────

    /// <summary>AC6: SafeDeleteAsync returns 0 (deleted) when no usages exist.</summary>
    [Fact]
    public async Task SafeDelete_NoUsages_DeletesAsset()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId);

        var result = await ExtendedRepo().SafeDeleteAsync(assetId);
        Assert.Equal(0, result);

        var asset = await AssetRepo().GetByIdAsync(assetId);
        Assert.Null(asset);
    }

    /// <summary>AC6: SafeDeleteAsync returns 1 (blocked) when asset is referenced.</summary>
    [Fact]
    public async Task SafeDelete_AssetInUse_ReturnsBlocked()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "safe_del_type_42");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "safe-del-slug-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId);

        var extRepo = ExtendedRepo();
        await extRepo.UpsertUsageAsync(assetId, entryId, "featuredImage");

        var result = await extRepo.SafeDeleteAsync(assetId);
        Assert.Equal(1, result);

        // Asset must still exist
        var asset = await AssetRepo().GetByIdAsync(assetId);
        Assert.NotNull(asset);
    }
}
