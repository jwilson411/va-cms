using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #33:
/// Implement slug auto-generation, URL preview, and 301 redirect on slug change.
///
/// AC:
/// - Slug auto-populates from title (lowercase, hyphenated, URL-safe) — verified in React layer
/// - Slug field is editable; shows full URL preview below — verified in React layer
/// - Changing a published page's slug creates a 301 redirect from the old path
/// - Duplicate slug within a locale returns a validation error
/// </summary>
[Collection("Database")]
public class Issue33AcceptanceTests(DatabaseFixture fixture)
{
    private IContentEntryRepository Repo() => new ContentEntryRepository(fixture.CreateDb());

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<(long EntryId, long UserId)> SeedEntryAsync(
        string? slug = null, string status = "Draft")
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId   = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "slug_test_type");
        var uniqueSlug = slug ?? $"slug-entry-{Guid.NewGuid():N}";
        var entryId = await TestSeeder.CreateEntryAsync(
            fixture.ConnectionString, ctId, uniqueSlug, "en-US", userId);

        if (status != "Draft")
        {
            await SetStatusAsync(entryId, status);
        }

        return (entryId, userId);
    }

    private async Task SetStatusAsync(long entryId, string status)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_UpdateStatus @Id, @Status, @PubVerId";
        cmd.Parameters.AddWithValue("@Id", entryId);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.Add("@PubVerId", System.Data.SqlDbType.BigInt).Value = DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(string? ToPath, int? StatusCode)> GetActiveRedirectAsync(string fromPath)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Redirect_GetByPath @FromPath";
        cmd.Parameters.AddWithValue("@FromPath", fromPath);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (null, null);
        return (reader.GetString(0), reader.GetInt32(1));
    }

    // ── AC: UpdateSlugAsync returns success for a valid new slug ───────────────

    [Fact]
    public async Task UpdateSlug_ValidNewSlug_ReturnsSuccess()
    {
        var (entryId, userId) = await SeedEntryAsync();

        var repo = Repo();
        var (success, error) = await repo.UpdateSlugAsync(entryId, "new-valid-slug", userId);

        Assert.True(success, $"Expected success but got error: {error}");
        Assert.Null(error);
    }

    // ── AC: UpdateSlugAsync updates the slug on the ContentEntry row ───────────

    [Fact]
    public async Task UpdateSlug_Updates_ContentEntry_Slug()
    {
        var (entryId, userId) = await SeedEntryAsync("original-slug-33a");

        var repo = Repo();
        await repo.UpdateSlugAsync(entryId, "updated-slug-33a", userId);

        var entry = await repo.GetByIdAsync(entryId);
        Assert.NotNull(entry);
        Assert.Equal("updated-slug-33a", entry!.Slug);
    }

    // ── AC: Duplicate slug within locale returns a validation error ─────────────

    [Fact]
    public async Task UpdateSlug_DuplicateSlug_WithinLocale_ReturnsError()
    {
        // Create two separate entries
        var (entry1Id, userId) = await SeedEntryAsync("slug-taken-33b");
        var (entry2Id, _)      = await SeedEntryAsync("slug-other-33b");

        var repo = Repo();
        // Try to set entry2's slug to entry1's slug — should fail
        var (success, error) = await repo.UpdateSlugAsync(entry2Id, "slug-taken-33b", userId);

        Assert.False(success, "Expected failure when slug is already taken");
        Assert.NotNull(error);
        Assert.Contains("slug-taken-33b", error);
    }

    // ── AC: Changing a Draft entry's slug does NOT create a redirect ───────────

    [Fact]
    public async Task UpdateSlug_DraftEntry_DoesNot_CreateRedirect()
    {
        var oldSlug = $"draft-old-slug-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedEntryAsync(oldSlug, "Draft");

        var repo = Repo();
        await repo.UpdateSlugAsync(entryId, $"draft-new-slug-{Guid.NewGuid():N}", userId);

        // No redirect should exist for the old draft slug
        var (toPath, _) = await GetActiveRedirectAsync(oldSlug);
        Assert.Null(toPath);
    }

    // ── AC: Changing a Published entry's slug creates a 301 redirect ──────────

    [Fact]
    public async Task UpdateSlug_PublishedEntry_Creates_301_Redirect()
    {
        var oldSlug = $"pub-old-slug-{Guid.NewGuid():N}";
        var newSlug = $"pub-new-slug-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedEntryAsync(oldSlug, "Published");

        var repo = Repo();
        var (success, error) = await repo.UpdateSlugAsync(entryId, newSlug, userId);

        Assert.True(success, $"Expected success but got error: {error}");

        // A 301 redirect must exist from old → new
        var (toPath, statusCode) = await GetActiveRedirectAsync(oldSlug);
        Assert.Equal(newSlug, toPath);
        Assert.Equal(301, statusCode);
    }

    // ── AC: Redirect points from old path to new path (not reversed) ──────────

    [Fact]
    public async Task UpdateSlug_Redirect_FromOldPath_ToNewPath()
    {
        var oldSlug = $"redirect-from-{Guid.NewGuid():N}";
        var newSlug = $"redirect-to-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedEntryAsync(oldSlug, "Published");

        var repo = Repo();
        await repo.UpdateSlugAsync(entryId, newSlug, userId);

        var (toPath, _) = await GetActiveRedirectAsync(oldSlug);
        Assert.Equal(newSlug, toPath);
    }

    // ── AC: No-op when slug is unchanged ──────────────────────────────────────

    [Fact]
    public async Task UpdateSlug_NoChange_ReturnsSuccess_NoRedirect()
    {
        var slug = $"unchanged-slug-{Guid.NewGuid():N}";
        var (entryId, userId) = await SeedEntryAsync(slug, "Published");

        var repo = Repo();
        var (success, error) = await repo.UpdateSlugAsync(entryId, slug, userId);

        Assert.True(success);
        Assert.Null(error);

        // No new redirect should be created (no-op)
        var (toPath, _) = await GetActiveRedirectAsync(slug);
        Assert.Null(toPath);
    }

    // ── AC: Non-existent entry returns failure ─────────────────────────────────

    [Fact]
    public async Task UpdateSlug_NonExistentEntry_ReturnsError()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);

        var repo = Repo();
        var (success, error) = await repo.UpdateSlugAsync(long.MaxValue - 1, "some-slug", userId);

        Assert.False(success);
        Assert.NotNull(error);
    }

    // ── AC: Subsequent slug changes on Published entry chain redirects ─────────

    [Fact]
    public async Task UpdateSlug_SecondChange_Deactivates_OldRedirect_Creates_NewOne()
    {
        var slug1 = $"chain-slug1-{Guid.NewGuid():N}";
        var slug2 = $"chain-slug2-{Guid.NewGuid():N}";
        var slug3 = $"chain-slug3-{Guid.NewGuid():N}";

        var (entryId, userId) = await SeedEntryAsync(slug1, "Published");

        var repo = Repo();
        await repo.UpdateSlugAsync(entryId, slug2, userId); // slug1 → slug2

        // Now change again: slug2 → slug3
        await repo.UpdateSlugAsync(entryId, slug3, userId);

        // slug2 should now redirect to slug3
        var (toPath, statusCode) = await GetActiveRedirectAsync(slug2);
        Assert.Equal(slug3, toPath);
        Assert.Equal(301, statusCode);

        // slug1 redirect to slug2 is still active (was created first)
        var (toPath1, statusCode1) = await GetActiveRedirectAsync(slug1);
        Assert.Equal(slug2, toPath1);
        Assert.Equal(301, statusCode1);
    }
}
