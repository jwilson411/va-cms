using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #52 — Implement pinned search results management.
///
/// BRD FR-SEARCH-04.
///
/// Acceptance criteria:
///   AC1: Admin can add a pinned result for a specific query string → content entry.
///       => POST /api/v1/admin/search/pins creates a row in SearchPin.
///   AC2: Pinned result appears at top of search results with 'Featured result' label.
///       => usp_Search_GetPinsForQuery returns IsFeatured = 1 for a pinned entry.
///   AC3: Pins managed via /admin/search/pins table.
///       => GET /api/v1/admin/search/pins returns all pins.
///       => DELETE /api/v1/admin/search/pins/{id} removes a pin.
///
/// Migration: V027 adds SearchPin table + 4 SPs.
///
/// Test strategy:
///   - Integration tests against real SQL Server (via DatabaseFixture / TestContainers).
///   - V027 structural tests: table and SPs exist.
///   - Controller unit tests (repo backed by real DB).
///   - Pin lifecycle: Create → List → verify in DB → Delete → verify gone.
///   - IsFeatured flag verified via direct SP call.
/// </summary>
[Collection("Database")]
public class Issue52AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private ISearchPinRepository Repo() => new SearchPinRepository(fixture.CreateDb());

    private SearchPinsController Controller()
    {
        var ctrl = new SearchPinsController(Repo());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return ctrl;
    }

    // ── V027 structural: table and SPs exist ──────────────────────────────────

    [Fact]
    public async Task V027_SearchPinTable_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.tables WHERE [name] = 'SearchPin';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V027_UspSearchPinList_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_SearchPin_List' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V027_UspSearchPinCreate_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_SearchPin_Create' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V027_UspSearchPinDelete_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_SearchPin_Delete' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V027_UspSearchGetPinsForQuery_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.objects WHERE [name] = 'usp_Search_GetPinsForQuery' AND [type] = 'P';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V027_SearchPinTable_HasUniqueIndexOnQueryString()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM   sys.indexes i
            JOIN   sys.tables  t ON t.[object_id] = i.[object_id]
            WHERE  t.[name] = 'SearchPin'
              AND  i.[name] = 'UX_SearchPin_QueryString'
              AND  i.[is_unique] = 1;";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    // ── AC1: Admin can add a pinned result ────────────────────────────────────

    [Fact]
    public async Task Create_Returns201_WithId()
    {
        var entryId = await GetAnyPublishedEntryIdAsync();
        if (entryId == null) return; // no published content in test DB, skip

        var result = await Controller().Create(new CreatePinRequest
        {
            QueryString    = $"test-pin-{Guid.NewGuid():N}",
            ContentEntryId = entryId.Value,
        });

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);

        var body = Assert.IsType<CreatePinResponseDto>(created.Value);
        Assert.True(body.Id > 0, "Created pin should have a positive Id");
    }

    [Fact]
    public async Task Create_InsertsRowInSearchPin()
    {
        var entryId = await GetAnyPublishedEntryIdAsync();
        if (entryId == null) return;

        var uniqueQuery = $"ac1-insert-{Guid.NewGuid():N}";
        await Repo().CreateAsync(uniqueQuery, entryId.Value);

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM [dbo].[SearchPin] WHERE [QueryString] = @q;";
        cmd.Parameters.AddWithValue("@q", uniqueQuery);
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Create_Upserts_WhenQueryAlreadyPinned()
    {
        var entryId = await GetAnyPublishedEntryIdAsync();
        if (entryId == null) return;

        var uniqueQuery = $"ac1-upsert-{Guid.NewGuid():N}";
        var repo = Repo();

        // Create once
        var id1 = await repo.CreateAsync(uniqueQuery, entryId.Value);

        // Re-create with same query — should update, not throw
        var id2 = await repo.CreateAsync(uniqueQuery, entryId.Value);

        Assert.Equal(id1, id2); // same row was updated

        // Should still be only one row for this query
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM [dbo].[SearchPin] WHERE [QueryString] = @q;";
        cmd.Parameters.AddWithValue("@q", uniqueQuery);
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Create_MissingQueryString_Returns400()
    {
        var result = await Controller().Create(new CreatePinRequest
        {
            QueryString    = "   ",
            ContentEntryId = 1,
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_ZeroContentEntryId_Returns400()
    {
        var result = await Controller().Create(new CreatePinRequest
        {
            QueryString    = "some query",
            ContentEntryId = 0,
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── AC3: Pins managed via the table ──────────────────────────────────────

    [Fact]
    public async Task List_Returns200Ok()
    {
        var result = await Controller().List();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task List_ResponseShapeIsCorrect()
    {
        var result   = await Controller().List();
        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SearchPinsListDto>(ok.Value);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task List_NewPin_AppearsInListAll()
    {
        var entryId = await GetAnyPublishedEntryIdAsync();
        if (entryId == null) return;

        var uniqueQuery = $"ac3-list-{Guid.NewGuid():N}";
        var repo = Repo();
        await repo.CreateAsync(uniqueQuery, entryId.Value);

        var pins = await repo.ListAsync();
        Assert.Contains(pins, p => p.QueryString == uniqueQuery);
    }

    [Fact]
    public async Task List_FilterByQueryString_ReturnsOnlyMatchingPin()
    {
        var entryId = await GetAnyPublishedEntryIdAsync();
        if (entryId == null) return;

        var uniqueQuery = $"ac3-filter-{Guid.NewGuid():N}";
        var repo = Repo();
        await repo.CreateAsync(uniqueQuery, entryId.Value);

        var pins = await repo.ListAsync(uniqueQuery);
        Assert.Single(pins);
        Assert.Equal(uniqueQuery, pins[0].QueryString);
    }

    [Fact]
    public async Task Delete_Returns204NoContent()
    {
        // Delete a non-existent id — SP silently does nothing; controller still returns 204.
        var result = await Controller().Delete(long.MaxValue);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_RemovesPinFromDatabase()
    {
        var entryId = await GetAnyPublishedEntryIdAsync();
        if (entryId == null) return;

        var uniqueQuery = $"ac3-delete-{Guid.NewGuid():N}";
        var repo = Repo();
        var pinId = await repo.CreateAsync(uniqueQuery, entryId.Value);

        // Verify it exists
        var beforeDelete = await repo.ListAsync(uniqueQuery);
        Assert.Single(beforeDelete);

        // Delete
        await repo.DeleteAsync(pinId);

        // Verify it's gone
        var afterDelete = await repo.ListAsync(uniqueQuery);
        Assert.Empty(afterDelete);
    }

    // ── AC2: Pinned result appears with IsFeatured flag ───────────────────────

    [Fact]
    public async Task GetPinsForQuery_NoPin_ReturnsEmpty()
    {
        var uniqueQuery = $"ac2-no-pin-{Guid.NewGuid():N}";

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Search_GetPinsForQuery @QueryString";
        cmd.Parameters.AddWithValue("@QueryString", uniqueQuery);

        var rows = new List<bool>(); // IsFeatured values
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetBoolean(reader.GetOrdinal("IsFeatured")));

        Assert.Empty(rows); // no pin for this query
    }

    [Fact]
    public async Task GetPinsForQuery_WithPin_ReturnsIsFeaturedTrue()
    {
        // Always seed our own published entry to avoid shared-state issues with
        // DemoSeedServiceTests that may archive / delete content during the full suite.
        var entryId = await CreateOwnPublishedEntryAsync();
        if (entryId == null) return; // ContentType not available in test DB

        var uniqueQuery = $"ac2-featured-{Guid.NewGuid():N}";
        var repo = Repo();
        await repo.CreateAsync(uniqueQuery, entryId.Value);

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Search_GetPinsForQuery @QueryString";
        cmd.Parameters.AddWithValue("@QueryString", uniqueQuery);

        var rows = new List<(long Id, bool IsFeatured)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetBoolean(reader.GetOrdinal("IsFeatured"))
            ));
        }

        Assert.Single(rows);
        Assert.True(rows[0].IsFeatured, "Pinned entry should have IsFeatured = true");
        Assert.Equal(entryId.Value, rows[0].Id);
    }

    // ── DTO shape: required properties exist ─────────────────────────────────

    [Fact]
    public void SearchPinDto_HasRequiredProperties()
    {
        var type = typeof(SearchPinDto);
        Assert.NotNull(type.GetProperty("Id"));
        Assert.NotNull(type.GetProperty("QueryString"));
        Assert.NotNull(type.GetProperty("ContentEntryId"));
        Assert.NotNull(type.GetProperty("CreatedAt"));
        Assert.NotNull(type.GetProperty("EntrySlug"));
        Assert.NotNull(type.GetProperty("EntryTitle"));
    }

    [Fact]
    public void CreatePinRequest_HasRequiredProperties()
    {
        var type = typeof(CreatePinRequest);
        Assert.NotNull(type.GetProperty("QueryString"));
        Assert.NotNull(type.GetProperty("ContentEntryId"));
    }

    [Fact]
    public void CreatePinResponseDto_HasIdProperty()
    {
        var type = typeof(CreatePinResponseDto);
        Assert.NotNull(type.GetProperty("Id"));
    }

    [Fact]
    public void SearchPinsListDto_HasItemsProperty()
    {
        var type = typeof(SearchPinsListDto);
        Assert.NotNull(type.GetProperty("Items"));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Id of a published ContentEntry, creating one if none exist.
    /// This makes the test self-sufficient and not dependent on demo seed data.
    /// </summary>
    private async Task<long?> GetAnyPublishedEntryIdAsync()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Try to find an existing published entry first
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP 1 [Id] FROM [dbo].[ContentEntry] WHERE [Status] = 'Published';";
            var existing = await cmd.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
                return (long)existing;
        }

        // None found — find or create a ContentType
        long contentTypeId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP 1 [Id] FROM [dbo].[ContentType];";
            var ctResult = await cmd.ExecuteScalarAsync();
            if (ctResult == null || ctResult == DBNull.Value)
                return null; // No content type available; skip test

            contentTypeId = (long)ctResult;
        }

        // Get or create a User for OwnerId
        long ownerId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP 1 [Id] FROM [dbo].[User];";
            var userResult = await cmd.ExecuteScalarAsync();
            if (userResult == null || userResult == DBNull.Value)
            {
                // Insert a minimal user
                cmd.CommandText =
                    "INSERT INTO [dbo].[User] ([ExternalId],[Email],[DisplayName],[IsActive],[CreatedAt],[UpdatedAt]) " +
                    "VALUES (NEWID(), 'test@test.invalid', 'Test User', 1, SYSUTCDATETIME(), SYSUTCDATETIME()); " +
                    "SELECT SCOPE_IDENTITY();";
                ownerId = Convert.ToInt64(await cmd.ExecuteScalarAsync()!);
            }
            else
            {
                ownerId = (long)userResult;
            }
        }

        // Create a ContentEntry in Published state with a published ContentVersion
        long entryId;
        await using (var cmd = conn.CreateCommand())
        {
            var slug = $"test-pin-entry-{Guid.NewGuid():N}";
            cmd.CommandText = @"
                DECLARE @EntryId BIGINT;
                INSERT INTO [dbo].[ContentEntry] ([ContentTypeId],[Slug],[Locale],[Status],[OwnerId],[CreatedAt],[UpdatedAt])
                VALUES (@ContentTypeId, @Slug, 'en-US', 'Published', @OwnerId, SYSUTCDATETIME(), SYSUTCDATETIME());
                SET @EntryId = SCOPE_IDENTITY();

                DECLARE @VersionId BIGINT;
                INSERT INTO [dbo].[ContentVersion] ([ContentEntryId],[VersionNumber],[FieldsJson],[Status],[AuthorId],[CreatedAt])
                VALUES (@EntryId, 1, N'{""title"":""Test Pinned Entry""}', 'Published', @OwnerId, SYSUTCDATETIME());
                SET @VersionId = SCOPE_IDENTITY();

                UPDATE [dbo].[ContentEntry] SET [PublishedVersionId] = @VersionId WHERE [Id] = @EntryId;

                SELECT @EntryId;";
            cmd.Parameters.AddWithValue("@ContentTypeId", contentTypeId);
            cmd.Parameters.AddWithValue("@Slug",          slug);
            cmd.Parameters.AddWithValue("@OwnerId",       ownerId);
            entryId = Convert.ToInt64(await cmd.ExecuteScalarAsync()!);
        }

        return entryId;
    }

    /// <summary>
    /// Always creates a brand-new published ContentEntry + ContentVersion pair for the test.
    /// Used when the test must not depend on existing (potentially mutated) shared state.
    /// Returns null only if there is no ContentType in the DB at all.
    /// </summary>
    private async Task<long?> CreateOwnPublishedEntryAsync()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        long contentTypeId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP 1 [Id] FROM [dbo].[ContentType];";
            var ctResult = await cmd.ExecuteScalarAsync();
            if (ctResult == null || ctResult == DBNull.Value)
                return null;
            contentTypeId = (long)ctResult;
        }

        long ownerId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP 1 [Id] FROM [dbo].[User];";
            var userResult = await cmd.ExecuteScalarAsync();
            if (userResult == null || userResult == DBNull.Value)
            {
                cmd.CommandText =
                    "INSERT INTO [dbo].[User] ([ExternalId],[Email],[DisplayName],[IsActive],[CreatedAt],[UpdatedAt]) " +
                    $"VALUES (NEWID(), 'pintest-{Guid.NewGuid():N}@test.invalid', 'Pin Test User', 1, SYSUTCDATETIME(), SYSUTCDATETIME()); " +
                    "SELECT SCOPE_IDENTITY();";
                ownerId = Convert.ToInt64(await cmd.ExecuteScalarAsync()!);
            }
            else
            {
                ownerId = (long)userResult;
            }
        }

        long entryId;
        await using (var cmd = conn.CreateCommand())
        {
            var slug = $"pin-ac2-{Guid.NewGuid():N}";
            cmd.CommandText = @"
                DECLARE @EntryId BIGINT;
                INSERT INTO [dbo].[ContentEntry] ([ContentTypeId],[Slug],[Locale],[Status],[OwnerId],[CreatedAt],[UpdatedAt])
                VALUES (@ContentTypeId, @Slug, 'en-US', 'Published', @OwnerId, SYSUTCDATETIME(), SYSUTCDATETIME());
                SET @EntryId = SCOPE_IDENTITY();

                DECLARE @VersionId BIGINT;
                INSERT INTO [dbo].[ContentVersion] ([ContentEntryId],[VersionNumber],[FieldsJson],[Status],[AuthorId],[CreatedAt])
                VALUES (@EntryId, 1, N'{""title"":""AC2 Test Entry""}', 'Published', @OwnerId, SYSUTCDATETIME());
                SET @VersionId = SCOPE_IDENTITY();

                UPDATE [dbo].[ContentEntry] SET [PublishedVersionId] = @VersionId WHERE [Id] = @EntryId;

                SELECT @EntryId;";
            cmd.Parameters.AddWithValue("@ContentTypeId", contentTypeId);
            cmd.Parameters.AddWithValue("@Slug",          slug);
            cmd.Parameters.AddWithValue("@OwnerId",       ownerId);
            entryId = Convert.ToInt64(await cmd.ExecuteScalarAsync()!);
        }

        return entryId;
    }
}
