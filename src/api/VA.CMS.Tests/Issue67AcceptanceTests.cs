using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #67: AD Group → CMS Role Mapping.
///
/// AC1: Admin Settings page includes an "AD Group Mappings" section.
///   → GET /api/v1/admin/settings/ad-group-mappings returns 200.
///
/// AC2: Admin can add a mapping: AD Group Name → CMS Role.
///   → POST /api/v1/admin/settings/ad-group-mappings returns 201.
///
/// AC3: Multiple groups can map to the same role (different rows).
///   → Two POSTs with different AdGroup but same RoleId both succeed.
///
/// AC4: On each login/token refresh, the API re-resolves AD group memberships
///     from the token claims and applies current mappings.
///   → AdGroupRoleResolver.MergeRolesAsync returns the mapped role.
///
/// AC5: Manually-assigned roles override group mappings (explicit always wins).
///   → If a user has an explicit Editor role, the group-mapped Editor is not
///     duplicated; the explicit row is kept.
///
/// AC6: Mapping changes take effect on the user's next login (not mid-session).
///   → This is architectural (roles are baked into the JWT); tested via the
///     resolver unit test: after adding a new mapping, a fresh resolve returns
///     the new role.
///
/// AC7: Audit log records creation/deletion (inside usp_AdGroupMapping_* since #165; see Issue165AcceptanceTests).
///   → Tested via stub audit log that captures WriteAsync calls.
/// </summary>
public class Issue67AcceptanceTests
{
    // ── AC1: GET endpoint returns 200 with a SystemAdmin JWT ─────────────────

    [Fact]
    public async Task AC1_GetMappings_Returns200_ForSystemAdmin()
    {
        await using var factory = new Issue67TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.SystemAdmin);

        var response = await client.GetAsync("/api/v1/admin/settings/ad-group-mappings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC1_GetMappings_Returns403_ForEditor()
    {
        await using var factory = new Issue67TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.Editor);

        var response = await client.GetAsync("/api/v1/admin/settings/ad-group-mappings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── AC2: POST creates a mapping ───────────────────────────────────────────

    [Fact]
    public async Task AC2_PostMapping_Returns201_WithId()
    {
        await using var factory = new Issue67TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/settings/ad-group-mappings",
            new CreateAdGroupMappingRequest("VA-CMS-Editors", RoleId: 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateMappingResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Id > 0);
    }

    [Fact]
    public async Task AC2_PostMapping_Returns400_WhenAdGroupEmpty()
    {
        await using var factory = new Issue67TestFactory();
        var client = factory.CreateAuthenticatedClient(CmsRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/settings/ad-group-mappings",
            new CreateAdGroupMappingRequest("", RoleId: 2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // AC3: Multiple groups → same role ─────────────────────────────────────

    [Fact]
    public async Task AC3_MultipleGroupsSameRole_BothInserted()
    {
        // Use the stub directly — no need for HTTP for this unit test
        var stub = new InMemoryAdGroupMappingRepository();

        // Add two different groups mapping to the same role
        await stub.UpsertAsync("VA-CMS-Editors",       roleId: 2, createdById: 1);
        await stub.UpsertAsync("VA-CMS-SeniorEditors",  roleId: 2, createdById: 1);

        var rows = (await stub.ListAsync()).ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(rows.All(r => r.RoleId == 2));
    }

    // ── AC4: Resolver returns mapped role from group claims ──────────────────

    [Fact]
    public async Task AC4_Resolver_Maps_Group_To_Role()
    {
        var stub = new InMemoryAdGroupMappingRepository();
        await stub.UpsertAsync("VA-CMS-Editors", roleId: 2, createdById: 1);
        stub.AddRoleName(2, "Editor");

        var resolver = new AdGroupRoleResolver(stub);
        var effective = (await resolver.MergeRolesAsync(
            adGroupNames: new[] { "VA-CMS-Editors" },
            explicitRoles: Array.Empty<UserRoleAssignment>())).ToList();

        Assert.Single(effective);
        Assert.Equal("Editor", effective[0].RoleName);
        Assert.Null(effective[0].SectionId); // group mappings are always global
    }

    [Fact]
    public async Task AC4_Resolver_NoMatchingGroup_ReturnsEmpty()
    {
        var stub = new InMemoryAdGroupMappingRepository();
        var resolver = new AdGroupRoleResolver(stub);

        var effective = (await resolver.MergeRolesAsync(
            adGroupNames: new[] { "VA-CMS-Unknown" },
            explicitRoles: Array.Empty<UserRoleAssignment>())).ToList();

        Assert.Empty(effective);
    }

    // ── AC5: Explicit roles override group-mapped roles ───────────────────────

    [Fact]
    public async Task AC5_ExplicitRole_Wins_Over_GroupMapped_Role()
    {
        var stub = new InMemoryAdGroupMappingRepository();
        await stub.UpsertAsync("VA-CMS-Editors", roleId: 2, createdById: 1);
        stub.AddRoleName(2, "Editor");

        var resolver = new AdGroupRoleResolver(stub);

        // User has an explicit Editor role (section-scoped)
        var explicitRoles = new[]
        {
            new UserRoleAssignment
            {
                RoleId = 2,
                RoleName = "Editor",
                SectionId = 7,
                SectionSlugPrefix = "/hr",
            },
        };

        var effective = (await resolver.MergeRolesAsync(
            adGroupNames: new[] { "VA-CMS-Editors" },
            explicitRoles: explicitRoles)).ToList();

        // Should have exactly one Editor — the explicit one (section-scoped)
        Assert.Single(effective);
        Assert.Equal(7, effective[0].SectionId); // explicit row preserved
    }

    [Fact]
    public async Task AC5_ExplicitRole_And_Different_GroupRole_Both_Present()
    {
        var stub = new InMemoryAdGroupMappingRepository();
        await stub.UpsertAsync("VA-CMS-SiteAdmins", roleId: 3, createdById: 1);
        stub.AddRoleName(3, "SiteAdmin");

        var resolver = new AdGroupRoleResolver(stub);

        // Explicit Editor + group-mapped SiteAdmin
        var explicitRoles = new[]
        {
            new UserRoleAssignment { RoleId = 2, RoleName = "Editor", SectionId = null },
        };

        var effective = (await resolver.MergeRolesAsync(
            adGroupNames: new[] { "VA-CMS-SiteAdmins" },
            explicitRoles: explicitRoles)).ToList();

        Assert.Equal(2, effective.Count);
        Assert.Contains(effective, r => r.RoleName == "Editor");
        Assert.Contains(effective, r => r.RoleName == "SiteAdmin");
    }

    // ── AC6: Mapping change takes effect on next token issuance ──────────────

    [Fact]
    public async Task AC6_NewMapping_ReturnedInSubsequentResolve()
    {
        var stub = new InMemoryAdGroupMappingRepository();
        var resolver = new AdGroupRoleResolver(stub);

        // Before mapping: no roles
        var before = (await resolver.MergeRolesAsync(
            adGroupNames: new[] { "VA-CMS-Editors" },
            explicitRoles: Array.Empty<UserRoleAssignment>())).ToList();
        Assert.Empty(before);

        // Admin adds a new mapping
        await stub.UpsertAsync("VA-CMS-Editors", roleId: 2, createdById: 1);
        stub.AddRoleName(2, "Editor");

        // Next resolve picks it up (simulating next login/refresh)
        var after = (await resolver.MergeRolesAsync(
            adGroupNames: new[] { "VA-CMS-Editors" },
            explicitRoles: Array.Empty<UserRoleAssignment>())).ToList();
        Assert.Single(after);
        Assert.Equal("Editor", after[0].RoleName);
    }

    // AC7 (audit rows on create / delete) moved into the stored procedures with #165;
    // Issue165AcceptanceTests covers it against a real database.
}

// ── Test factory ──────────────────────────────────────────────────────────────

public sealed class Issue67TestFactory : WebApplicationFactory<Program>
{
    private InMemoryAdGroupMappingRepository? _mappingStub;
    private Issue67AuditLogStub? _auditStub;

    public InMemoryAdGroupMappingRepository GetMappingStub()
        => _mappingStub ?? throw new InvalidOperationException("Factory not initialized");

    public Issue67AuditLogStub GetAuditStub()
        => _auditStub ?? throw new InvalidOperationException("Factory not initialized");

    public HttpClient CreateAuthenticatedClient(string roleName)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var svc = Services.GetRequiredService<IJwtService>();
        var user = new User
        {
            Id          = 1,
            ExternalId  = "oid-67",
            Email       = "admin@va.gov",
            DisplayName = "Admin",
            IsActive    = true,
        };
        var roles = new[] { new UserRoleAssignment { RoleId = 1, RoleName = roleName } };
        var token = svc.IssueAccessToken(user, roles);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _mappingStub = new InMemoryAdGroupMappingRepository();
        _auditStub   = new Issue67AuditLogStub();

        builder.UseSetting("SKIP_MIGRATIONS",  "true");
        builder.UseSetting("Jwt:SigningKey",    "issue-67-acceptance-key-32chars!!");
        builder.UseSetting("Jwt:Issuer",        "va-cms-api");
        builder.UseSetting("Jwt:Audience",      "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IUserRepository>(services,         _ => new Issue67UserStub());
            Replace<IDbMonitorRepository>(services,    _ => new Issue67DbMonitorStub());
            AuthTestStubs.UseInMemoryAuth(services);
            Replace<IAdGroupMappingRepository>(services, _ => _mappingStub);
            Replace<IAuditLogRepository>(services,     _ => _auditStub);
        });
    }

    private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory)
        where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddScoped<T>(factory);
    }
}

// ── In-memory test stubs ──────────────────────────────────────────────────────

/// <summary>
/// In-memory AdGroupMappingRepository for tests.
/// </summary>
public class InMemoryAdGroupMappingRepository : IAdGroupMappingRepository
{
    private readonly List<AdGroupRoleMappingRow> _rows = new();
    private readonly Dictionary<long, string>    _roleNames = new();
    private long _nextId = 1;

    public void AddRoleName(long roleId, string roleName) => _roleNames[roleId] = roleName;

    public Task<IEnumerable<AdGroupRoleMappingRow>> ListAsync()
        => Task.FromResult<IEnumerable<AdGroupRoleMappingRow>>(_rows.ToList());

    public Task<long> UpsertAsync(string adGroup, long roleId, long createdById)
    {
        var existing = _rows.FirstOrDefault(r =>
            string.Equals(r.AdGroup, adGroup, StringComparison.OrdinalIgnoreCase) && r.RoleId == roleId);
        if (existing != null)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            return Task.FromResult(existing.Id);
        }

        var id = _nextId++;
        _rows.Add(new AdGroupRoleMappingRow
        {
            Id          = id,
            AdGroup     = adGroup,
            RoleId      = roleId,
            RoleName    = _roleNames.TryGetValue(roleId, out var name) ? name : $"Role_{roleId}",
            CreatedById = createdById,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        });
        return Task.FromResult(id);
    }

    public Task DeleteAsync(long id)
    {
        _rows.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<(long RoleId, string RoleName)>> ResolveRolesForGroupsAsync(
        IEnumerable<string> adGroups)
    {
        var groupSet = new HashSet<string>(adGroups, StringComparer.OrdinalIgnoreCase);
        var result = _rows
            .Where(r => groupSet.Contains(r.AdGroup))
            .Select(r =>
            {
                // Use the current _roleNames lookup so tests can set names after Upsert
                var name = _roleNames.TryGetValue(r.RoleId, out var n) ? n : $"Role_{r.RoleId}";
                return (r.RoleId, name);
            })
            .Distinct()
            .ToList();
        return Task.FromResult<IEnumerable<(long, string)>>(result);
    }
}

internal sealed class Issue67UserStub : IUserRepository
{
    private readonly User _user = new()
    {
        Id          = 1,
        ExternalId  = "oid-67",
        Email       = "admin@va.gov",
        DisplayName = "Admin",
        IsActive    = true,
    };

    public Task<long> UpsertAsync(string externalId, string email, string displayName)
        => Task.FromResult(1L);
    public Task<User?> GetByIdAsync(long id)
        => Task.FromResult<User?>(id == 1 ? _user : null);
    public Task<User?> GetByExternalIdAsync(string externalId)
        => Task.FromResult<User?>(_user.ExternalId == externalId ? _user : null);
    public Task<IEnumerable<UserRoleAssignment>> GetRolesAsync(long userId)
        => Task.FromResult<IEnumerable<UserRoleAssignment>>(
        [
            new UserRoleAssignment { RoleId = 1, RoleName = CmsRoles.SystemAdmin },
        ]);
}

internal sealed class Issue67DbMonitorStub : IDbMonitorRepository
{
    public Task<IEnumerable<IndexFragmentationRow>> GetIndexFragmentationAsync()
        => Task.FromResult<IEnumerable<IndexFragmentationRow>>(Array.Empty<IndexFragmentationRow>());
    public Task<IEnumerable<TableSizeRow>> GetTableSizesAsync()
        => Task.FromResult<IEnumerable<TableSizeRow>>(Array.Empty<TableSizeRow>());
    public Task<IEnumerable<LongRunningQueryRow>> GetLongRunningQueriesAsync()
        => Task.FromResult<IEnumerable<LongRunningQueryRow>>(Array.Empty<LongRunningQueryRow>());
}

/// <summary>Captures audit writes for assertion in tests.</summary>
public class Issue67AuditLogStub : IAuditLogRepository
{
    public record AuditWrite(long? ActorId, string EntityType, long EntityId, string Action, string? DiffJson,
        string Outcome = AuditOutcome.Success);

    public List<AuditWrite> Writes { get; } = new();

    public Task WriteAsync(long? actorId, string entityType, long entityId, string action, string? diffJson = null,
        string outcome = AuditOutcome.Success)
    {
        lock (Writes)
            Writes.Add(new AuditWrite(actorId, entityType, entityId, action, diffJson, outcome));
        return Task.CompletedTask;
    }

    public Task<IEnumerable<AuditLog>> ListAsync(long? actorId = null, string? entityType = null,
        string? action = null, DateTime? fromDate = null, DateTime? toDate = null,
        int page = 1, int pageSize = 50)
        => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());

    public Task<AuditLogPage> ListPagedAsync(
        long? actorId = null, string? action = null, string? entityType = null,
        DateTime? fromDate = null, DateTime? toDate = null,
        int page = 1, int pageSize = 50,
        string? outcome = null, string? ipAddress = null)
        => Task.FromResult(new AuditLogPage { Items = [], TotalItems = 0, Page = page, PageSize = pageSize });

    public Task<IReadOnlyList<AuditLogRow>> ExportAsync(
        long? actorId = null, string? action = null, string? entityType = null,
        DateTime? fromDate = null, DateTime? toDate = null,
        string? outcome = null, string? ipAddress = null)
        => Task.FromResult<IReadOnlyList<AuditLogRow>>(Array.Empty<AuditLogRow>());
}
