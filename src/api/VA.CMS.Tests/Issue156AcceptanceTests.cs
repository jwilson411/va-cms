using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using PetaPoco;
using VA.CMS.API.Auth;
using VA.CMS.API.GraphQL;
using VA.CMS.API.GraphQL.DataLoaders;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #156: the GraphQL schema has two audiences.
///
///   - Anonymous: contentEntries is pinned to Published at the repository call
///     (status argument ignored), contentEntry(id) hides non-published entries,
///     mediaAssets is denied, mediaAsset(id) only for assets used by published
///     content, ownerId / uploadedById / storagePath are denied.
///   - CanRead JWT: full surface.
///   - Depth limit and introspection switch are enforced by the executor.
///
/// Repositories are stubbed so the tests need no database; the executor is built
/// exactly as Program.cs builds it (authorization + audience + limits).
/// </summary>
public class Issue156AcceptanceTests
{
    // ── contentEntries ────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_ContentEntries_Draft_Returns_Only_Published()
    {
        var (executor, entries) = Build();

        var doc = await ExecuteAsync(executor, user: null,
            @"{ contentEntries(status: ""Draft"") { id slug status } }");

        Assert.Null(doc.RootElement.GetPropertyOrNull("errors"));
        Assert.Equal("Published", entries.LastStatusFilter);   // pinned before the repository (SP) call
        var rows = doc.RootElement.GetProperty("data").GetProperty("contentEntries").EnumerateArray().ToList();
        Assert.All(rows, r => Assert.Equal("Published", r.GetProperty("status").GetString()));
        Assert.Single(rows);
    }

    [Fact]
    public async Task Authenticated_ContentEntries_Draft_Returns_Drafts()
    {
        var (executor, entries) = Build();

        var doc = await ExecuteAsync(executor, user: Editor(),
            @"{ contentEntries(status: ""Draft"") { id slug status ownerId } }");

        Assert.Null(doc.RootElement.GetPropertyOrNull("errors"));
        Assert.Equal("Draft", entries.LastStatusFilter);
        var rows = doc.RootElement.GetProperty("data").GetProperty("contentEntries").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("Draft", rows[0].GetProperty("status").GetString());
        Assert.Equal(99, rows[0].GetProperty("ownerId").GetInt64());
    }

    [Fact]
    public async Task Anonymous_ContentEntry_By_Id_Hides_Draft_And_Shows_Published()
    {
        var (executor, _) = Build();

        var draft = await ExecuteAsync(executor, null, @"{ contentEntry(id: 1) { id status } }");
        Assert.True(draft.RootElement.GetProperty("data").GetProperty("contentEntry").ValueKind == JsonValueKind.Null);

        var published = await ExecuteAsync(executor, null, @"{ contentEntry(id: 2) { id status } }");
        Assert.Equal("Published", published.RootElement.GetProperty("data").GetProperty("contentEntry").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Authenticated_ContentEntry_By_Id_Shows_Draft()
    {
        var (executor, _) = Build();

        var draft = await ExecuteAsync(executor, Editor(), @"{ contentEntry(id: 1) { id status } }");

        Assert.Equal("Draft", draft.RootElement.GetProperty("data").GetProperty("contentEntry").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Anonymous_OwnerId_Is_Denied()
    {
        var (executor, _) = Build();

        var doc = await ExecuteAsync(executor, null, @"{ contentEntry(id: 2) { id ownerId } }");

        var errors = doc.RootElement.GetPropertyOrNull("errors");
        Assert.NotNull(errors);
        Assert.Contains("AUTH_NOT_AUTHENTICATED", errors.Value.ToString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").GetProperty("contentEntry").ValueKind);
    }

    // ── media ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_MediaAssets_List_Is_Denied()
    {
        var (executor, _) = Build();

        var doc = await ExecuteAsync(executor, null, @"{ mediaAssets { id fileName } }");

        Assert.Contains("AUTH_NOT_AUTHENTICATED", doc.RootElement.GetPropertyOrNull("errors")!.Value.ToString());
    }

    [Fact]
    public async Task Authenticated_MediaAssets_List_Allowed()
    {
        var (executor, _) = Build();

        var doc = await ExecuteAsync(executor, Editor(), @"{ mediaAssets { id fileName uploadedById storagePath } }");

        Assert.Null(doc.RootElement.GetPropertyOrNull("errors"));
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("mediaAssets").GetArrayLength());
    }

    [Fact]
    public async Task Anonymous_MediaAsset_By_Id_Only_When_Published_Content_References_It()
    {
        var (executor, _) = Build();

        var used   = await ExecuteAsync(executor, null, @"{ mediaAsset(id: 10) { id fileName } }");
        var unused = await ExecuteAsync(executor, null, @"{ mediaAsset(id: 11) { id fileName } }");

        Assert.Equal("hero.jpg", used.RootElement.GetProperty("data").GetProperty("mediaAsset").GetProperty("fileName").GetString());
        Assert.Equal(JsonValueKind.Null, unused.RootElement.GetProperty("data").GetProperty("mediaAsset").ValueKind);
    }

    [Fact]
    public async Task Anonymous_MediaAsset_Hidden_Fields_Denied()
    {
        var (executor, _) = Build();

        var doc = await ExecuteAsync(executor, null, @"{ mediaAsset(id: 10) { id uploadedById } }");

        Assert.Contains("AUTH_NOT_AUTHENTICATED", doc.RootElement.GetPropertyOrNull("errors")!.Value.ToString());
    }

    // ── limits ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_Deeper_Than_Limit_Is_Rejected()
    {
        // The schema has no nested object relations yet, so prove the rule is wired by
        // building the executor with depth 1 and sending a depth-2 selection.
        var (executor, _) = Build(maxDepth: 1);

        var doc = await ExecuteAsync(executor, Editor(), @"{ contentEntries { id } }");

        var errors = doc.RootElement.GetPropertyOrNull("errors");
        Assert.NotNull(errors);
        Assert.Contains("depth", errors.Value.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configured_Depth_Limit_Covers_The_Schema()
        => Assert.True(GraphQLLimits.MaxExecutionDepth >= 3);

    [Fact]
    public async Task Introspection_Disabled_Outside_Development()
    {
        var (executor, _) = Build(isDevelopment: false);

        var doc = await ExecuteAsync(executor, Editor(), @"{ __schema { types { name } } }");

        Assert.NotNull(doc.RootElement.GetPropertyOrNull("errors"));
    }

    [Fact]
    public async Task Introspection_Enabled_In_Development()
    {
        var (executor, _) = Build(isDevelopment: true);

        var doc = await ExecuteAsync(executor, Editor(), @"{ __schema { queryType { name } } }");

        Assert.Null(doc.RootElement.GetPropertyOrNull("errors"));
    }

    // ── end to end through the host (JWT bearer → UserState) ──────────────────

    [Fact]
    public async Task Http_Anonymous_Gets_Published_Only_And_Bearer_Gets_Drafts()
    {
        await using var factory = new Issue156HostFactory();
        var query = new { query = @"{ contentEntries(status: ""Draft"") { id status } }" };

        var anon = factory.CreateClient();
        var anonDoc = JsonDocument.Parse(await (await anon.PostAsJsonAsync("/api/graphql", query)).Content.ReadAsStringAsync());
        var anonRows = anonDoc.RootElement.GetProperty("data").GetProperty("contentEntries").EnumerateArray().ToList();
        Assert.Single(anonRows);
        Assert.Equal("Published", anonRows[0].GetProperty("status").GetString());

        var editor = factory.CreateAuthenticatedClient(CmsRoles.Editor);
        var edDoc = JsonDocument.Parse(await (await editor.PostAsJsonAsync("/api/graphql", query)).Content.ReadAsStringAsync());
        var edRows = edDoc.RootElement.GetProperty("data").GetProperty("contentEntries").EnumerateArray().ToList();
        Assert.Single(edRows);
        Assert.Equal("Draft", edRows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Http_RoleLess_Bearer_Is_Treated_As_Anonymous()
    {
        await using var factory = new Issue156HostFactory();
        var client = factory.CreateAuthenticatedClient(roleName: null);

        var doc = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/graphql",
            new { query = @"{ mediaAssets { id } }" })).Content.ReadAsStringAsync());

        Assert.NotNull(doc.RootElement.GetPropertyOrNull("errors"));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ClaimsPrincipal Editor()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, CmsRoles.Editor)], "Test"));

    private static async Task<JsonDocument> ExecuteAsync(IRequestExecutor executor, ClaimsPrincipal? user, string query)
    {
        // The HTTP transport always supplies a UserState — an unauthenticated identity for anonymous callers.
        var builder = OperationRequestBuilder.New()
            .SetDocument(query)
            .SetGlobalState(WellKnownContextData.UserState,
                new UserState(user ?? new ClaimsPrincipal(new ClaimsIdentity())));
        var result = await executor.ExecuteAsync(builder.Build());
        return JsonDocument.Parse(result.ToJson());
    }

    /// <summary>Mirrors the Program.cs GraphQL registration against in-memory repositories.</summary>
    private static (IRequestExecutor Executor, EntryStub Entries) Build(bool isDevelopment = false, int maxDepth = GraphQLLimits.MaxExecutionDepth)
    {
        var services = new ServiceCollection();
        var entries  = new EntryStub();

        services.AddScoped<IContentEntryRepository>(_ => entries);
        services.AddScoped<IMediaAssetRepository,     MediaStub>();
        services.AddScoped<IMediaExtendedRepository,  UsageStub>();
        services.AddScoped<INavigationMenuRepository>(_ => throw new NotSupportedException());
        services.AddScoped<ITaxonomyRepository>(_ => throw new NotSupportedException());
        services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults);
        services.AddLogging();
        services.AddCmsAuthorization();
        services.AddScoped<IGraphQLAudience, GraphQLAudience>();

        services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddQueryType<Query>()
            .AddDataLoader<ContentEntryByIdDataLoader>()
            .AddDataLoader<MediaAssetByIdDataLoader>()
            .AddMaxExecutionDepthRule(maxDepth, skipIntrospectionFields: true)
            .ModifyCostOptions(o => { o.MaxFieldCost = GraphQLLimits.MaxFieldCost; o.MaxTypeCost = GraphQLLimits.MaxTypeCost; })
            .ModifyRequestOptions(o => o.ExecutionTimeout = GraphQLLimits.ExecutionTimeout)
            .DisableIntrospection(!isDevelopment);

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync().GetAwaiter().GetResult();
        return (executor, entries);
    }

    /// <summary>One Draft (id 1, owner 99) and one Published (id 2) entry; records the status filter it was asked for.</summary>
    internal sealed class EntryStub : Issue23ContentEntryStub
    {
        public string? LastStatusFilter { get; private set; }

        private static readonly ContentEntry Draft = new()
            { Id = 1, ContentTypeId = 1, Slug = "draft-one", Status = "Draft", OwnerId = 99, Locale = "en-US" };
        private static readonly ContentEntry Published = new()
            { Id = 2, ContentTypeId = 1, Slug = "live-one", Status = "Published", OwnerId = 99, Locale = "en-US", PublishedVersionId = 5 };

        public override Task<ContentEntry?> GetByIdAsync(long id)
            => Task.FromResult<ContentEntry?>(id switch { 1 => Draft, 2 => Published, _ => null });

        public override Task<Page<ContentEntry>> ListAsync(int page, int pageSize, string? status = null, long? contentTypeId = null)
        {
            LastStatusFilter = status;
            var items = new[] { Draft, Published }
                .Where(e => status is null || string.Equals(e.Status, status, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(new Page<ContentEntry> { CurrentPage = page, ItemsPerPage = pageSize, Items = items, TotalItems = items.Count });
        }
    }

    internal sealed class MediaStub : IMediaAssetRepository
    {
        private static readonly MediaAsset Used   = new() { Id = 10, FileName = "hero.jpg",  MimeType = "image/jpeg", StoragePath = "2026/hero.jpg", UploadedById = 7 };
        private static readonly MediaAsset Unused = new() { Id = 11, FileName = "draft.png", MimeType = "image/png",  StoragePath = "2026/draft.png", UploadedById = 7 };
        public Task<MediaAsset?> GetByIdAsync(long id) => Task.FromResult<MediaAsset?>(id switch { 10 => Used, 11 => Unused, _ => null });
        public Task<Page<MediaAsset>> ListAsync(int page, int pageSize, string? mimeTypePrefix = null, string? searchTerm = null)
            => Task.FromResult(new Page<MediaAsset> { Items = [Used, Unused], TotalItems = 2, CurrentPage = page, ItemsPerPage = pageSize });
        public Task<long> CreateAsync(MediaAsset asset) => Task.FromResult(0L);
        public Task UpdateAsync(MediaAsset asset) => Task.CompletedTask;
        public Task UpdateWebPPathAsync(long id, string webPStoragePath) => Task.CompletedTask;
    }

    /// <summary>Asset 10 is used by a Published entry and a Draft; asset 11 only by a Draft.</summary>
    internal sealed class UsageStub : IMediaExtendedRepository
    {
        public Task<IEnumerable<MediaUsageDetail>> GetUsageAsync(long mediaAssetId)
            => Task.FromResult<IEnumerable<MediaUsageDetail>>(mediaAssetId switch
            {
                10 => [new MediaUsageDetail { ContentEntryId = 2, Status = "Published" }, new MediaUsageDetail { ContentEntryId = 1, Status = "Draft" }],
                11 => [new MediaUsageDetail { ContentEntryId = 1, Status = "Draft" }],
                _  => [],
            });
        public Task SetVirusScanResultAsync(long assetId, bool passed) => Task.CompletedTask;
        public Task<IEnumerable<MediaUsageWithTitle>> GetUsageWithTitleAsync(long mediaAssetId) => Task.FromResult<IEnumerable<MediaUsageWithTitle>>([]);
        public Task<int> SafeDeleteAsync(long assetId) => Task.FromResult(0);
        public Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName) => Task.CompletedTask;
        public Task DeleteUsageForEntryAsync(long contentEntryId) => Task.CompletedTask;
    }
}

/// <summary>Full host with the GraphQL feature on and the same stubs as the executor tests.</summary>
public sealed class Issue156HostFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    public HttpClient CreateAuthenticatedClient(string? roleName)
    {
        var client = CreateClient();
        var jwt    = Services.GetRequiredService<IJwtService>();
        var user   = new User { Id = 1, ExternalId = "x", Email = "x@va.gov", DisplayName = "X", IsActive = true };
        var roles  = roleName is null ? Array.Empty<UserRoleAssignment>() : [new UserRoleAssignment { RoleId = 1, RoleName = roleName }];
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt.IssueAccessToken(user, roles));
        return client;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("SKIP_MIGRATIONS", "true");
        builder.UseSetting("Jwt:SigningKey",  "issue-156-acceptance-key-32chars!");
        builder.UseSetting("Jwt:Issuer",      "va-cms-api");
        builder.UseSetting("Jwt:Audience",    "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IContentEntryRepository>(services,  _ => new Issue156AcceptanceTests.EntryStub());
            Replace<IMediaAssetRepository>(services,    _ => new Issue156AcceptanceTests.MediaStub());
            Replace<IMediaExtendedRepository>(services, _ => new Issue156AcceptanceTests.UsageStub());
            Replace<IDbMonitorRepository>(services,     _ => new AuthTestStubs.StubDbMonitorRepository());
            services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults.With(SiteSettingKeys.FeatureGraphQl, true));
        });
    }

    private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
    {
        foreach (var existing in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(existing);
        services.AddScoped<T>(factory);
    }
}

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v : null;
}
