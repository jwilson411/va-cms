using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #43 — Enforce alt text requirement before asset use in published content.
///
/// BRD FR-MEDIA-05.
///
/// Acceptance criteria:
///   AC1: GetMissingAltTextAsync returns empty list when all image assets have alt text.
///   AC2: GetMissingAltTextAsync returns blocking assets when image assets lack alt text.
///   AC3: Non-image assets (PDFs) with no alt text do NOT block publish.
///   AC4: Assets with no MediaUsage link to the entry do NOT appear in the result.
///   AC5: After setting alt text via usp_MediaAsset_UpdateMetadata, the asset is no longer blocking.
///
/// Test strategy:
///   Integration tests using DatabaseFixture (real SQL Server).
///   All seed operations via stored procedures only — no direct DML from tests.
/// </summary>
[Collection("Database")]
public class Issue43AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private IMediaAltTextGuardRepository Guard() =>
        new MediaAltTextGuardRepository(fixture.CreateDb());

    /// <summary>Seeds an image asset (image/jpeg) with optional alt text.</summary>
    private static async Task<long> SeedImageAssetAsync(
        string connStr,
        long userId,
        string? altText = null,
        string fileName = "test-image.jpg",
        string mimeType = "image/jpeg")
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_MediaAsset_Create " +
            "@FileName, @StoragePath, @StorageBackend, @MimeType, " +
            "@FileSizeBytes, NULL, NULL, @UploadedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@FileName",       fileName);
        cmd.Parameters.AddWithValue("@StoragePath",    "uploads/" + Guid.NewGuid() + "/" + fileName);
        cmd.Parameters.AddWithValue("@StorageBackend", "local");
        cmd.Parameters.AddWithValue("@MimeType",       mimeType);
        cmd.Parameters.AddWithValue("@FileSizeBytes",  4096L);
        cmd.Parameters.AddWithValue("@UploadedById",   userId);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var assetId = (long)outParam.Value;

        if (altText is not null)
            await SetAltTextAsync(connStr, assetId, altText);

        return assetId;
    }

    private static async Task SetAltTextAsync(string connStr, long assetId, string altText)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaAsset_UpdateMetadata @Id, @AltText, NULL, NULL, NULL";
        cmd.Parameters.AddWithValue("@Id",      assetId);
        cmd.Parameters.AddWithValue("@AltText", altText);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task LinkAssetToEntryAsync(string connStr, long assetId, long entryId, string fieldName = "featuredImage")
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_MediaUsage_Upsert @MediaAssetId, @ContentEntryId, @FieldName";
        cmd.Parameters.AddWithValue("@MediaAssetId",   assetId);
        cmd.Parameters.AddWithValue("@ContentEntryId", entryId);
        cmd.Parameters.AddWithValue("@FieldName",      fieldName);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── AC1: all images have alt text — empty result ──────────────────────────

    /// <summary>AC1: Entry with all image assets having alt text returns empty list.</summary>
    [Fact]
    public async Task GetMissingAltText_AllImagesHaveAltText_ReturnsEmpty()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "alt_guard_type_43a");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "alt-guard-43a-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);

        var assetId = await SeedImageAssetAsync(
            fixture.ConnectionString, userId, altText: "A descriptive alt text");

        await LinkAssetToEntryAsync(fixture.ConnectionString, assetId, entryId);

        var missing = await Guard().GetMissingAltTextAsync(entryId);

        Assert.Empty(missing);
    }

    // ── AC2: image without alt text — returns blocking asset ─────────────────

    /// <summary>AC2: Entry referencing an image without alt text returns that asset in the list.</summary>
    [Fact]
    public async Task GetMissingAltText_ImageWithNoAltText_ReturnsBlockingAsset()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "alt_guard_type_43b");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "alt-guard-43b-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);

        var assetId = await SeedImageAssetAsync(
            fixture.ConnectionString, userId, altText: null, fileName: "no-alt.jpg");

        await LinkAssetToEntryAsync(fixture.ConnectionString, assetId, entryId);

        var missing = await Guard().GetMissingAltTextAsync(entryId);

        Assert.Single(missing);
        Assert.Equal(assetId, missing[0].Id);
        Assert.Equal("no-alt.jpg", missing[0].FileName);
        Assert.StartsWith("image/", missing[0].MimeType);
    }

    // ── AC3: non-image assets with no alt text do NOT block ──────────────────

    /// <summary>AC3: PDF with no alt text does NOT appear in the missing-alt-text list.</summary>
    [Fact]
    public async Task GetMissingAltText_PdfWithNoAltText_IsNotBlocking()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "alt_guard_type_43c");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "alt-guard-43c-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);

        var pdfId = await SeedImageAssetAsync(
            fixture.ConnectionString, userId,
            altText: null,
            fileName: "policy.pdf",
            mimeType: "application/pdf");

        await LinkAssetToEntryAsync(fixture.ConnectionString, pdfId, entryId, "attachment");

        var missing = await Guard().GetMissingAltTextAsync(entryId);

        Assert.Empty(missing);
    }

    // ── AC4: assets not linked to the entry are ignored ──────────────────────

    /// <summary>AC4: Image missing alt text but not linked to the entry does not appear.</summary>
    [Fact]
    public async Task GetMissingAltText_UnlinkedImageNoAltText_IsNotReturned()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "alt_guard_type_43d");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "alt-guard-43d-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);

        // Create asset but do NOT link it to the entry
        await SeedImageAssetAsync(fixture.ConnectionString, userId, altText: null, fileName: "orphan.jpg");

        var missing = await Guard().GetMissingAltTextAsync(entryId);

        Assert.Empty(missing);
    }

    // ── AC5: fix alt text → asset no longer blocking ─────────────────────────

    /// <summary>AC5: After alt text is set, the asset no longer blocks publish.</summary>
    [Fact]
    public async Task GetMissingAltText_AfterAltTextSet_ReturnsEmpty()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "alt_guard_type_43e");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "alt-guard-43e-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);

        var assetId = await SeedImageAssetAsync(
            fixture.ConnectionString, userId, altText: null, fileName: "fix-me.jpg");

        await LinkAssetToEntryAsync(fixture.ConnectionString, assetId, entryId);

        // Confirm it's blocking
        var before = await Guard().GetMissingAltTextAsync(entryId);
        Assert.Single(before);

        // Fix it
        await SetAltTextAsync(fixture.ConnectionString, assetId, "Now it has alt text");

        // No longer blocking
        var after = await Guard().GetMissingAltTextAsync(entryId);
        Assert.Empty(after);
    }

    // ── AC6: mixed — one image with alt text, one without ────────────────────

    /// <summary>AC6: Only the image missing alt text appears; the one with alt text does not.</summary>
    [Fact]
    public async Task GetMissingAltText_MixedAssets_ReturnsOnlyMissingOnes()
    {
        var userId  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId  = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "alt_guard_type_43f");
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, typeId,
            "alt-guard-43f-" + Guid.NewGuid().ToString("N")[..8],
            "en-US", userId);

        var withAlt    = await SeedImageAssetAsync(fixture.ConnectionString, userId, "Has alt", "with-alt.jpg");
        var withoutAlt = await SeedImageAssetAsync(fixture.ConnectionString, userId, null, "without-alt.jpg");

        await LinkAssetToEntryAsync(fixture.ConnectionString, withAlt, entryId, "hero");
        await LinkAssetToEntryAsync(fixture.ConnectionString, withoutAlt, entryId, "body");

        var missing = await Guard().GetMissingAltTextAsync(entryId);

        Assert.Single(missing);
        Assert.Equal(withoutAlt, missing[0].Id);
    }
}
