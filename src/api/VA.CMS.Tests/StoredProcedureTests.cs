using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

// ────────────────────────────────────────────────────────────────────────────
// Helpers shared across V008 SP tests
// ────────────────────────────────────────────────────────────────────────────

internal static class V008Seeder
{
    /// <summary>Ensure a NavigationMenu row and return its Id.</summary>
    public static async Task<long> EnsureMenuAsync(string connectionString, string handle)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var ins = conn.CreateCommand())
        {
            ins.CommandText = @"
                IF NOT EXISTS (SELECT 1 FROM [NavigationMenu] WHERE [Handle] = @Handle)
                INSERT INTO [NavigationMenu] ([Name],[Handle],[CreatedAt],[UpdatedAt])
                VALUES (@Handle, @Handle, SYSUTCDATETIME(), SYSUTCDATETIME());";
            ins.Parameters.AddWithValue("@Handle", handle);
            await ins.ExecuteNonQueryAsync();
        }
        await using var sel = conn.CreateCommand();
        sel.CommandText = "SELECT Id FROM [NavigationMenu] WHERE [Handle] = @Handle";
        sel.Parameters.AddWithValue("@Handle", handle);
        return (long)(await sel.ExecuteScalarAsync())!;
    }

    /// <summary>Ensure a Role row and return its Id.</summary>
    public static async Task<long> EnsureRoleAsync(string connectionString, string name)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var ins = conn.CreateCommand())
        {
            // Role table: Id, Name, DisplayName, IsSystemRole — no CreatedAt
            ins.CommandText = @"
                IF NOT EXISTS (SELECT 1 FROM [Role] WHERE [Name] = @Name)
                INSERT INTO [Role] ([Name],[DisplayName],[IsSystemRole]) VALUES (@Name, @Name, 0);";
            ins.Parameters.AddWithValue("@Name", name);
            await ins.ExecuteNonQueryAsync();
        }
        await using var sel = conn.CreateCommand();
        sel.CommandText = "SELECT Id FROM [Role] WHERE [Name] = @Name";
        sel.Parameters.AddWithValue("@Name", name);
        return (long)(await sel.ExecuteScalarAsync())!;
    }

    /// <summary>Insert a Webhook row directly and return its Id.</summary>
    public static async Task<long> InsertWebhookAsync(string connectionString,
        string url, string eventsJson)
    {
        // Need a real user as CreatedById; create a throwaway one
        var createdById = await TestSeeder.UpsertUserAsync(connectionString);
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Webhook: Id, Name, Url, Secret, EventsJson, IsActive, CreatedById, CreatedAt — no UpdatedAt
        cmd.CommandText = @"
            INSERT INTO [Webhook] ([Name],[Url],[Secret],[EventsJson],[IsActive],[CreatedById],[CreatedAt])
            VALUES (@Name, @Url, '', @EventsJson, 1, @CreatedById, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();";
        cmd.Parameters.AddWithValue("@Name", $"test-hook-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("@Url", url);
        cmd.Parameters.AddWithValue("@EventsJson", eventsJson);
        cmd.Parameters.AddWithValue("@CreatedById", createdById);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Ensure a Taxonomy row and return its Id.</summary>
    public static async Task<long> EnsureTaxonomyAsync(string connectionString, string handle)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var ins = conn.CreateCommand())
        {
            // Taxonomy table: Id, Name, Handle, IsHierarchical — no CreatedAt/UpdatedAt
            ins.CommandText = @"
                IF NOT EXISTS (SELECT 1 FROM [Taxonomy] WHERE [Handle] = @Handle)
                INSERT INTO [Taxonomy] ([Handle],[Name],[IsHierarchical])
                VALUES (@Handle, @Handle, 0);";
            ins.Parameters.AddWithValue("@Handle", handle);
            await ins.ExecuteNonQueryAsync();
        }
        await using var sel = conn.CreateCommand();
        sel.CommandText = "SELECT Id FROM [Taxonomy] WHERE [Handle] = @Handle";
        sel.Parameters.AddWithValue("@Handle", handle);
        return (long)(await sel.ExecuteScalarAsync())!;
    }

    /// <summary>Insert a TaxonomyTerm and return its Id.</summary>
    public static async Task<long> InsertTermAsync(string connectionString,
        long taxonomyId, string name, long? parentTermId = null)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // TaxonomyTerm: Id, TaxonomyId, ParentTermId, Name, Slug, SortOrder — no CreatedAt/UpdatedAt
        cmd.CommandText = @"
            INSERT INTO [TaxonomyTerm] ([TaxonomyId],[ParentTermId],[Name],[Slug],[SortOrder])
            VALUES (@TaxonomyId, @ParentTermId, @Name, @Slug, 0);
            SELECT SCOPE_IDENTITY();";
        cmd.Parameters.AddWithValue("@TaxonomyId", taxonomyId);
        cmd.Parameters.AddWithValue("@ParentTermId", (object?)parentTermId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@Slug", name.ToLower().Replace(" ", "-"));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.5 Workflow — usp_Workflow_Transition
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class WorkflowRepositoryTests(DatabaseFixture fixture)
{
    private async Task<(long EntryId, long VersionId, long ActorId)> SeedEntryAsync()
    {
        var actorId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "wf_type");
        var entryId = await TestSeeder.CreateEntryAsync(fixture.ConnectionString,
            ctId, $"wf-{Guid.NewGuid():N}", "en-US", actorId);

        // Create a version
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentVersion_Create @ContentEntryId, @FieldsJson, NULL, @Status, @AuthorId, NULL, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", entryId);
        cmd.Parameters.AddWithValue("@FieldsJson", "{}");
        cmd.Parameters.AddWithValue("@Status", "Draft");
        cmd.Parameters.AddWithValue("@AuthorId", actorId);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var versionId = (long)outParam.Value;

        return (entryId, versionId, actorId);
    }

    [Fact]
    public async Task TransitionAsync_LegalTransition_Succeeds()
    {
        var (entryId, versionId, actorId) = await SeedEntryAsync();
        var repo = new WorkflowRepository(fixture.CreateDb());

        var (success, error) = await repo.TransitionAsync(
            entryId, versionId, "Draft", "InReview", actorId);

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task TransitionAsync_IllegalTransition_ReturnsFalseWithError()
    {
        var (entryId, versionId, actorId) = await SeedEntryAsync();
        var repo = new WorkflowRepository(fixture.CreateDb());

        // Draft → Published is only allowed as admin bypass; Direct Draft → Archived is not allowed
        var (success, error) = await repo.TransitionAsync(
            entryId, versionId, "Draft", "Archived", actorId);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task TransitionAsync_ReturnToDraft_RequiresComment()
    {
        var (entryId, versionId, actorId) = await SeedEntryAsync();
        var repo = new WorkflowRepository(fixture.CreateDb());

        // Move to InReview first
        await repo.TransitionAsync(entryId, versionId, "Draft", "InReview", actorId);

        // Attempt return to Draft without comment — should fail
        var (success, error) = await repo.TransitionAsync(
            entryId, versionId, "InReview", "Draft", actorId, comment: null);

        Assert.False(success);
        Assert.Contains("comment", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransitionAsync_ReturnToDraft_WithComment_Succeeds()
    {
        var (entryId, versionId, actorId) = await SeedEntryAsync();
        var repo = new WorkflowRepository(fixture.CreateDb());

        await repo.TransitionAsync(entryId, versionId, "Draft", "InReview", actorId);

        var (success, error) = await repo.TransitionAsync(
            entryId, versionId, "InReview", "Draft", actorId, comment: "Needs revision");

        Assert.True(success);
        Assert.Null(error);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.6 Navigation — usp_Navigation_GetMenuTree, usp_Navigation_UpsertItem,
//                   usp_Redirect_GetByPath, usp_Redirect_Create
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class NavigationRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task UpsertItemAsync_Creates_NavigationItem()
    {
        var menuId = await V008Seeder.EnsureMenuAsync(fixture.ConnectionString, $"nav-{Guid.NewGuid():N}");
        var repo = new NavigationRepository(fixture.CreateDb());

        var newId = await repo.UpsertItemAsync(new NavigationItem
        {
            MenuId = menuId,
            Label = "Home",
            Url = "/",
            SortOrder = 0,
            IsVisible = true,
        });

        Assert.True(newId > 0);
    }

    [Fact]
    public async Task GetMenuTreeAsync_ReturnsItems_AfterInsert()
    {
        var handle = $"tree-{Guid.NewGuid():N}";
        var menuId = await V008Seeder.EnsureMenuAsync(fixture.ConnectionString, handle);
        var repo = new NavigationRepository(fixture.CreateDb());

        await repo.UpsertItemAsync(new NavigationItem
        {
            MenuId = menuId, Label = "About", Url = "/about",
            SortOrder = 1, IsVisible = true,
        });

        var tree = (await repo.GetMenuTreeAsync(handle)).ToList();

        Assert.NotEmpty(tree);
        Assert.Contains(tree, i => i.Label == "About");
    }

    [Fact]
    public async Task GetMenuTreeAsync_MaxDepth_IsThreeLevels()
    {
        // SP CTE uses `mt.Depth < 3` guard. The root is depth=0, so items up to depth=3
        // (4 levels) are included; items at depth=4 (whose parent is depth=3) are excluded.
        var handle = $"deep-{Guid.NewGuid():N}";
        var menuId = await V008Seeder.EnsureMenuAsync(fixture.ConnectionString, handle);
        var repo = new NavigationRepository(fixture.CreateDb());

        // level 0 (depth=0)
        var l0 = await repo.UpsertItemAsync(new NavigationItem
        { MenuId = menuId, Label = "L0", Url = "/l0", SortOrder = 0, IsVisible = true });

        // level 1 (depth=1)
        var l1 = await repo.UpsertItemAsync(new NavigationItem
        { MenuId = menuId, ParentItemId = l0, Label = "L1", Url = "/l1", SortOrder = 0, IsVisible = true });

        // level 2 (depth=2)
        var l2 = await repo.UpsertItemAsync(new NavigationItem
        { MenuId = menuId, ParentItemId = l1, Label = "L2", Url = "/l2", SortOrder = 0, IsVisible = true });

        // level 3 (depth=3) — included (parent depth 2 < 3)
        var l3 = await repo.UpsertItemAsync(new NavigationItem
        { MenuId = menuId, ParentItemId = l2, Label = "L3", Url = "/l3", SortOrder = 0, IsVisible = true });

        // level 4 (depth=4) — excluded (parent depth 3 is NOT < 3)
        await repo.UpsertItemAsync(new NavigationItem
        { MenuId = menuId, ParentItemId = l3, Label = "L4_Hidden", Url = "/l4", SortOrder = 0, IsVisible = true });

        var tree = (await repo.GetMenuTreeAsync(handle)).ToList();

        // L3 is present; L4_Hidden is excluded by the CTE guard
        Assert.Contains(tree, i => i.Label == "L3");
        Assert.DoesNotContain(tree, i => i.Label == "L4_Hidden");
    }

    [Fact]
    public async Task CreateRedirectAsync_ReturnsId()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new NavigationRepository(fixture.CreateDb());

        var id = await repo.CreateRedirectAsync(new Redirect
        {
            FromPath = $"/old-{Guid.NewGuid():N}",
            ToPath = "/new",
            StatusCode = 301,
            CreatedById = userId,
        });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetRedirectByPathAsync_ReturnsRedirect_AfterCreate()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new NavigationRepository(fixture.CreateDb());
        var fromPath = $"/redir-{Guid.NewGuid():N}";

        await repo.CreateRedirectAsync(new Redirect
        {
            FromPath = fromPath, ToPath = "/destination",
            StatusCode = 302, CreatedById = userId,
        });

        var redir = await repo.GetRedirectByPathAsync(fromPath);

        Assert.NotNull(redir);
        Assert.Equal("/destination", redir!.ToPath);
        Assert.Equal(302, redir.StatusCode);
    }

    [Fact]
    public async Task GetRedirectByPathAsync_ReturnsNull_ForUnknownPath()
    {
        var repo = new NavigationRepository(fixture.CreateDb());
        var result = await repo.GetRedirectByPathAsync($"/doesnotexist-{Guid.NewGuid():N}");
        Assert.Null(result);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.7 Search — usp_Search_FullText, usp_Search_LogQuery
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class SearchRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task LogQueryAsync_DoesNotThrow()
    {
        var repo = new SearchRepository(fixture.CreateDb());
        // Should not throw regardless of FTS availability
        await repo.LogQueryAsync("test query", 5, null);
    }

    [Fact]
    public async Task FullTextSearchAsync_ReturnsEmptyPageWhenFtsUnavailable_NotThrow()
    {
        // FTS may not be installed in the TestContainers SQL Server image.
        // The SP gracefully returns 0 rows. Verify no exception is thrown.
        var repo = new SearchRepository(fixture.CreateDb());
        var page = await repo.FullTextSearchAsync("content", page: 1, pageSize: 10);

        Assert.NotNull(page);
        Assert.True(page.TotalItems >= 0);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.8 Taxonomy — usp_Taxonomy_GetTermTree, usp_Taxonomy_GetEntriesForTerm
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class TaxonomyRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetTermTreeAsync_ReturnsTerms_AfterInsert()
    {
        var handle = $"tax-{Guid.NewGuid():N}";
        var taxId = await V008Seeder.EnsureTaxonomyAsync(fixture.ConnectionString, handle);
        await V008Seeder.InsertTermAsync(fixture.ConnectionString, taxId, "Category A");
        await V008Seeder.InsertTermAsync(fixture.ConnectionString, taxId, "Category B");

        var repo = new TaxonomyRepository(fixture.CreateDb());
        var terms = (await repo.GetTermTreeAsync(handle)).ToList();

        Assert.True(terms.Count >= 2);
        Assert.Contains(terms, t => t.Name == "Category A");
        Assert.Contains(terms, t => t.Name == "Category B");
    }

    [Fact]
    public async Task GetTermTreeAsync_ReturnsNestedTerms()
    {
        var handle = $"tax2-{Guid.NewGuid():N}";
        var taxId = await V008Seeder.EnsureTaxonomyAsync(fixture.ConnectionString, handle);
        var parentId = await V008Seeder.InsertTermAsync(fixture.ConnectionString, taxId, "Parent");
        await V008Seeder.InsertTermAsync(fixture.ConnectionString, taxId, "Child", parentId);

        var repo = new TaxonomyRepository(fixture.CreateDb());
        var terms = (await repo.GetTermTreeAsync(handle)).ToList();

        Assert.Contains(terms, t => t.Name == "Parent");
        Assert.Contains(terms, t => t.Name == "Child");
    }

    [Fact]
    public async Task GetEntriesForTermAsync_ReturnsEmpty_WithNoEntries()
    {
        var handle = $"tax3-{Guid.NewGuid():N}";
        var taxId = await V008Seeder.EnsureTaxonomyAsync(fixture.ConnectionString, handle);
        var termId = await V008Seeder.InsertTermAsync(fixture.ConnectionString, taxId, "Unused");

        var repo = new TaxonomyRepository(fixture.CreateDb());
        var entries = (await repo.GetEntriesForTermAsync(termId)).ToList();

        Assert.Empty(entries);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.9 Audit — Write+List already covered in RepositoryTests.cs
//              (AuditLogRepositoryTests). Add archive-safety check here.
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class AuditLogAppendOnlyTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task AuditLog_IsAppendOnly_CannotUpdateOrDeleteViaApp()
    {
        // This test documents the constraint: the application layer should never
        // UPDATE or DELETE AuditLog rows. We assert that the repo only exposes
        // Write and List, not any mutation methods.
        var repo = new AuditLogRepository(fixture.CreateDb());
        var repoType = repo.GetType();

        // Must have WriteAsync and ListAsync
        Assert.NotNull(repoType.GetMethod("WriteAsync"));
        Assert.NotNull(repoType.GetMethod("ListAsync"));

        // Must NOT expose DeleteAsync or UpdateAsync
        Assert.Null(repoType.GetMethod("DeleteAsync"));
        Assert.Null(repoType.GetMethod("UpdateAsync"));
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.10 Webhooks — usp_Webhook_GetActiveForEvent, usp_WebhookDelivery_Create
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class WebhookRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetActiveForEventAsync_ReturnsWebhooks_MatchingEvent()
    {
        var eventName = $"content.published";
        await V008Seeder.InsertWebhookAsync(
            fixture.ConnectionString,
            $"https://webhook-test-{Guid.NewGuid():N}.example.com/hook",
            $"[\"{eventName}\"]");

        var repo = new WebhookRepository(fixture.CreateDb());
        var hooks = (await repo.GetActiveForEventAsync(eventName)).ToList();

        Assert.NotEmpty(hooks);
        Assert.All(hooks, h => Assert.True(h.IsActive));
    }

    [Fact]
    public async Task GetActiveForEventAsync_ReturnsEmpty_ForUnknownEvent()
    {
        var repo = new WebhookRepository(fixture.CreateDb());
        var hooks = await repo.GetActiveForEventAsync($"no.such.event.{Guid.NewGuid():N}");
        Assert.Empty(hooks);
    }

    [Fact]
    public async Task CreateDeliveryAsync_ReturnsDeliveryId()
    {
        var hookId = await V008Seeder.InsertWebhookAsync(
            fixture.ConnectionString,
            $"https://delivery-{Guid.NewGuid():N}.example.com/hook",
            "[\"content.created\"]");

        var repo = new WebhookRepository(fixture.CreateDb());

        var id = await repo.CreateDeliveryAsync(new WebhookDelivery
        {
            WebhookId = hookId,
            EventName = "content.created",
            PayloadJson = @"{""entryId"":1}",
            ResponseStatusCode = 200,
            AttemptNumber = 1,
        });

        Assert.True(id > 0);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.4 Users & Auth (extended) — GetRoles, AssignRole, RevokeRole,
//                                Deactivate, List
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class UserRoleRepositoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task AssignRoleAsync_Then_GetRolesAsync_ReturnsRole()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var granterId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var roleId = await V008Seeder.EnsureRoleAsync(fixture.ConnectionString, $"Editor-{Guid.NewGuid():N}");

        var repo = new UserRoleRepository(fixture.CreateDb());
        await repo.AssignRoleAsync(userId, roleId, granterId);

        var roles = (await repo.GetRolesAsync(userId)).ToList();
        Assert.Contains(roles, r => r.RoleId == roleId);
    }

    [Fact]
    public async Task RevokeRoleAsync_RemovesRole()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var granterId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var roleId = await V008Seeder.EnsureRoleAsync(fixture.ConnectionString, $"Reviewer-{Guid.NewGuid():N}");

        var repo = new UserRoleRepository(fixture.CreateDb());
        await repo.AssignRoleAsync(userId, roleId, granterId);
        await repo.RevokeRoleAsync(userId, roleId);

        var roles = (await repo.GetRolesAsync(userId)).ToList();
        Assert.DoesNotContain(roles, r => r.RoleId == roleId);
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var actorId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new UserRoleRepository(fixture.CreateDb());

        await repo.DeactivateAsync(userId, actorId);

        // Read back and verify
        var userRepo = new UserRepository(fixture.CreateDb());
        var user = await userRepo.GetByIdAsync(userId);
        Assert.NotNull(user);
        Assert.False(user!.IsActive);
    }

    [Fact]
    public async Task ListAsync_ReturnsActiveUsers()
    {
        // Create a fresh user we know is active, then verify it appears in the list
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new UserRoleRepository(fixture.CreateDb());

        var users = (await repo.ListAsync(isActive: true, page: 1, pageSize: 200)).ToList();
        Assert.NotEmpty(users);
        // Our freshly-created user must appear; it's active
        Assert.Contains(users, u => u.Id == userId && u.IsActive);
    }

    [Fact]
    public async Task ListAsync_SearchTerm_FiltersResults()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var email = $"searchable-{uid}@test.va.gov";
        await TestSeeder.UpsertUserAsync(fixture.ConnectionString,
            email: email, displayName: $"SearchableUser_{uid}");

        var repo = new UserRoleRepository(fixture.CreateDb());
        var users = (await repo.ListAsync(searchTerm: uid, page: 1, pageSize: 50)).ToList();

        Assert.Contains(users, u => u.Email == email);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// §4.3 Media (extended) — SetVirusScanResult, SafeDelete, GetUsage,
//                          MediaUsage_Upsert, MediaUsage_DeleteForEntry
// ────────────────────────────────────────────────────────────────────────────

[Collection("Database")]
public class MediaExtendedRepositoryTests(DatabaseFixture fixture)
{
    private async Task<long> CreateAssetAsync()
    {
        var uploaderId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo = new MediaAssetRepository(fixture.CreateDb());
        return await repo.CreateAsync(new MediaAsset
        {
            FileName = $"test-{Guid.NewGuid():N}.jpg",
            StoragePath = $"/uploads/{Guid.NewGuid():N}.jpg",
            StorageBackend = "local",
            MimeType = "image/jpeg",
            FileSizeBytes = 102400,
            UploadedById = uploaderId,
        });
    }

    private async Task<long> CreateEntryAsync()
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "media_ext_type");
        return await TestSeeder.CreateEntryAsync(fixture.ConnectionString,
            ctId, $"media-{Guid.NewGuid():N}", "en-US", ownerId);
    }

    [Fact]
    public async Task SetVirusScanResultAsync_SetsPassed()
    {
        var assetId = await CreateAssetAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        await repo.SetVirusScanResultAsync(assetId, passed: true);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IsVirusScanPassed FROM [MediaAsset] WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", assetId);
        var val = (bool?)(await cmd.ExecuteScalarAsync());
        Assert.True(val);
    }

    [Fact]
    public async Task SetVirusScanResultAsync_SetsFailed()
    {
        var assetId = await CreateAssetAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        await repo.SetVirusScanResultAsync(assetId, passed: false);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IsVirusScanPassed FROM [MediaAsset] WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", assetId);
        var val = (bool?)(await cmd.ExecuteScalarAsync());
        Assert.False(val);
    }

    [Fact]
    public async Task SafeDeleteAsync_DeletesAsset_WhenNotInUse()
    {
        var assetId = await CreateAssetAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        var result = await repo.SafeDeleteAsync(assetId);

        Assert.Equal(0, result); // 0 = deleted
    }

    [Fact]
    public async Task SafeDeleteAsync_BlocksDelete_WhenInUse()
    {
        var assetId = await CreateAssetAsync();
        var entryId = await CreateEntryAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        await repo.UpsertUsageAsync(assetId, entryId, "heroImage");
        var result = await repo.SafeDeleteAsync(assetId);

        Assert.Equal(1, result); // 1 = blocked
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUsage_AfterUpsert()
    {
        var assetId = await CreateAssetAsync();
        var entryId = await CreateEntryAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        await repo.UpsertUsageAsync(assetId, entryId, "bodyImage");

        var usage = (await repo.GetUsageAsync(assetId)).ToList();
        Assert.Contains(usage, u => u.ContentEntryId == entryId && u.FieldName == "bodyImage");
    }

    [Fact]
    public async Task UpsertUsageAsync_IsIdempotent()
    {
        var assetId = await CreateAssetAsync();
        var entryId = await CreateEntryAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        // Call twice — should not throw and should not create duplicates
        await repo.UpsertUsageAsync(assetId, entryId, "cover");
        await repo.UpsertUsageAsync(assetId, entryId, "cover");

        var usage = (await repo.GetUsageAsync(assetId))
            .Where(u => u.ContentEntryId == entryId && u.FieldName == "cover")
            .ToList();
        Assert.Single(usage);
    }

    [Fact]
    public async Task DeleteUsageForEntryAsync_RemovesAllUsageForEntry()
    {
        var asset1 = await CreateAssetAsync();
        var asset2 = await CreateAssetAsync();
        var entryId = await CreateEntryAsync();
        var repo = new MediaExtendedRepository(fixture.CreateDb());

        await repo.UpsertUsageAsync(asset1, entryId, "img1");
        await repo.UpsertUsageAsync(asset2, entryId, "img2");

        await repo.DeleteUsageForEntryAsync(entryId);

        var u1 = (await repo.GetUsageAsync(asset1)).Where(u => u.ContentEntryId == entryId);
        var u2 = (await repo.GetUsageAsync(asset2)).Where(u => u.ContentEntryId == entryId);
        Assert.Empty(u1);
        Assert.Empty(u2);
    }
}
