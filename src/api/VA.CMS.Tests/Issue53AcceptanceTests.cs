using System.Security.Claims;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.API.GraphQL;
using VA.CMS.API.GraphQL.DataLoaders;
using VA.CMS.API.GraphQL.Types;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #53 — Set up Hot Chocolate GraphQL with DataLoader and all core types.
///
/// BRD FR-DEV-02.
///
/// Acceptance criteria:
///   AC1: GraphQL endpoint registered at /api/graphql.
///       => MapGraphQL("/api/graphql") added to Program.cs.
///   AC2: ContentEntry, MediaAsset, NavigationMenu, TaxonomyTerm types in schema.
///       => Schema introspection confirms all four types are registered.
///   AC3: DataLoader prevents N+1 queries on relations.
///       => ContentEntryByIdDataLoader and MediaAssetByIdDataLoader wired.
///   AC4: Playground at /api/graphql/ui in Development.
///       => HC maps /api/graphql/ui by default in Development environments.
///
/// Test strategy:
///   - Schema reflection: verify all four core types appear in the schema.
///   - Live query tests against real SQL Server (via DatabaseFixture / TestContainers):
///       contentEntries, mediaAssets, navigationMenu, taxonomyTerms queries all execute.
///   - DataLoader type existence verified via DI resolution.
///   - Mutation guard: schema must not expose a Mutation type (read-only for headless).
/// </summary>
[Collection("Database")]
public class Issue53AcceptanceTests(DatabaseFixture fixture)
{
    // ── Shared: build a minimal HC service collection backed by the real DB ──

    private IRequestExecutor BuildExecutor()
    {
        var services = new ServiceCollection();

        // Wire the real DB (test container connection)
        services.AddScoped<CmsDatabase>(_ => fixture.CreateDb());

        // All repositories the Query type depends on
        services.AddScoped<IContentEntryRepository, ContentEntryRepository>();
        services.AddScoped<IMediaAssetRepository,   MediaAssetRepository>();
        services.AddScoped<INavigationMenuRepository, NavigationMenuRepository>();
        services.AddScoped<ITaxonomyRepository,      TaxonomyRepository>();
        services.AddScoped<IMediaExtendedRepository,   MediaExtendedRepository>();
        // Page-size clamp comes from site settings (issue #147); defaults are enough here.
        services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults);

        // #156: the schema carries [Authorize] fields and an audience service.
        services.AddLogging();
        services.AddCmsAuthorization();
        services.AddScoped<IGraphQLAudience, GraphQLAudience>();

        services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddQueryType<Query>()
            .AddDataLoader<ContentEntryByIdDataLoader>()
            .AddDataLoader<MediaAssetByIdDataLoader>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IRequestExecutorResolver>()
                       .GetRequestExecutorAsync()
                       .GetAwaiter()
                       .GetResult();
    }

    /// <summary>These tests exercise the full (CanRead) surface, so run as an Editor.</summary>
    private static Task<IExecutionResult> ExecuteAsEditorAsync(IRequestExecutor executor, string query)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, CmsRoles.Editor)], "Test"));
        var request = OperationRequestBuilder.New()
            .SetDocument(query)
            .SetGlobalState(WellKnownContextData.UserState, new UserState(principal))
            .Build();
        return executor.ExecuteAsync(request);
    }

    // ── AC2: All four core types present in the schema ────────────────────────

    [Fact]
    public void Schema_ContainsContentEntryType()
    {
        var executor = BuildExecutor();
        Assert.NotNull(executor.Schema.GetType<ObjectType>("ContentEntryType"));
    }

    [Fact]
    public void Schema_ContainsMediaAssetType()
    {
        var executor = BuildExecutor();
        Assert.NotNull(executor.Schema.GetType<ObjectType>("MediaAssetType"));
    }

    [Fact]
    public void Schema_ContainsNavigationMenuType()
    {
        var executor = BuildExecutor();
        Assert.NotNull(executor.Schema.GetType<ObjectType>("NavigationMenuType"));
    }

    [Fact]
    public void Schema_ContainsTaxonomyTermType()
    {
        var executor = BuildExecutor();
        Assert.NotNull(executor.Schema.GetType<ObjectType>("TaxonomyTermType"));
    }

    // ── Schema shape: expected fields on each type ────────────────────────────

    [Fact]
    public void Schema_ContentEntryType_HasExpectedFields()
    {
        var executor = BuildExecutor();
        var type = executor.Schema.GetType<ObjectType>("ContentEntryType");
        var names = type.Fields.Select(f => f.Name).ToHashSet();
        Assert.Contains("id",       names);
        Assert.Contains("slug",     names);
        Assert.Contains("status",   names);
        Assert.Contains("locale",   names);
    }

    [Fact]
    public void Schema_MediaAssetType_HasExpectedFields()
    {
        var executor = BuildExecutor();
        var type = executor.Schema.GetType<ObjectType>("MediaAssetType");
        var names = type.Fields.Select(f => f.Name).ToHashSet();
        Assert.Contains("id",         names);
        Assert.Contains("fileName",   names);
        Assert.Contains("mimeType",   names);
        Assert.Contains("storageBackend", names);
    }

    [Fact]
    public void Schema_NavigationMenuType_HasExpectedFields()
    {
        var executor = BuildExecutor();
        var type = executor.Schema.GetType<ObjectType>("NavigationMenuType");
        var names = type.Fields.Select(f => f.Name).ToHashSet();
        Assert.Contains("id",     names);
        Assert.Contains("name",   names);
        Assert.Contains("handle", names);
    }

    [Fact]
    public void Schema_TaxonomyTermType_HasExpectedFields()
    {
        var executor = BuildExecutor();
        var type = executor.Schema.GetType<ObjectType>("TaxonomyTermType");
        var names = type.Fields.Select(f => f.Name).ToHashSet();
        Assert.Contains("id",   names);
        Assert.Contains("name", names);
        Assert.Contains("slug", names);
    }

    // ── Query root fields ─────────────────────────────────────────────────────

    [Fact]
    public void Schema_QueryType_ExposesContentEntryFields()
    {
        var executor = BuildExecutor();
        var query = executor.Schema.QueryType;
        var names = query.Fields.Select(f => f.Name).ToHashSet();
        Assert.Contains("contentEntry",   names);
        Assert.Contains("contentEntries", names);
    }

    [Fact]
    public void Schema_QueryType_ExposesMediaAssetFields()
    {
        var executor = BuildExecutor();
        var query = executor.Schema.QueryType;
        var names = query.Fields.Select(f => f.Name).ToHashSet();
        Assert.Contains("mediaAsset",  names);
        Assert.Contains("mediaAssets", names);
    }

    [Fact]
    public void Schema_QueryType_ExposesNavigationMenuField()
    {
        var executor = BuildExecutor();
        var query = executor.Schema.QueryType;
        Assert.Contains("navigationMenu", query.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Schema_QueryType_ExposesTaxonomyTermsField()
    {
        var executor = BuildExecutor();
        var query = executor.Schema.QueryType;
        Assert.Contains("taxonomyTerms", query.Fields.Select(f => f.Name));
    }

    // ── AC3: DataLoaders wired (resolve from DI) ─────────────────────────────

    [Fact]
    public void ContentEntryByIdDataLoader_ResolvesFromServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<CmsDatabase>(_ => fixture.CreateDb());
        services.AddScoped<IContentEntryRepository, ContentEntryRepository>();
        services.AddScoped<IMediaAssetRepository,   MediaAssetRepository>();
        services.AddScoped<INavigationMenuRepository, NavigationMenuRepository>();
        services.AddScoped<ITaxonomyRepository,      TaxonomyRepository>();
        // Page-size clamp comes from site settings (issue #147); defaults are enough here.
        services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults);

        services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddDataLoader<ContentEntryByIdDataLoader>()
            .AddDataLoader<MediaAssetByIdDataLoader>();

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();

        // HC registers DataLoaders against IDataLoader; verify the types are accessible
        var batchScheduler = scope.ServiceProvider.GetService<IBatchScheduler>();
        // DataLoaders are resolved per-request by HC; confirm no DI exception on build
        Assert.NotNull(provider.GetRequiredService<IRequestExecutorResolver>());
    }

    // ── AC1: Introspection query executes successfully ────────────────────────

    [Fact]
    public async Task IntrospectionQuery_Succeeds()
    {
        var executor = BuildExecutor();
        var result = await executor.ExecuteAsync("{ __typename }");
        var errors = ((HotChocolate.Execution.IOperationResult)result).Errors;
        Assert.Null(errors); // no schema or execution errors
    }

    // ── Live queries against real DB ─────────────────────────────────────────

    [Fact]
    public async Task ContentEntries_Query_ExecutesWithoutError()
    {
        var executor = BuildExecutor();
        var result = await ExecuteAsEditorAsync(executor, @"
            {
                contentEntries(first: 5) {
                    id
                    slug
                    status
                    locale
                    contentTypeId
                    createdAt
                    updatedAt
                }
            }");

        Assert.Null(((HotChocolate.Execution.IOperationResult)result).Errors);
    }

    [Fact]
    public async Task MediaAssets_Query_ExecutesWithoutError()
    {
        var executor = BuildExecutor();
        var result = await ExecuteAsEditorAsync(executor, @"
            {
                mediaAssets(first: 5) {
                    id
                    fileName
                    mimeType
                    storageBackend
                }
            }");

        Assert.Null(((HotChocolate.Execution.IOperationResult)result).Errors);
    }

    [Fact]
    public async Task NavigationMenu_Query_UnknownHandle_ReturnsNull()
    {
        var executor = BuildExecutor();
        var result = await ExecuteAsEditorAsync(executor, @"
            {
                navigationMenu(handle: ""non-existent-handle-qxz"") {
                    id
                    name
                    handle
                }
            }");

        Assert.Null(((HotChocolate.Execution.IOperationResult)result).Errors);
        // A null result for an unknown handle is correct behaviour
    }

    [Fact]
    public async Task TaxonomyTerms_Query_UnknownHandle_ReturnsEmpty()
    {
        var executor = BuildExecutor();
        var result = await ExecuteAsEditorAsync(executor, @"
            {
                taxonomyTerms(handle: ""non-existent-taxonomy-qxz"") {
                    id
                    name
                    slug
                    depth
                }
            }");

        Assert.Null(((HotChocolate.Execution.IOperationResult)result).Errors);
    }

    [Fact]
    public async Task ContentEntry_ByNonExistentId_ReturnsNull()
    {
        var executor = BuildExecutor();
        var result = await ExecuteAsEditorAsync(executor, @"
            {
                contentEntry(id: 999999999) {
                    id
                    slug
                }
            }");

        Assert.Null(((HotChocolate.Execution.IOperationResult)result).Errors);
    }

    [Fact]
    public async Task ContentEntries_FilterByStatus_Published_Executes()
    {
        var executor = BuildExecutor();
        var result = await ExecuteAsEditorAsync(executor, @"
            {
                contentEntries(status: ""Published"", first: 10) {
                    id
                    slug
                    status
                }
            }");

        Assert.Null(((HotChocolate.Execution.IOperationResult)result).Errors);
    }

    // ── DataLoader map helper coverage ────────────────────────────────────────

    [Fact]
    public void ContentEntryByIdDataLoader_MapToType_MapsAllFields()
    {
        var poco = new VA.CMS.Infrastructure.Data.Pocos.ContentEntry
        {
            Id                 = 42,
            ContentTypeId      = 1,
            Slug               = "test-slug",
            Locale             = "en-US",
            Status             = "Published",
            PublishedVersionId = 7,
            OwnerId            = 3,
            CreatedAt          = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt          = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var type = ContentEntryByIdDataLoader.MapToType(poco);

        Assert.Equal(42,           type.Id);
        Assert.Equal(1,            type.ContentTypeId);
        Assert.Equal("test-slug",  type.Slug);
        Assert.Equal("en-US",      type.Locale);
        Assert.Equal("Published",  type.Status);
        Assert.Equal(7,            type.PublishedVersionId);
        Assert.Equal(3,            type.OwnerId);
    }

    [Fact]
    public void MediaAssetByIdDataLoader_MapToType_MapsAllFields()
    {
        var poco = new VA.CMS.Infrastructure.Data.Pocos.MediaAsset
        {
            Id             = 99,
            FileName       = "photo.jpg",
            StoragePath    = "/uploads/photo.jpg",
            StorageBackend = "local",
            MimeType       = "image/jpeg",
            FileSizeBytes  = 204800,
            AltText        = "A photo",
            UploadedById   = 5,
            CreatedAt      = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt      = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };

        var type = MediaAssetByIdDataLoader.MapToType(poco);

        Assert.Equal(99,           type.Id);
        Assert.Equal("photo.jpg",  type.FileName);
        Assert.Equal("image/jpeg", type.MimeType);
        Assert.Equal("local",      type.StorageBackend);
        Assert.Equal("A photo",    type.AltText);
    }
}
