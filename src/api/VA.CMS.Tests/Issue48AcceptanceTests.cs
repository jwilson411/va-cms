using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using VA.CMS.API.Controllers;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #48 — Build redirect management table in admin.
///
/// BRD FR-NAV-06.
///
/// Acceptance criteria:
///   AC1: Admin page lists all active redirects with From, To, Status Code, Created By.
///   AC2: Create, edit, deactivate actions.
///   AC3: API middleware handles redirects (existing usp_Redirect_GetByPath used at request time).
///
/// Test strategy:
///   - RedirectAdminController acceptance tests against a real DB (via DatabaseFixture).
///   - NavigationRepository integration tests for new usp_Redirect_* admin SPs.
/// </summary>
[Collection("Database")]
public class Issue48AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private INavigationRepository NavRepo() =>
        new NavigationRepository(fixture.CreateDb());

    private RedirectAdminController Controller()
    {
        var controller = new RedirectAdminController(NavRepo());
        // Simulate no authenticated user claim — CreatedById will be 0 for tests
        return controller;
    }

    private async Task<long> SeedUserAsync()
        => await TestSeeder.UpsertUserAsync(fixture.ConnectionString);

    private async Task<long> SeedRedirectAsync(
        string fromPath, string toPath, int statusCode = 301, long? createdById = null)
    {
        var userId = createdById ?? await SeedUserAsync();
        var redirect = new Redirect
        {
            FromPath    = fromPath,
            ToPath      = toPath,
            StatusCode  = statusCode,
            IsActive    = true,
            CreatedById = userId,
        };
        return await NavRepo().CreateRedirectAsync(redirect);
    }

    // ── AC1: List returns From, To, StatusCode, CreatedBy ────────────────────

    [Fact]
    public async Task ListRedirects_Returns200_WithAllFields()
    {
        // Arrange: seed a redirect
        var userId = await SeedUserAsync();
        var fromPath = $"/old-{Guid.NewGuid():N}";
        var toPath   = $"/new-{Guid.NewGuid():N}";
        await NavRepo().CreateRedirectAsync(new Redirect
        {
            FromPath    = fromPath,
            ToPath      = toPath,
            StatusCode  = 301,
            IsActive    = true,
            CreatedById = userId,
        });

        // Act
        var result = await Controller().ListRedirects();

        // Assert: 200 OK with list response
        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<RedirectListResponse>(ok.Value);
        Assert.True(body.TotalRows >= 1);
        Assert.NotEmpty(body.Items);

        // Find the row we seeded
        var row = body.Items.FirstOrDefault(r => r.FromPath == fromPath);
        Assert.NotNull(row);
        Assert.Equal(toPath, row!.ToPath);
        Assert.Equal(301, row.StatusCode);
        Assert.NotNull(row.CreatedByEmail);
    }

    [Fact]
    public async Task ListRedirects_FilterByIsActive_ReturnsOnlyActive()
    {
        // Arrange: create one active, deactivate another
        var fromActive   = $"/active-{Guid.NewGuid():N}";
        var fromInactive = $"/inactive-{Guid.NewGuid():N}";
        var id1 = await SeedRedirectAsync(fromActive, "/dest1");
        var id2 = await SeedRedirectAsync(fromInactive, "/dest2");
        await NavRepo().DeactivateRedirectAsync(id2);

        // Act — isActive=true
        var result = await Controller().ListRedirects(isActive: true);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var body   = Assert.IsType<RedirectListResponse>(ok.Value);

        // Active row should appear; inactive row should not
        Assert.Contains(body.Items, r => r.FromPath == fromActive && r.IsActive);
        Assert.DoesNotContain(body.Items, r => r.FromPath == fromInactive);
    }

    // ── AC2a: Create ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRedirect_ViaRepository_PersistsRow()
    {
        // The controller Create action requires an authenticated user for CreatedById.
        // We test the full create flow at the repository layer here; the controller
        // validation tests below cover the HTTP contract.
        var userId   = await SeedUserAsync();
        var fromPath = $"/create-{Guid.NewGuid():N}";

        var redirect = new Redirect
        {
            FromPath    = fromPath,
            ToPath      = "/destination",
            StatusCode  = 301,
            IsActive    = true,
            CreatedById = userId,
        };
        var newId = await NavRepo().CreateRedirectAsync(redirect);

        // Verify persisted
        var row = await NavRepo().GetRedirectByIdAsync(newId);
        Assert.NotNull(row);
        Assert.Equal(fromPath, row!.FromPath);
        Assert.Equal("/destination", row.ToPath);
        Assert.Equal(301, row.StatusCode);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task CreateRedirect_MissingFromPath_Returns400()
    {
        var req = new CreateRedirectRequest { FromPath = "", ToPath = "/dest", StatusCode = 301 };
        var result = await Controller().CreateRedirect(req);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateRedirect_InvalidStatusCode_Returns400()
    {
        var req = new CreateRedirectRequest { FromPath = "/old", ToPath = "/new", StatusCode = 200 };
        var result = await Controller().CreateRedirect(req);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateRedirect_302_IsAccepted()
    {
        // Use repository layer — controller needs auth context for CreatedById
        var userId   = await SeedUserAsync();
        var fromPath = $"/302-{Guid.NewGuid():N}";

        var redirect = new Redirect
        {
            FromPath    = fromPath,
            ToPath      = "/temp-dest",
            StatusCode  = 302,
            IsActive    = true,
            CreatedById = userId,
        };
        var newId = await NavRepo().CreateRedirectAsync(redirect);
        var row   = await NavRepo().GetRedirectByIdAsync(newId);

        Assert.NotNull(row);
        Assert.Equal(302, row!.StatusCode);
    }

    // ── AC2b: Edit ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EditRedirect_Returns204_AndPersistsChange()
    {
        var fromPath = $"/edit-old-{Guid.NewGuid():N}";
        var newPath  = $"/edit-new-{Guid.NewGuid():N}";
        var id = await SeedRedirectAsync(fromPath, "/original-dest");

        var req = new UpdateRedirectRequest
        {
            FromPath   = newPath,
            ToPath     = "/updated-dest",
            StatusCode = 302,
        };
        var result = await Controller().UpdateRedirect(id, req);

        Assert.IsType<NoContentResult>(result);

        // Verify persisted
        var row = await NavRepo().GetRedirectByIdAsync(id);
        Assert.NotNull(row);
        Assert.Equal(newPath, row!.FromPath);
        Assert.Equal("/updated-dest", row.ToPath);
        Assert.Equal(302, row.StatusCode);
    }

    [Fact]
    public async Task EditRedirect_NotFound_Returns404()
    {
        var req = new UpdateRedirectRequest { FromPath = "/x", ToPath = "/y", StatusCode = 301 };
        var result = await Controller().UpdateRedirect(long.MaxValue, req);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditRedirect_InvalidStatusCode_Returns400()
    {
        var id = await SeedRedirectAsync($"/edit-bad-{Guid.NewGuid():N}", "/dest");
        var req = new UpdateRedirectRequest { FromPath = "/x", ToPath = "/y", StatusCode = 200 };
        var result = await Controller().UpdateRedirect(id, req);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── AC2c: Deactivate ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateRedirect_Returns204_AndSetsIsActiveFalse()
    {
        var fromPath = $"/deactivate-{Guid.NewGuid():N}";
        var id = await SeedRedirectAsync(fromPath, "/dest");

        var result = await Controller().DeactivateRedirect(id);

        Assert.IsType<NoContentResult>(result);

        var row = await NavRepo().GetRedirectByIdAsync(id);
        Assert.NotNull(row);
        Assert.False(row!.IsActive);
    }

    [Fact]
    public async Task DeactivateRedirect_NotFound_Returns404()
    {
        var result = await Controller().DeactivateRedirect(long.MaxValue);
        Assert.IsType<NotFoundResult>(result);
    }

    // ── AC1: GetById ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRedirect_ById_ReturnsRow()
    {
        var fromPath = $"/getbyid-{Guid.NewGuid():N}";
        var id = await SeedRedirectAsync(fromPath, "/dest-x");

        var result = await Controller().GetRedirect(id);

        var ok  = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<RedirectAdminDto>(ok.Value);
        Assert.Equal(fromPath, dto.FromPath);
        Assert.Equal("/dest-x", dto.ToPath);
    }

    [Fact]
    public async Task GetRedirect_NotFound_Returns404()
    {
        var result = await Controller().GetRedirect(long.MaxValue);
        Assert.IsType<NotFoundResult>(result);
    }

    // ── Repository integration: usp_Redirect_List ────────────────────────────

    [Fact]
    public async Task ListRedirectsAsync_Paginates_Correctly()
    {
        // Seed 5 redirects
        for (var i = 0; i < 5; i++)
            await SeedRedirectAsync($"/page-test-{Guid.NewGuid():N}", "/dest");

        var (rows, total) = await NavRepo().ListRedirectsAsync(page: 1, pageSize: 2);
        Assert.True(total >= 5);
        Assert.Equal(2, rows.Count);
    }

    // ── Deactivate idempotency ────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateRedirect_Idempotent_SecondCallSucceeds()
    {
        var id = await SeedRedirectAsync($"/idem-{Guid.NewGuid():N}", "/dest");

        await Controller().DeactivateRedirect(id);
        // Second call — already deactivated, but should still 204 (GetById returns row)
        var result = await Controller().DeactivateRedirect(id);
        Assert.IsType<NoContentResult>(result);
    }
}
