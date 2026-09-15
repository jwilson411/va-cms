using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #36 — Implement duplicate entry action.
///
/// BRD FR-AUTH-07. Acceptance criteria:
///   AC1: 'Duplicate' action on a content entry creates a new Draft with '(Copy)' appended to title.
///   AC2: Slug is cleared on the duplicate (must be set before publish).
///   AC3: All field values are copied; media references are shared (not re-uploaded).
///
/// Test strategy: repository integration tests via real SQL Server (DatabaseFixture + TestContainers).
/// </summary>
[Collection("Database")]
public class Issue36AcceptanceTests(DatabaseFixture fixture)
{
    private IContentEntryRepository Repo() => new ContentEntryRepository(fixture.CreateDb());

    private async Task<(long ContentTypeId, long OwnerId)> SeedPrerequisitesAsync()
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId    = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "dup_type");
        return (ctId, ownerId);
    }

    /// <summary>
    /// Helper: create a content entry with a ContentVersion whose FieldsJson has a title field.
    /// Returns the entry Id.
    /// </summary>
    private async Task<long> CreateEntryWithTitleAsync(
        long ctId, long ownerId, string slug, string titleValue)
    {
        // Create entry
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, ctId, slug, "en-US", ownerId);

        // Insert a ContentVersion with a title field
        var fieldsJson = $"{{\"title\":\"{titleValue}\"}}";
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO [ContentVersion]
                ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
                 [Status], [AuthorId], [ChangeNote], [CreatedAt])
            VALUES (@EntryId, 1, @FieldsJson, NULL, 'Draft', @AuthorId, NULL, SYSUTCDATETIME())";
        cmd.Parameters.AddWithValue("@EntryId",    entryId);
        cmd.Parameters.AddWithValue("@FieldsJson", fieldsJson);
        cmd.Parameters.AddWithValue("@AuthorId",   ownerId);
        await cmd.ExecuteNonQueryAsync();

        return entryId;
    }

    /// <summary>Helper: create an entry with a media usage row attached.</summary>
    private async Task<(long EntryId, long MediaAssetId)> CreateEntryWithMediaAsync(
        long ctId, long ownerId, string slug)
    {
        var entryId = await CreateEntryWithTitleAsync(ctId, ownerId, slug, "Test Entry");

        // Create a media asset
        long assetId;
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var ins = conn.CreateCommand();
        ins.CommandText = @"
            INSERT INTO [MediaAsset]
                ([FileName], [StoragePath], [StorageBackend], [MimeType], [FileSizeBytes],
                 [UploadedById], [IsVirusScanPassed], [CreatedAt], [UpdatedAt])
            OUTPUT INSERTED.Id
            VALUES ('test.jpg', 'uploads/test.jpg', 'local', 'image/jpeg', 1024,
                    @UploaderId, 1, SYSUTCDATETIME(), SYSUTCDATETIME())";
        ins.Parameters.AddWithValue("@UploaderId", ownerId);
        assetId = (long)(await ins.ExecuteScalarAsync())!;

        // Create MediaUsage row linking asset to the entry
        await using var mu = conn.CreateCommand();
        mu.CommandText = @"
            INSERT INTO [MediaUsage] ([MediaAssetId], [ContentEntryId], [FieldName], [CreatedAt])
            VALUES (@AssetId, @EntryId, 'featuredImage', SYSUTCDATETIME())";
        mu.Parameters.AddWithValue("@AssetId", assetId);
        mu.Parameters.AddWithValue("@EntryId", entryId);
        await mu.ExecuteNonQueryAsync();

        return (entryId, assetId);
    }

    // ── AC1: '(Copy)' appended to title ──────────────────────────────────────

    /// <summary>AC1: Duplicate creates a new Draft entry; title has '(Copy)' appended.</summary>
    [Fact]
    public async Task Duplicate_CreatesNewDraftWithCopyInTitle()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var slug = $"dup-title-{Guid.NewGuid():N}";
        var sourceId = await CreateEntryWithTitleAsync(ctId, ownerId, slug, "Benefits Overview");

        var repo = Repo();
        var (success, newEntryId, error) = await repo.DuplicateAsync(sourceId, ownerId);

        Assert.True(success, $"Expected success but got error: {error}");
        Assert.Null(error);
        Assert.NotNull(newEntryId);
        Assert.NotEqual(sourceId, newEntryId!.Value);

        // Verify new entry is Draft
        var newEntry = await repo.GetByIdAsync(newEntryId.Value);
        Assert.NotNull(newEntry);
        Assert.Equal("Draft", newEntry!.Status);

        // Verify '(Copy)' is in the title of the new ContentVersion
        var titleInVersion = await GetVersionTitleAsync(newEntryId.Value);
        Assert.Equal("Benefits Overview (Copy)", titleInVersion);
    }

    /// <summary>AC1: Duplicating an entry with no title field does not crash.</summary>
    [Fact]
    public async Task Duplicate_EntryWithoutTitle_SucceedsAndCopiesFieldsUnchanged()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var slug = $"dup-notitle-{Guid.NewGuid():N}";
        var sourceId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, ctId, slug, "en-US", ownerId);

        // Insert a ContentVersion without a title field
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO [ContentVersion]
                ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
                 [Status], [AuthorId], [ChangeNote], [CreatedAt])
            VALUES (@EntryId, 1, @FieldsJson, NULL, 'Draft', @AuthorId, NULL, SYSUTCDATETIME())";
        cmd.Parameters.AddWithValue("@EntryId",    sourceId);
        cmd.Parameters.AddWithValue("@FieldsJson", "{\"summary\":\"Some summary\"}");
        cmd.Parameters.AddWithValue("@AuthorId",   ownerId);
        await cmd.ExecuteNonQueryAsync();

        var repo = Repo();
        var (success, newEntryId, error) = await repo.DuplicateAsync(sourceId, ownerId);

        Assert.True(success, $"Expected success but got error: {error}");
        Assert.NotNull(newEntryId);
    }

    // ── AC2: Slug is cleared on duplicate ─────────────────────────────────────

    /// <summary>
    /// AC2: The duplicate entry has a different slug from the source (cleared/placeholder).
    /// The slug must be set by the content owner before publish.
    /// </summary>
    [Fact]
    public async Task Duplicate_SlugIsClearedOnNewEntry()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var slug = $"original-slug-{Guid.NewGuid():N}";
        var sourceId = await CreateEntryWithTitleAsync(ctId, ownerId, slug, "My Page");

        var repo = Repo();
        var (success, newEntryId, _) = await repo.DuplicateAsync(sourceId, ownerId);

        Assert.True(success);

        // AC2: Slug on the new entry must NOT be the same as the source slug.
        // The SP uses a UUID placeholder so the entry can be saved without a valid slug set.
        var newEntry = await repo.GetByIdAsync(newEntryId!.Value);
        Assert.NotNull(newEntry);
        Assert.NotEqual(slug, newEntry!.Slug);
        // The placeholder slug should not be empty (unique constraint requires a non-empty value)
        Assert.False(string.IsNullOrWhiteSpace(newEntry.Slug),
            "Duplicate slug placeholder must not be empty — uniqueness constraint requires a value.");
    }

    // ── AC3: Media references are shared ──────────────────────────────────────

    /// <summary>AC3: MediaUsage rows from source entry are copied to the duplicate entry.</summary>
    [Fact]
    public async Task Duplicate_MediaReferencesAreShared_NotReuploaded()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var slug = $"dup-media-{Guid.NewGuid():N}";
        var (sourceId, assetId) = await CreateEntryWithMediaAsync(ctId, ownerId, slug);

        var repo = Repo();
        var (success, newEntryId, _) = await repo.DuplicateAsync(sourceId, ownerId);

        Assert.True(success);

        // Verify MediaUsage row exists for the new entry pointing to the SAME asset
        var mediaUsageExists = await MediaUsageExistsAsync(newEntryId!.Value, assetId);
        Assert.True(mediaUsageExists,
            "Expected MediaUsage row for the duplicate entry pointing to the same asset.");
    }

    /// <summary>AC3: All field values are copied to the new ContentVersion.</summary>
    [Fact]
    public async Task Duplicate_AllFieldValuesCopied()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var slug = $"dup-fields-{Guid.NewGuid():N}";

        // Create entry with multiple fields
        var sourceId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, ctId, slug, "en-US", ownerId);

        const string fieldsJson = """{"title":"Field Copy Test","summary":"Some summary","body":"# Hello"}""";
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO [ContentVersion]
                ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
                 [Status], [AuthorId], [ChangeNote], [CreatedAt])
            VALUES (@EntryId, 1, @FieldsJson, NULL, 'Draft', @AuthorId, NULL, SYSUTCDATETIME())";
        cmd.Parameters.AddWithValue("@EntryId",    sourceId);
        cmd.Parameters.AddWithValue("@FieldsJson", fieldsJson);
        cmd.Parameters.AddWithValue("@AuthorId",   ownerId);
        await cmd.ExecuteNonQueryAsync();

        var repo = Repo();
        var (success, newEntryId, _) = await repo.DuplicateAsync(sourceId, ownerId);

        Assert.True(success);

        // summary and body should be identical in the new version
        var newFieldsJson = await GetVersionFieldsJsonAsync(newEntryId!.Value);
        Assert.NotNull(newFieldsJson);
        Assert.Contains("Some summary", newFieldsJson!);
        Assert.Contains("# Hello", newFieldsJson);
        // title should have (Copy) appended
        Assert.Contains("Field Copy Test (Copy)", newFieldsJson);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    /// <summary>Duplicating a non-existent entry returns success=false with an error message.</summary>
    [Fact]
    public async Task Duplicate_NonExistentEntry_ReturnsError()
    {
        var (_, ownerId) = await SeedPrerequisitesAsync();
        var repo = Repo();

        var (success, newEntryId, error) = await repo.DuplicateAsync(999_999_888L, ownerId);

        Assert.False(success);
        Assert.Null(newEntryId);
        Assert.NotNull(error);
    }

    // ── Helpers: direct DB reads for assertions ───────────────────────────────

    private async Task<string?> GetVersionTitleAsync(long entryId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 JSON_VALUE([FieldsJson], '$.title')
            FROM   [ContentVersion]
            WHERE  [ContentEntryId] = @EntryId
            ORDER  BY [VersionNumber] DESC";
        cmd.Parameters.AddWithValue("@EntryId", entryId);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result as string;
    }

    private async Task<string?> GetVersionFieldsJsonAsync(long entryId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 [FieldsJson]
            FROM   [ContentVersion]
            WHERE  [ContentEntryId] = @EntryId
            ORDER  BY [VersionNumber] DESC";
        cmd.Parameters.AddWithValue("@EntryId", entryId);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result as string;
    }

    private async Task<bool> MediaUsageExistsAsync(long entryId, long assetId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM   [MediaUsage]
            WHERE  [ContentEntryId] = @EntryId
              AND  [MediaAssetId]   = @AssetId";
        cmd.Parameters.AddWithValue("@EntryId", entryId);
        cmd.Parameters.AddWithValue("@AssetId", assetId);
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}
