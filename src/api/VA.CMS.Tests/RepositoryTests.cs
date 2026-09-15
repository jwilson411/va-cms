using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace VA.CMS.Tests;

/// <summary>
/// Helper: seed test data via raw ADO.NET (bypasses PetaPoco's @N param scanner
/// which conflicts with OUTPUT parameters in EXEC statements).
/// </summary>
internal static class TestSeeder
{
    /// <summary>
    /// Upsert a user and return the database ID. Uses a unique external ID per call.
    /// </summary>
    public static async Task<long> UpsertUserAsync(string connectionString,
        string? externalId = null, string? email = null, string? displayName = null)
    {
        var uid = Guid.NewGuid().ToString("N");
        externalId ??= $"ext-{uid}";
        email ??= $"user-{uid}@test.va.gov";
        displayName ??= $"User {uid}";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_User_Upsert @ExternalId, @Email, @DisplayName, @UserId OUTPUT";
        cmd.Parameters.AddWithValue("@ExternalId", externalId);
        cmd.Parameters.AddWithValue("@Email", email);
        cmd.Parameters.AddWithValue("@DisplayName", displayName);
        var outParam = cmd.Parameters.Add("@UserId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    public static async Task<long> EnsureContentTypeAsync(string connectionString, string name)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = @"
                IF NOT EXISTS (SELECT 1 FROM [ContentType] WHERE [Name] = @Name)
                INSERT INTO [ContentType] ([Name],[DisplayName],[IsSystemType],[AllowWorkflow],[FieldSchemaJson],[CreatedAt],[UpdatedAt])
                VALUES (@Name, @Name, 0, 1, '[]', SYSUTCDATETIME(), SYSUTCDATETIME());";
            insertCmd.Parameters.AddWithValue("@Name", name);
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using var selCmd = conn.CreateCommand();
        selCmd.CommandText = "SELECT Id FROM [ContentType] WHERE [Name] = @Name";
        selCmd.Parameters.AddWithValue("@Name", name);
        return (long)(await selCmd.ExecuteScalarAsync())!;
    }

    public static async Task<long> CreateEntryAsync(string connectionString,
        long contentTypeId, string slug, string locale, long ownerId)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_Create @ContentTypeId, @Slug, @Locale, @OwnerId, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentTypeId", contentTypeId);
        cmd.Parameters.AddWithValue("@Slug", slug);
        cmd.Parameters.AddWithValue("@Locale", locale);
        cmd.Parameters.AddWithValue("@OwnerId", ownerId);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }
}

[Collection("Database")]
public class ContentEntryRepositoryTests(DatabaseFixture fixture)
{
    private IContentEntryRepository Repo() => new ContentEntryRepository(fixture.CreateDb());

    private async Task<(long ContentTypeId, long OwnerId)> SeedPrerequisitesAsync()
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        // One shared content type is fine — idempotent via IF NOT EXISTS
        var ctId = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "ce_type");
        return (ctId, ownerId);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNewId()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo = Repo();

        var id = await repo.CreateAsync(new ContentEntry
        {
            ContentTypeId = ctId,
            Slug = $"create-{Guid.NewGuid():N}",
            Locale = "en-US",
            OwnerId = ownerId,
        });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntry_AfterCreate()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo = Repo();
        var slug = $"getbyid-{Guid.NewGuid():N}";

        var id = await repo.CreateAsync(new ContentEntry
        {
            ContentTypeId = ctId, Slug = slug, Locale = "en-US", OwnerId = ownerId
        });

        var loaded = await repo.GetByIdAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(id, loaded!.Id);
        Assert.Equal("Draft", loaded.Status);
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_ForDraft()
    {
        // usp_ContentEntry_GetBySlug JOINs ContentVersion ON PublishedVersionId
        // A Draft entry has NULL PublishedVersionId → INNER JOIN excludes it → returns NULL
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo = Repo();
        var slug = $"draft-slug-{Guid.NewGuid():N}";

        await repo.CreateAsync(new ContentEntry
        {
            ContentTypeId = ctId, Slug = slug, Locale = "en-US", OwnerId = ownerId
        });

        var result = await repo.GetBySlugAsync(slug);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsPaginatedEntries()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo = Repo();

        // Create 3 entries with unique slugs
        var slugs = Enumerable.Range(0, 3).Select(_ => $"list-{Guid.NewGuid():N}").ToList();
        foreach (var slug in slugs)
        {
            await repo.CreateAsync(new ContentEntry
            {
                ContentTypeId = ctId, Slug = slug, Locale = "en-US", OwnerId = ownerId,
            });
        }

        var page = await repo.ListAsync(page: 1, pageSize: 100, status: "Draft");

        // All created slugs appear in the list (DB may have other entries from other tests)
        var returnedSlugs = page.Items.Select(e => e.Slug).ToHashSet();
        Assert.All(slugs, s => Assert.Contains(s, returnedSlugs));
    }

    [Fact]
    public async Task UpdateAsync_ChangesStatus()
    {
        var (ctId, ownerId) = await SeedPrerequisitesAsync();
        var repo = Repo();
        var slug = $"update-{Guid.NewGuid():N}";

        var id = await repo.CreateAsync(new ContentEntry
        {
            ContentTypeId = ctId, Slug = slug, Locale = "en-US", OwnerId = ownerId
        });

        var entry = await repo.GetByIdAsync(id);
        Assert.NotNull(entry);
        entry!.Status = "InReview";
        await repo.UpdateAsync(entry);

        var updated = await repo.GetByIdAsync(id);
        Assert.Equal("InReview", updated?.Status);
    }
}

[Collection("Database")]
public class ContentVersionRepositoryTests(DatabaseFixture fixture)
{
    private async Task<(long ContentTypeId, long OwnerId, long EntryId)> SeedEntryAsync()
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "cv_type");
        var entryId = await TestSeeder.CreateEntryAsync(fixture.ConnectionString,
            ctId, $"cv-{Guid.NewGuid():N}", "en-US", ownerId);
        return (ctId, ownerId, entryId);
    }

    [Fact]
    public async Task CreateAsync_ReturnsVersionId()
    {
        var (_, ownerId, entryId) = await SeedEntryAsync();
        var repo = new ContentVersionRepository(fixture.CreateDb());

        var id = await repo.CreateAsync(new ContentVersion
        {
            ContentEntryId = entryId,
            FieldsJson = @"{""title"":""Hello""}",
            Status = "Draft",
            AuthorId = ownerId,
        });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsVersion_AfterCreate()
    {
        var (_, ownerId, entryId) = await SeedEntryAsync();
        var repo = new ContentVersionRepository(fixture.CreateDb());

        var id = await repo.CreateAsync(new ContentVersion
        {
            ContentEntryId = entryId,
            FieldsJson = @"{""body"":""Test""}",
            Status = "Draft",
            AuthorId = ownerId,
            ChangeNote = "Test note",
        });

        var loaded = await repo.GetByIdAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(id, loaded!.Id);
        Assert.Equal("Test note", loaded.ChangeNote);
    }

    [Fact]
    public async Task ListAsync_ReturnsPaginatedVersions()
    {
        var (_, ownerId, entryId) = await SeedEntryAsync();
        var repo = new ContentVersionRepository(fixture.CreateDb());

        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await repo.CreateAsync(new ContentVersion
            {
                ContentEntryId = entryId, FieldsJson = "{}", Status = "Draft", AuthorId = ownerId
            }));
        }

        var page = await repo.ListAsync(entryId, page: 1, pageSize: 50);
        Assert.True(page.Items.Count >= 3);
        var returnedIds = page.Items.Select(v => v.Id).ToHashSet();
        Assert.All(ids, id => Assert.Contains(id, returnedIds));
    }
}

[Collection("Database")]
public class MediaAssetRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task CreateAsync_ReturnsAssetId()
    {
        var uploaderId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new MediaAssetRepository(fixture.CreateDb());

        var id = await repo.CreateAsync(new MediaAsset
        {
            FileName = "test-image.jpg",
            StoragePath = $"/uploads/{Guid.NewGuid():N}.jpg",
            StorageBackend = "local",
            MimeType = "image/jpeg",
            FileSizeBytes = 204800,
            Width = 1920,
            Height = 1080,
            UploadedById = uploaderId,
        });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesMetadata()
    {
        var uploaderId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new MediaAssetRepository(fixture.CreateDb());

        var id = await repo.CreateAsync(new MediaAsset
        {
            FileName = "meta-test.png",
            StoragePath = $"/uploads/{Guid.NewGuid():N}.png",
            StorageBackend = "local",
            MimeType = "image/png",
            FileSizeBytes = 1024,
            UploadedById = uploaderId,
        });

        await repo.UpdateAsync(new MediaAsset
        {
            Id = id,
            AltText = "A descriptive alt text",
            Title = "Test Title",
        });

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT AltText FROM [MediaAsset] WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        var altText = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("A descriptive alt text", altText);
    }
}

[Collection("Database")]
public class UserRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task UpsertAsync_CreatesUser_OnFirstCall()
    {
        var repo = new UserRepository(fixture.CreateDb());
        var extId = $"ext-{Guid.NewGuid():N}";

        var id = await repo.UpsertAsync(extId, $"{Guid.NewGuid():N}@test.va.gov", "New User");

        Assert.True(id > 0);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesUser_OnSecondCall_ReturnsSameId()
    {
        var repo = new UserRepository(fixture.CreateDb());
        var extId = $"ext-{Guid.NewGuid():N}";
        var email = $"{Guid.NewGuid():N}@test.va.gov";

        var id1 = await repo.UpsertAsync(extId, email, "Original Name");
        var id2 = await repo.UpsertAsync(extId, email, "Updated Name");

        Assert.Equal(id1, id2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUser()
    {
        var email = $"{Guid.NewGuid():N}@test.va.gov";
        var id = await TestSeeder.UpsertUserAsync(fixture.ConnectionString,
            email: email, displayName: "GetById User");

        var repo = new UserRepository(fixture.CreateDb());
        var user = await repo.GetByIdAsync(id);

        Assert.NotNull(user);
        Assert.Equal(email, user!.Email);
    }
}

[Collection("Database")]
public class AuditLogRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task WriteAsync_DoesNotThrow()
    {
        var actorId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new AuditLogRepository(fixture.CreateDb());

        // AuditLog is append-only — must not throw
        await repo.WriteAsync(actorId, "ContentEntry", 999999, "TestWrite", null);
    }

    [Fact]
    public async Task ListAsync_ReturnsRecentEntries()
    {
        var actorId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new AuditLogRepository(fixture.CreateDb());

        await repo.WriteAsync(actorId, "ContentEntry", 888888, "TestList", null);

        var entries = await repo.ListAsync(actorId: actorId, page: 1, pageSize: 50);
        Assert.NotEmpty(entries);
    }
}
