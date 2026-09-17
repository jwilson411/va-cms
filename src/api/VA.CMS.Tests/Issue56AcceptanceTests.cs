using VA.CMS.Infrastructure.Settings;
using VA.CMS.API.Auth;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #56 — Build user directory and role assignment admin UI.
///
/// BRD FR-USERS-03 and FR-USERS-04.
///
/// Acceptance criteria:
///   AC1: /admin/users lists all users (search by name/email)
///   AC2: User detail shows assigned roles and sections
///   AC3: Admin can add/remove roles and section scope
///   AC4: Deactivate user action (sets IsActive=0, next login returns 403)
///
/// Migration: V029 adds usp_Role_List, usp_ContentSection_List, usp_User_GetDetail.
/// </summary>
[Collection("Database")]
public class Issue56AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private IUserRepository       UserRepo()     => new UserRepository(fixture.CreateDb());
    private IUserRoleRepository   UserRoleRepo() => new UserRoleRepository(fixture.CreateDb());
    private IRoleRepository       RoleRepo()     => new RoleRepository(fixture.CreateDb());

    private async Task<long> SeedUserAsync(string suffix = "")
    {
        // TestSeeder.UpsertUserAsync uses a random Guid — suffix is just for naming clarity.
        return await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
    }

    private UserRoleController Controller(long actorId)
    {
        var ctrl = new UserRoleController(UserRepo(), UserRoleRepo(), RoleRepo(), new SessionRevocationGuard(StaticSiteSettings.Defaults));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[]
                        {
                            new System.Security.Claims.Claim("cms_user_id", actorId.ToString()),
                        },
                        authenticationType: "test")),
            },
        };
        return ctrl;
    }

    // ── V029 migration: SPs exist ──────────────────────────────────────────────

    [Fact]
    public async Task V029_usp_Role_List_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_Role_List';";
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V029_usp_ContentSection_List_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_ContentSection_List';";
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V029_usp_User_GetDetail_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_User_GetDetail';";
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    // ── AC1: User list (search by name/email) ─────────────────────────────────

    [Fact]
    public async Task AC1_ListUsers_ReturnsActiveUsers()
    {
        // Seed a user whose DisplayName sorts first: usp_User_List orders by DisplayName and the
        // shared test DB holds more than one page of seeded users.
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString,
            displayName: $"000 list user {Guid.NewGuid():N}");

        var result = await UserRoleRepo().ListAsync(searchTerm: null, isActive: true, page: 1, pageSize: 100);
        var list   = result.ToList();

        Assert.Contains(list, u => u.Id == userId);
    }

    [Fact]
    public async Task AC1_ListUsers_SearchByEmail_ReturnsMatchingUser()
    {
        // Seed a user with a known email prefix so search works
        var uid     = Guid.NewGuid().ToString("N");
        var email   = $"srch-{uid}@va.gov";
        var userId  = await TestSeeder.UpsertUserAsync(
            fixture.ConnectionString,
            externalId:  $"ext-srch-{uid}",
            email:       email,
            displayName: $"Search User {uid}");

        var result = await UserRoleRepo().ListAsync(searchTerm: $"srch-{uid}", isActive: true, page: 1, pageSize: 50);
        var list   = result.ToList();

        Assert.Contains(list, u => u.Id == userId);
    }

    [Fact]
    public async Task AC1_Controller_ListUsers_Returns200()
    {
        var actorId = await SeedUserAsync("_actor1");
        var ctrl    = Controller(actorId);

        var result = await ctrl.ListUsers(search: null, page: 1, pageSize: 50);

        Assert.IsType<OkObjectResult>(result);
    }

    // ── AC2: User detail shows assigned roles and sections ────────────────────

    [Fact]
    public async Task AC2_GetDetailAsync_ReturnsUserWithRoles()
    {
        var userId  = await SeedUserAsync("_detail");
        var actorId = await SeedUserAsync("_granter");
        var roles   = (await RoleRepo().ListAllAsync()).ToList();
        Assert.NotEmpty(roles);

        // Assign a role
        await UserRoleRepo().AssignRoleAsync(userId, roles[0].Id, actorId, sectionId: null);

        var detail = await UserRoleRepo().GetDetailAsync(userId);

        Assert.NotNull(detail);
        Assert.Equal(userId, detail.Id);
        Assert.Contains(detail.Roles, r => r.RoleId == roles[0].Id);
    }

    [Fact]
    public async Task AC2_GetDetailAsync_ReturnsNull_ForMissingUser()
    {
        var detail = await UserRoleRepo().GetDetailAsync(long.MaxValue - 1);
        Assert.Null(detail);
    }

    [Fact]
    public async Task AC2_Controller_GetUser_Returns200_WithRoles()
    {
        var userId  = await SeedUserAsync("_det2");
        var actorId = await SeedUserAsync("_act2");
        var ctrl    = Controller(actorId);

        var result = await ctrl.GetUser(userId);

        Assert.IsType<OkObjectResult>(result);
        var body = (OkObjectResult)result;
        Assert.IsType<UserDetail>(body.Value);
    }

    [Fact]
    public async Task AC2_Controller_GetUser_Returns404_ForMissingUser()
    {
        var actorId = await SeedUserAsync("_act3");
        var ctrl    = Controller(actorId);

        var result = await ctrl.GetUser(long.MaxValue - 2);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── AC3: Assign and remove roles with section scope ───────────────────────

    [Fact]
    public async Task AC3_AssignRole_And_GetRoles_RoundTrip()
    {
        var userId  = await SeedUserAsync("_role1");
        var actorId = await SeedUserAsync("_grantR");
        var roles   = (await RoleRepo().ListAllAsync()).ToList();
        Assert.NotEmpty(roles);

        await UserRoleRepo().AssignRoleAsync(userId, roles[0].Id, actorId, sectionId: null);

        var assignments = (await UserRepo().GetRolesAsync(userId)).ToList();
        Assert.Contains(assignments, a => a.RoleId == roles[0].Id);
    }

    [Fact]
    public async Task AC3_RevokeRole_RemovesAssignment()
    {
        var userId  = await SeedUserAsync("_role2");
        var actorId = await SeedUserAsync("_grantR2");
        var roles   = (await RoleRepo().ListAllAsync()).ToList();
        Assert.NotEmpty(roles);

        // Assign then revoke
        await UserRoleRepo().AssignRoleAsync(userId, roles[0].Id, actorId, sectionId: null);
        await UserRoleRepo().RevokeRoleAsync(userId, roles[0].Id, sectionId: null);

        var assignments = (await UserRepo().GetRolesAsync(userId)).ToList();
        Assert.DoesNotContain(assignments, a => a.RoleId == roles[0].Id);
    }

    [Fact]
    public async Task AC3_Controller_AssignRole_Returns204()
    {
        var userId  = await SeedUserAsync("_role3");
        var actorId = await SeedUserAsync("_grantR3");
        var roles   = (await RoleRepo().ListAllAsync()).ToList();
        var ctrl    = Controller(actorId);

        var result = await ctrl.AssignRole(userId, new AssignRoleRequest(roles[0].Id, null));

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AC3_Controller_RevokeRole_Returns204()
    {
        var userId  = await SeedUserAsync("_role4");
        var actorId = await SeedUserAsync("_grantR4");
        var roles   = (await RoleRepo().ListAllAsync()).ToList();
        var ctrl    = Controller(actorId);

        await UserRoleRepo().AssignRoleAsync(userId, roles[0].Id, actorId, sectionId: null);
        var result = await ctrl.RevokeRole(userId, roles[0].Id, sectionId: null);

        Assert.IsType<NoContentResult>(result);
    }

    // ── AC4: Deactivate user — IsActive=0 blocks next login ──────────────────

    [Fact]
    public async Task AC4_DeactivateUser_SetsIsActiveZero()
    {
        var userId  = await SeedUserAsync("_deact");
        var actorId = await SeedUserAsync("_actDeact");

        await UserRoleRepo().DeactivateAsync(userId, actorId);

        var user = await UserRepo().GetByIdAsync(userId);
        Assert.NotNull(user);
        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task AC4_DeactivatedUser_DoesNotAppearInActiveList()
    {
        var userId  = await SeedUserAsync("_deact2");
        var actorId = await SeedUserAsync("_actDeact2");

        await UserRoleRepo().DeactivateAsync(userId, actorId);

        var active = (await UserRoleRepo().ListAsync(isActive: true, page: 1, pageSize: 100)).ToList();
        Assert.DoesNotContain(active, u => u.Id == userId);
    }

    [Fact]
    public async Task AC4_Controller_DeactivateUser_Returns204()
    {
        var userId  = await SeedUserAsync("_deact3");
        var actorId = await SeedUserAsync("_actDeact3");
        var ctrl    = Controller(actorId);

        var result = await ctrl.DeactivateUser(userId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AC4_Controller_DeactivateUser_Returns404_ForMissingUser()
    {
        var actorId = await SeedUserAsync("_actDeact4");
        var ctrl    = Controller(actorId);

        var result = await ctrl.DeactivateUser(long.MaxValue - 3);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Role and Section lookup ────────────────────────────────────────────────

    [Fact]
    public async Task ListRoles_ReturnsSeededSystemRoles()
    {
        var roles = (await RoleRepo().ListAllAsync()).ToList();

        // V012 seeds: ContentOwner, Editor, SiteAdmin, Developer, SystemAdmin, ReadOnly
        Assert.NotEmpty(roles);
        Assert.Contains(roles, r => r.Name == "SystemAdmin");
        Assert.Contains(roles, r => r.Name == "ContentOwner");
    }

    [Fact]
    public async Task ListSections_ReturnsResults()
    {
        // Sections may be empty in a fresh test DB — just assert the SP executes cleanly.
        var sections = await RoleRepo().ListSectionsAsync();
        Assert.NotNull(sections);
    }
}
