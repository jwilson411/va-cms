using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #32:
/// Implement content versioning: create version on every save, list versions, restore.
///
/// AC:
/// - Every save (draft or publish) creates a ContentVersion row
/// - Version list screen shows: version number, author, date, change note, status
/// - 'Restore this version' button sets the current draft to the selected version's fields
/// - Restoring creates a new version (does not delete history)
/// </summary>
[Collection("Database")]
public class Issue32AcceptanceTests(DatabaseFixture fixture)
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private IContentVersionRepository VersionRepo()
        => new ContentVersionRepository(fixture.CreateDb());

    private async Task<(long EntryId, long UserId)> SeedEntryAsync(string? slug = null)
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId   = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "ver_test_type");
        var uniqueSlug = slug ?? $"ver-entry-{Guid.NewGuid():N}";
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, ctId, uniqueSlug, "en-US", userId);
        return (entryId, userId);
    }

    private async Task<long> CreateVersionAsync(long entryId, long authorId,
        string fieldsJson = "{\"title\":\"Hello\"}", string status = "Draft",
        string? changeNote = null)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentVersion_Create @ContentEntryId, @FieldsJson, NULL, @Status, @AuthorId, @ChangeNote, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", entryId);
        cmd.Parameters.AddWithValue("@FieldsJson", fieldsJson);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@AuthorId", authorId);
        cmd.Parameters.Add("@ChangeNote", System.Data.SqlDbType.NVarChar, 1000).Value =
            (object?)changeNote ?? DBNull.Value;
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    // ── AC: Every save creates a ContentVersion row ───────────────────────────

    [Fact]
    public async Task SaveEntry_Creates_ContentVersion_Row()
    {
        var (entryId, userId) = await SeedEntryAsync();

        var versionId = await CreateVersionAsync(entryId, userId,
            fieldsJson: "{\"title\":\"Initial draft\"}",
            status: "Draft",
            changeNote: "Initial save");

        Assert.True(versionId > 0, "usp_ContentVersion_Create must return a positive id");

        // Verify the row exists
        var repo    = VersionRepo();
        var version = await repo.GetByIdAsync(versionId);
        Assert.NotNull(version);
        Assert.Equal(entryId, version!.ContentEntryId);
        Assert.Equal("Draft", version.Status);
    }

    [Fact]
    public async Task MultiSaves_Increment_VersionNumber()
    {
        var (entryId, userId) = await SeedEntryAsync();

        var v1 = await CreateVersionAsync(entryId, userId, "{\"title\":\"v1\"}");
        var v2 = await CreateVersionAsync(entryId, userId, "{\"title\":\"v2\"}");
        var v3 = await CreateVersionAsync(entryId, userId, "{\"title\":\"v3\"}");

        var repo     = VersionRepo();
        var versions = await repo.ListWithAuthorAsync(entryId, page: 1, pageSize: 25);

        Assert.True(versions.Count >= 3, "Should have at least 3 versions");

        // Newest first
        var vNums = versions.Select(v => v.VersionNumber).ToList();
        for (int i = 1; i < vNums.Count; i++)
            Assert.True(vNums[i - 1] >= vNums[i],
                $"Expected descending version numbers, got {vNums[i-1]} < {vNums[i]}");
    }

    // ── AC: Version list shows required columns ────────────────────────────────

    [Fact]
    public async Task VersionList_Returns_RequiredColumns()
    {
        var (entryId, userId) = await SeedEntryAsync();
        await CreateVersionAsync(entryId, userId,
            fieldsJson: "{\"title\":\"My Content\"}",
            status: "Draft",
            changeNote: "Reviewed by editor");

        var repo     = VersionRepo();
        var versions = await repo.ListWithAuthorAsync(entryId, page: 1, pageSize: 25);

        Assert.NotEmpty(versions);
        var v = versions.First();

        Assert.True(v.Id > 0,                      "Id must be positive");
        Assert.True(v.VersionNumber >= 1,           "VersionNumber must be >= 1");
        Assert.False(string.IsNullOrEmpty(v.AuthorName), "AuthorName must be populated");
        Assert.True(v.CreatedAt > DateTime.MinValue,"CreatedAt must be a real timestamp");
        Assert.False(string.IsNullOrEmpty(v.Status),"Status must be populated");
        // ChangeNote may be null for saves without a note — just verify it's accessible
        // (not throwing)
    }

    [Fact]
    public async Task VersionList_ShowsChangeNote_When_Provided()
    {
        var (entryId, userId) = await SeedEntryAsync();
        var note = $"Changed title — {Guid.NewGuid():N}";
        await CreateVersionAsync(entryId, userId, changeNote: note);

        var repo     = VersionRepo();
        var versions = await repo.ListWithAuthorAsync(entryId, page: 1, pageSize: 25);

        var row = versions.FirstOrDefault(v => v.ChangeNote == note);
        Assert.NotNull(row);
        Assert.Equal(note, row!.ChangeNote);
    }

    // ── AC: Restore sets current draft fields to selected version's fields ─────

    [Fact]
    public async Task Restore_Creates_NewVersion_With_TargetFieldsJson()
    {
        var (entryId, userId) = await SeedEntryAsync();
        var originalJson = "{\"title\":\"Original version\",\"body\":\"The original body\"}";
        var v1Id = await CreateVersionAsync(entryId, userId, originalJson, "Draft");

        // Make a newer version with different content
        await CreateVersionAsync(entryId, userId, "{\"title\":\"Modified version\"}", "Draft");

        var repo        = VersionRepo();
        var newVersionId = await repo.RestoreAsync(entryId, v1Id, userId);

        Assert.True(newVersionId > 0, "Restore must return a positive new version id");

        // New version should carry the original fields
        var restored = await repo.GetByIdAsync(newVersionId);
        Assert.NotNull(restored);
        Assert.Equal(originalJson, restored!.FieldsJson);
    }

    // ── AC: Restoring creates a new version — does not delete history ──────────

    [Fact]
    public async Task Restore_DoesNot_Delete_ExistingVersions()
    {
        var (entryId, userId) = await SeedEntryAsync();

        var v1Id = await CreateVersionAsync(entryId, userId, "{\"title\":\"v1\"}");
        var v2Id = await CreateVersionAsync(entryId, userId, "{\"title\":\"v2\"}");

        var repo = VersionRepo();
        await repo.RestoreAsync(entryId, v1Id, userId);

        // All three versions must still exist
        var versions = await repo.ListWithAuthorAsync(entryId, page: 1, pageSize: 100);

        Assert.True(versions.Count >= 3, $"Expected >= 3 versions after restore, got {versions.Count}");
        Assert.Contains(versions, v => v.Id == v1Id);
        Assert.Contains(versions, v => v.Id == v2Id);
    }

    [Fact]
    public async Task Restore_IncrementVersionNumber_Beyond_Existing()
    {
        var (entryId, userId) = await SeedEntryAsync();

        var v1Id = await CreateVersionAsync(entryId, userId, "{\"title\":\"v1\"}");
        await CreateVersionAsync(entryId, userId, "{\"title\":\"v2\"}");

        var repo         = VersionRepo();
        var before       = await repo.ListWithAuthorAsync(entryId, page: 1, pageSize: 100);
        var maxBefore    = before.Max(v => v.VersionNumber);

        var newVersionId = await repo.RestoreAsync(entryId, v1Id, userId);
        var restored     = await repo.GetByIdAsync(newVersionId);

        Assert.NotNull(restored);
        Assert.True(restored!.VersionNumber > maxBefore,
            $"Restored version number {restored.VersionNumber} should be > {maxBefore}");
    }

    // ── AC: GetByIdWithAuthor returns correct data ─────────────────────────────

    [Fact]
    public async Task GetByIdWithAuthor_Returns_Version_With_AuthorName()
    {
        var (entryId, userId) = await SeedEntryAsync();
        var versionId = await CreateVersionAsync(entryId, userId, "{\"title\":\"Test\"}");

        var repo    = VersionRepo();
        var version = await repo.GetByIdWithAuthorAsync(versionId);

        Assert.NotNull(version);
        Assert.Equal(versionId, version!.Id);
        Assert.False(string.IsNullOrEmpty(version.AuthorName),
            "AuthorName must be populated via the JOIN in usp_ContentVersion_GetById");
    }

    [Fact]
    public async Task GetByIdWithAuthor_Returns_Null_For_NonExistentId()
    {
        var repo    = VersionRepo();
        var version = await repo.GetByIdWithAuthorAsync(long.MaxValue);

        Assert.Null(version);
    }
}
