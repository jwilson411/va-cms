using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using VA.CMS.API.Controllers;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #46 — Build navigation menu CRUD API and admin drag-and-drop editor.
///
/// BRD FR-NAV-01, FR-NAV-03.
///
/// Acceptance criteria:
///   AC1: GET/POST/PATCH/DELETE /api/v1/navigation/{handle}/items — full CRUD for nav items.
///   AC2: Admin UI: menu items shown in tree; drag-and-drop reorders and reparents.
///        (This test covers the API layer — UI tested in admin/src/features/navigation tests.)
///   AC3: Supports up to 3 levels deep — 4th level is rejected with 422.
///   AC4: Preview button shows menu render before saving (GET /api/v1/navigation/{handle}/preview).
///
/// Test strategy:
///   - NavigationAdminController acceptance tests against a real DB (via DatabaseFixture).
///   - NavigationMenuRepository integration tests for menu CRUD SPs.
/// </summary>
[Collection("Database")]
public class Issue46AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private INavigationMenuRepository MenuRepo() =>
        new NavigationMenuRepository(fixture.CreateDb());

    private INavigationRepository NavRepo() =>
        new NavigationRepository(fixture.CreateDb());

    private NavigationAdminController Controller() =>
        new NavigationAdminController(MenuRepo(), NavRepo());

    private async Task<string> SeedMenuAsync()
    {
        var handle = $"test-menu-{Guid.NewGuid():N}";
        await MenuRepo().CreateAsync($"Test Menu {handle}", handle);
        return handle;
    }

    // ── AC1: Menu CRUD ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMenus_Returns200WithList()
    {
        var result = await Controller().ListMenus();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateMenu_Returns201()
    {
        var handle = $"m-{Guid.NewGuid():N}";
        var result = await Controller().CreateMenu(new CreateMenuRequest("Footer Nav", handle));
        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task CreateMenu_MissingName_Returns400()
    {
        var result = await Controller().CreateMenu(new CreateMenuRequest("", "h1"));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateMenu_MissingHandle_Returns400()
    {
        var result = await Controller().CreateMenu(new CreateMenuRequest("Foo", ""));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateMenu_RenamesMenu()
    {
        var handle = await SeedMenuAsync();
        var result = await Controller().UpdateMenu(handle, new UpdateMenuRequest("Renamed Nav"));
        Assert.IsType<NoContentResult>(result);

        var menu = await MenuRepo().GetByHandleAsync(handle);
        Assert.Equal("Renamed Nav", menu?.Name);
    }

    [Fact]
    public async Task UpdateMenu_UnknownHandle_Returns404()
    {
        var result = await Controller().UpdateMenu("no-such-menu-zzz", new UpdateMenuRequest("X"));
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteMenu_Returns204()
    {
        var handle = await SeedMenuAsync();
        var result = await Controller().DeleteMenu(handle);
        Assert.IsType<NoContentResult>(result);

        var menu = await MenuRepo().GetByHandleAsync(handle);
        Assert.Null(menu);
    }

    [Fact]
    public async Task DeleteMenu_UnknownHandle_Returns404()
    {
        var result = await Controller().DeleteMenu("no-such-menu-abc");
        Assert.IsType<NotFoundResult>(result);
    }

    // ── AC1: Item CRUD ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMenuItems_ReturnsItemsForHandle()
    {
        var handle = await SeedMenuAsync();
        // Seed an item
        await Controller().CreateItem(handle, new UpsertItemRequest
        {
            Label = "Home", Url = "/", SortOrder = 0, IsVisible = true
        });

        var result = await Controller().GetMenuItems(handle);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var resp   = Assert.IsType<NavItemsResponse>(ok.Value);
        Assert.Contains(resp.Items, i => i.Label == "Home");
    }

    [Fact]
    public async Task GetMenuItems_UnknownHandle_Returns404()
    {
        var result = await Controller().GetMenuItems("no-such-handle-xyzzy");
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CreateItem_Returns201()
    {
        var handle = await SeedMenuAsync();
        var result = await Controller().CreateItem(handle, new UpsertItemRequest
        {
            Label = "About", Url = "/about", SortOrder = 1, IsVisible = true
        });
        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task CreateItem_MissingLabel_Returns400()
    {
        var handle = await SeedMenuAsync();
        var result = await Controller().CreateItem(handle, new UpsertItemRequest
        {
            Label = "", Url = "/", SortOrder = 0
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateItem_Returns204()
    {
        var handle = await SeedMenuAsync();
        // Create item first
        var createResult = await Controller().CreateItem(handle, new UpsertItemRequest
        {
            Label = "Services", Url = "/services", SortOrder = 2, IsVisible = true
        });
        var created = Assert.IsType<CreatedAtActionResult>(createResult);
        var dto     = Assert.IsType<NavItemAdminDto>(created.Value);

        // Update it
        var result = await Controller().UpdateItem(handle, dto.Id, new UpsertItemRequest
        {
            Label = "Our Services", Url = "/our-services", SortOrder = 2, IsVisible = false
        });
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteItem_Returns204()
    {
        var handle = await SeedMenuAsync();
        var createResult = await Controller().CreateItem(handle, new UpsertItemRequest
        {
            Label = "Delete Me", Url = "/del", SortOrder = 99, IsVisible = true
        });
        var created = Assert.IsType<CreatedAtActionResult>(createResult);
        var dto     = Assert.IsType<NavItemAdminDto>(created.Value);

        var result = await Controller().DeleteItem(handle, dto.Id);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteItem_WrongHandle_Returns404()
    {
        var handle = await SeedMenuAsync();
        var createResult = await Controller().CreateItem(handle, new UpsertItemRequest
        {
            Label = "Test", Url = "/", SortOrder = 0, IsVisible = true
        });
        var dto = Assert.IsType<NavItemAdminDto>(
            Assert.IsType<CreatedAtActionResult>(createResult).Value);

        var result = await Controller().DeleteItem("some-other-menu", dto.Id);
        Assert.IsType<NotFoundResult>(result);
    }

    // ── AC3: 3-level depth enforcement ────────────────────────────────────────

    [Fact]
    public async Task CreateItem_ThreeLevel_Succeeds()
    {
        // Level 0 → Level 1 → Level 2 should all succeed
        var handle = await SeedMenuAsync();
        var l0 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L0", Url = "/l0", SortOrder = 0, IsVisible = true }));
        var l1 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L1", Url = "/l1", SortOrder = 0, IsVisible = true, ParentItemId = l0 }));
        var l2 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L2", Url = "/l2", SortOrder = 0, IsVisible = true, ParentItemId = l1 }));

        Assert.True(l0 > 0);
        Assert.True(l1 > 0);
        Assert.True(l2 > 0);
    }

    [Fact]
    public async Task CreateItem_FourthLevel_Returns422()
    {
        // Level 0 → 1 → 2 is max. Level 3 (4th level) should return 422.
        var handle = await SeedMenuAsync();
        var l0 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L0", Url = "/l0", SortOrder = 0, IsVisible = true }));
        var l1 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L1", Url = "/l1", SortOrder = 0, IsVisible = true, ParentItemId = l0 }));
        var l2 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L2", Url = "/l2", SortOrder = 0, IsVisible = true, ParentItemId = l1 }));

        // This should be rejected (4th level, 0-indexed depth 3)
        var result = await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "L3-BLOCKED", Url = "/l3", SortOrder = 0, IsVisible = true, ParentItemId = l2 });

        Assert.IsType<UnprocessableEntityObjectResult>(result);
    }

    // ── AC4: Preview endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task PreviewMenu_Returns200WithNestedTree()
    {
        var handle = await SeedMenuAsync();
        await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "Home", Url = "/", SortOrder = 0, IsVisible = true });
        await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "About", Url = "/about", SortOrder = 1, IsVisible = true });

        var result = await Controller().PreviewMenu(handle);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var resp   = Assert.IsType<NavigationMenuPreviewResponse>(ok.Value);
        Assert.Equal(handle, resp.Handle);
        Assert.NotEmpty(resp.Items);
    }

    [Fact]
    public async Task PreviewMenu_UnknownHandle_Returns404()
    {
        var result = await Controller().PreviewMenu("no-such-menu-preview-xyzzy");
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PreviewMenu_NestedItems_AreNested()
    {
        var handle = await SeedMenuAsync();
        var parentId = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "Parent", Url = "/parent", SortOrder = 0, IsVisible = true }));
        await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "Child", Url = "/child", SortOrder = 0, IsVisible = true, ParentItemId = parentId });

        var result = await Controller().PreviewMenu(handle);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var resp   = Assert.IsType<NavigationMenuPreviewResponse>(ok.Value);

        var parent = resp.Items.FirstOrDefault(i => i.Id == parentId);
        Assert.NotNull(parent);
        Assert.Single(parent!.Children);
        Assert.Equal("Child", parent.Children[0].Label);
    }

    // ── Bulk reorder (drag-and-drop) ──────────────────────────────────────────

    [Fact]
    public async Task BulkReorder_Returns204OnValidPayload()
    {
        var handle = await SeedMenuAsync();
        var id1 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "Item1", Url = "/i1", SortOrder = 0, IsVisible = true }));
        var id2 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "Item2", Url = "/i2", SortOrder = 1, IsVisible = true }));

        var result = await Controller().BulkReorder(handle, new List<ReorderItemRequest>
        {
            new() { Id = id1, ParentItemId = null, SortOrder = 1 },
            new() { Id = id2, ParentItemId = null, SortOrder = 0 },
        });
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task BulkReorder_TooDeep_Returns422()
    {
        var handle = await SeedMenuAsync();
        var id1 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "A", Url = "/a", SortOrder = 0, IsVisible = true }));
        var id2 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "B", Url = "/b", SortOrder = 0, IsVisible = true }));
        var id3 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "C", Url = "/c", SortOrder = 0, IsVisible = true }));
        var id4 = GetCreatedId(await Controller().CreateItem(handle, new UpsertItemRequest
            { Label = "D", Url = "/d", SortOrder = 0, IsVisible = true }));

        // Try to make a chain 4 deep: id1 → id2 → id3 → id4
        var result = await Controller().BulkReorder(handle, new List<ReorderItemRequest>
        {
            new() { Id = id1, ParentItemId = null, SortOrder = 0 },
            new() { Id = id2, ParentItemId = id1,  SortOrder = 0 },
            new() { Id = id3, ParentItemId = id2,  SortOrder = 0 },
            new() { Id = id4, ParentItemId = id3,  SortOrder = 0 }, // depth=3 — too deep
        });
        Assert.IsType<UnprocessableEntityObjectResult>(result);
    }

    // ── NavigationMenuRepository unit tests ───────────────────────────────────

    [Fact]
    public async Task NavigationMenuRepository_CreateAndGetByHandle()
    {
        var handle = $"repo-test-{Guid.NewGuid():N}";
        var repo   = MenuRepo();
        var id     = await repo.CreateAsync("Repo Test Menu", handle);
        Assert.True(id > 0);

        var menu = await repo.GetByHandleAsync(handle);
        Assert.NotNull(menu);
        Assert.Equal("Repo Test Menu", menu!.Name);
        Assert.Equal(handle, menu.Handle);
    }

    [Fact]
    public async Task NavigationMenuRepository_ListAllIncludes_CreatedMenu()
    {
        var handle = $"list-{Guid.NewGuid():N}";
        await MenuRepo().CreateAsync("Listed Menu", handle);
        var all = await MenuRepo().ListAllAsync();
        Assert.Contains(all, m => m.Handle == handle);
    }

    [Fact]
    public async Task NavigationMenuRepository_Delete_RemovesMenu()
    {
        var handle = $"del-{Guid.NewGuid():N}";
        var repo   = MenuRepo();
        var id     = await repo.CreateAsync("Delete Test", handle);
        await repo.DeleteAsync(id);
        var menu = await repo.GetByHandleAsync(handle);
        Assert.Null(menu);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static long GetCreatedId(IActionResult result)
    {
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto     = Assert.IsType<NavItemAdminDto>(created.Value);
        return dto.Id;
    }
}
