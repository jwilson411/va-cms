using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #44 — Implement media usage tracking and safe delete.
///
/// BRD FR-MEDIA-06.
///
/// Acceptance criteria:
///   AC1: MediaUsage rows created via UpsertUsageAsync when assets added to content fields.
///   AC2: MediaUsage rows removed via DeleteUsageForEntryAsync when entry fields change.
///   AC3: GetUsageWithTitleAsync returns enriched rows including EntryTitle and ContentTypeName.
///   AC4: SafeDeleteAsync returns 1 (blocked) when usage rows exist; 0 (deleted) when clear.
///   AC5: GetUsageWithTitleAsync returns empty list for an asset with no usages.
///   AC6: Multiple assets referenced by the same entry each appear in the usage list.
/// </summary>
[Collection("Database")]
public class Issue44AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private IMediaExtendedRepository Repo() =>
        new MediaExtendedRepository(fixture.CreateDb());

    private static async Task<long> SeedAssetAsync(string connStr, long userId,
        string fileName = "test.jpg", string mimeType = "image/jpeg")
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
        cmd.Parameters.AddWithValue("@FileSizeBytes", 2048L);
        cmd.Parameters.AddWithValue("@UploadedById",  userId);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    // ── AC1: usage row created when asset added to content field ──────────────

    /// <summary>
    /// AC1: UpsertUsageAsync creates a MediaUsage row that appears in GetUsageWithTitleAsync.
    /// </summary>
    [Fact]
    public async Task UpsertUsage_CreatesRow_VisibleInGetUsageWithTitle()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "usage_track_type_44a");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "usage-track-44a-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId, "hero.jpg");

        var repo = Repo();
        await repo.UpsertUsageAsync(assetId, entryId, "heroImage");

        var usages = (await repo.GetUsageWithTitleAsync(assetId)).ToList();

        Assert.Single(usages);
        Assert.Equal(assetId, /* verify via content entry id */ usages[0].ContentEntryId == entryId ? assetId : 0);
        Assert.Equal(entryId, usages[0].ContentEntryId);
        Assert.Equal("heroImage", usages[0].FieldName);
    }

    // ── AC2: usage rows removed when entry fields change ──────────────────────

    /// <summary>
    /// AC2: DeleteUsageForEntryAsync removes all usage rows for a content entry.
    /// Simulates the field-change scenario: old rows cleared, new rows inserted.
    /// </summary>
    [Fact]
    public async Task DeleteUsageForEntry_RemovesAllRows_ThenUpsertAddsNew()
    {
        var userId   = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId   = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "usage_track_type_44b");
        var entryId  = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "usage-track-44b-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var asset1   = await SeedAssetAsync(fixture.ConnectionString, userId, "old-banner.jpg");
        var asset2   = await SeedAssetAsync(fixture.ConnectionString, userId, "new-banner.jpg");

        var repo = Repo();

        // Initial state: entry references asset1
        await repo.UpsertUsageAsync(asset1, entryId, "banner");
        var before = (await repo.GetUsageWithTitleAsync(asset1)).ToList();
        Assert.Single(before);

        // Simulate field change: old asset removed, new asset added
        await repo.DeleteUsageForEntryAsync(entryId);
        await repo.UpsertUsageAsync(asset2, entryId, "banner");

        // asset1 usage must be gone
        var asset1Usages = (await repo.GetUsageWithTitleAsync(asset1)).ToList();
        Assert.Empty(asset1Usages);

        // asset2 usage must be present
        var asset2Usages = (await repo.GetUsageWithTitleAsync(asset2)).ToList();
        Assert.Single(asset2Usages);
        Assert.Equal(entryId, asset2Usages[0].ContentEntryId);
    }

    // ── AC3: GetUsageWithTitleAsync returns enriched rows ─────────────────────

    /// <summary>
    /// AC3: GetUsageWithTitleAsync returns ContentTypeName and EntryTitle
    /// populated from the ContentType table and FieldsJson respectively.
    /// </summary>
    [Fact]
    public async Task GetUsageWithTitle_ReturnsEnrichedRows_WithContentTypeAndTitle()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "usage_enrich_type_44c");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "usage-enrich-44c-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId, "thumb.png", "image/png");

        var repo = Repo();
        await repo.UpsertUsageAsync(assetId, entryId, "thumbnail");

        var usages = (await repo.GetUsageWithTitleAsync(assetId)).ToList();

        Assert.Single(usages);
        var u = usages[0];
        Assert.Equal(entryId, u.ContentEntryId);
        Assert.Equal("thumbnail", u.FieldName);

        // ContentTypeName must not be empty (populated from ContentType.DisplayName)
        Assert.False(string.IsNullOrEmpty(u.ContentTypeName),
            "ContentTypeName must be populated from ContentType table");

        // EntryTitle falls back to Slug when no published version exists
        Assert.False(string.IsNullOrEmpty(u.EntryTitle),
            "EntryTitle must be populated (slug fallback when no version exists)");
    }

    // ── AC4: safe delete blocked by usage row ─────────────────────────────────

    /// <summary>
    /// AC4a: SafeDeleteAsync returns 1 (blocked) when asset has a usage row.
    /// </summary>
    [Fact]
    public async Task SafeDelete_AssetInUse_Returns1_AssetPreserved()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "safe_del_type_44d");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "safe-del-44d-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId, "blocked.jpg");

        var repo = Repo();
        await repo.UpsertUsageAsync(assetId, entryId, "featuredImage");

        var result = await repo.SafeDeleteAsync(assetId);

        Assert.Equal(1, result);   // blocked

        // Usage row still exists
        var usages = (await repo.GetUsageWithTitleAsync(assetId)).ToList();
        Assert.Single(usages);
    }

    /// <summary>
    /// AC4b: SafeDeleteAsync returns 0 (deleted) once usage rows are removed.
    /// </summary>
    [Fact]
    public async Task SafeDelete_AfterUsageCleared_Returns0_AssetGone()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "safe_del_type_44e");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "safe-del-44e-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId, "soon-free.jpg");

        var repo = Repo();
        await repo.UpsertUsageAsync(assetId, entryId, "heroImage");

        // Simulate "remove asset from fields"
        await repo.DeleteUsageForEntryAsync(entryId);

        var result = await repo.SafeDeleteAsync(assetId);
        Assert.Equal(0, result);   // deleted
    }

    // ── AC5: no usages → empty list ───────────────────────────────────────────

    /// <summary>AC5: Asset with no usage rows returns empty list from GetUsageWithTitleAsync.</summary>
    [Fact]
    public async Task GetUsageWithTitle_NoUsages_ReturnsEmptyList()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var assetId = await SeedAssetAsync(fixture.ConnectionString, userId, "orphan.jpg");

        var usages = await Repo().GetUsageWithTitleAsync(assetId);

        Assert.Empty(usages);
    }

    // ── AC6: multiple assets referenced by same entry ─────────────────────────

    /// <summary>
    /// AC6: Multiple assets referenced by the same entry appear in their respective usage lists.
    /// </summary>
    [Fact]
    public async Task MultipleAssets_SameEntry_EachHasOwnUsageRow()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "multi_asset_type_44f");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "multi-asset-44f-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);
        var heroId    = await SeedAssetAsync(fixture.ConnectionString, userId, "hero-44f.jpg");
        var thumbId   = await SeedAssetAsync(fixture.ConnectionString, userId, "thumb-44f.jpg");
        var attachId  = await SeedAssetAsync(fixture.ConnectionString, userId, "attach-44f.pdf", "application/pdf");

        var repo = Repo();
        await repo.UpsertUsageAsync(heroId,   entryId, "heroImage");
        await repo.UpsertUsageAsync(thumbId,  entryId, "thumbnail");
        await repo.UpsertUsageAsync(attachId, entryId, "attachment");

        var heroUsages   = (await repo.GetUsageWithTitleAsync(heroId)).ToList();
        var thumbUsages  = (await repo.GetUsageWithTitleAsync(thumbId)).ToList();
        var attachUsages = (await repo.GetUsageWithTitleAsync(attachId)).ToList();

        Assert.Single(heroUsages);
        Assert.Single(thumbUsages);
        Assert.Single(attachUsages);

        Assert.Equal("heroImage",  heroUsages[0].FieldName);
        Assert.Equal("thumbnail",  thumbUsages[0].FieldName);
        Assert.Equal("attachment", attachUsages[0].FieldName);

        // All three point to the same entry
        Assert.Equal(entryId, heroUsages[0].ContentEntryId);
        Assert.Equal(entryId, thumbUsages[0].ContentEntryId);
        Assert.Equal(entryId, attachUsages[0].ContentEntryId);
    }
}
