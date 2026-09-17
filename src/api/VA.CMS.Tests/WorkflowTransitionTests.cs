using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Issue #37 — content status state machine wiring (BRD FR-WORKFLOW-01).
///
/// The allowed-edge table itself lives in usp_Workflow_Transition (V008); these tests
/// stub the repository with the same table and verify the API layer:
///   - each endpoint asks for the right from→to transition with the latest version id
///   - a rejected transition surfaces as 422 with the SP's plain-language message
///   - publish sets Status/PublishedVersionId; a second publish re-points without a transition
///   - PATCH persists a new Draft version; POST creates an initial version
/// </summary>
public class WorkflowTransitionTests
{
    private static readonly (string From, string To)[] AllowedEdges =
    [
        ("Draft", "InReview"), ("InReview", "Approved"), ("InReview", "Draft"),
        ("Approved", "Published"), ("Approved", "Draft"), ("Published", "Archived"),
        ("Published", "Approved"), ("Draft", "Published"), ("Approved", "Archived"),
    ];

    private static (WorkflowTestFactory Factory, HttpClient Client, WorkflowEntryStub Entries, Issue23ContentVersionStub Versions)
        Host(string initialStatus)
    {
        var entries  = new WorkflowEntryStub(initialStatus);
        var versions = new Issue23ContentVersionStub();
        var factory  = new WorkflowTestFactory(entries, versions);
        var client   = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");   // DevBypass → SystemAdmin
        return (factory, client, entries, versions);
    }

    [Theory]
    [InlineData("Draft",     "submit-review", "InReview")]
    [InlineData("InReview",  "approve",       "Approved")]
    [InlineData("Approved",  "publish",       "Published")]
    [InlineData("Draft",     "publish",       "Published")]
    [InlineData("Published", "unpublish",     "Approved")]
    [InlineData("Published", "archive",       "Archived")]
    public async Task ValidTransition_Returns204_And_RequestsExpectedEdge(string from, string endpoint, string to)
    {
        var (factory, client, entries, _) = Host(from);
        await using var _f = factory;

        var res = await client.PostAsync($"/api/v1/content/1/{endpoint}", null);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var call = Assert.Single(entries.Transitions);
        Assert.Equal((from, to), (call.From, call.To));
        Assert.Equal(1L, call.VersionId);   // latest version from the stub
    }

    [Theory]
    [InlineData("Draft",     "approve")]        // Draft → Approved not allowed
    [InlineData("Approved",  "submit-review")]  // Approved → InReview not allowed
    [InlineData("Draft",     "unpublish")]      // Draft → Approved not allowed
    [InlineData("Archived",  "publish")]        // Archived is terminal
    public async Task InvalidTransition_Returns422_WithMessage(string from, string endpoint)
    {
        var (factory, client, _, _) = Host(from);
        await using var _f = factory;

        var res = await client.PostAsync($"/api/v1/content/1/{endpoint}", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not permitted", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Return_RequiresComment_AndPassesItThrough()
    {
        var (factory, client, entries, _) = Host("InReview");
        await using var _f = factory;

        var missing = await client.PostAsJsonAsync("/api/v1/content/1/return", new { comment = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/v1/content/1/return", new { comment = "Please fix the title." });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("Please fix the title.", entries.Transitions.Last().Comment);
    }

    [Fact]
    public async Task Publish_SetsPublishedVersion_ThenRepublishRepointsWithoutTransition()
    {
        var (factory, client, entries, versions) = Host("Draft");
        await using var _f = factory;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/publish", null)).StatusCode);
        Assert.Equal("Published", entries.Entry.Status);
        Assert.Equal(1L, entries.Entry.PublishedVersionId);
        Assert.Single(entries.Transitions);

        // Save a new draft version, then publish again: no state transition, new version live.
        var patch = await client.PatchAsJsonAsync("/api/v1/content/1", new { fieldsJson = """{"title":"v2"}""" });
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        var latest = (await versions.ListWithAuthorAsync(1, 1, 1)).Single();
        Assert.Equal("""{"title":"v2"}""", latest.FieldsJson);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/publish", null)).StatusCode);
        Assert.Equal(latest.Id, entries.Entry.PublishedVersionId);
        Assert.Single(entries.Transitions);   // still just the first Draft→Published
    }

    [Fact]
    public async Task Patch_RejectsNonObjectFieldsJson()
    {
        var (factory, client, _, _) = Host("Draft");
        await using var _f = factory;

        var res = await client.PatchAsJsonAsync("/api/v1/content/1", new { fieldsJson = "[1,2]" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Create_ByContentTypeName_CreatesInitialVersion()
    {
        var (factory, client, _, versions) = Host("Draft");
        await using var _f = factory;

        var res = await client.PostAsJsonAsync("/api/v1/content", new
        {
            contentTypeName = "standard_page",
            slug = "hr/new-page",
            fieldsJson = """{"title":"New"}""",
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt64();
        var initial = (await versions.ListWithAuthorAsync(id, 1, 1)).Single();
        Assert.Equal("""{"title":"New"}""", initial.FieldsJson);
        Assert.Equal("Draft", initial.Status);
    }

    [Fact]
    public async Task Create_UnregisteredContentTypeName_Returns422()
    {
        var (factory, client, _, _) = Host("Draft");
        await using var _f = factory;

        var res = await client.PostAsJsonAsync("/api/v1/content", new { contentTypeName = "no_such_type", slug = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsLatestVersionFields_AndContentTypeName()
    {
        var (factory, client, _, _) = Host("Draft");
        await using var _f = factory;

        await client.PatchAsJsonAsync("/api/v1/content/1", new { fieldsJson = """{"title":"latest"}""" });
        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/1");

        Assert.Equal("""{"title":"latest"}""", body.GetProperty("fieldsJson").GetString());
        Assert.Equal("standard_page", body.GetProperty("contentTypeName").GetString());
        Assert.Equal(2L, body.GetProperty("latestVersionId").GetInt64());
    }

    // ── Stubs / host ─────────────────────────────────────────────────────────

    /// <summary>Entry stub with mutable status and a TransitionAsync that mirrors the SP's edge table.</summary>
    internal sealed class WorkflowEntryStub : Issue23ContentEntryStub
    {
        public ContentEntry Entry { get; }
        public List<(string From, string To, long VersionId, string? Comment)> Transitions { get; } = [];

        public WorkflowEntryStub(string status)
        {
            Entry = new ContentEntry
            {
                Id = 1, ContentTypeId = 1, ContentTypeName = "standard_page", Slug = "hr/test",
                Locale = "en-US", Status = status, OwnerId = 99, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
        }

        public override Task<ContentEntry?> GetByIdAsync(long id)
            => Task.FromResult<ContentEntry?>(id == Entry.Id ? Entry : null);

        public override Task UpdateAsync(ContentEntry entry)
        {
            Entry.Status             = entry.Status;
            Entry.PublishedVersionId = entry.PublishedVersionId;
            return Task.CompletedTask;
        }

        public override Task<(bool Success, string? ErrorMessage)> TransitionAsync(
            long entryId, long versionId, string fromStatus, string toStatus, long actorId, string? comment = null)
        {
            if (Entry.Status != fromStatus)
                return Task.FromResult<(bool, string?)>((false, $"Content is not in expected status {fromStatus}"));
            if (!AllowedEdges.Contains((fromStatus, toStatus)))
                return Task.FromResult<(bool, string?)>((false, $"Transition from {fromStatus} to {toStatus} is not permitted"));
            if (toStatus == "Draft" && fromStatus == "InReview" && string.IsNullOrWhiteSpace(comment))
                return Task.FromResult<(bool, string?)>((false, "A comment is required when returning content to Draft"));

            Entry.Status = toStatus;
            Transitions.Add((fromStatus, toStatus, versionId, comment));
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    internal sealed class WorkflowTestFactory : WebApplicationFactory<Program>
    {
        private readonly WorkflowEntryStub _entries;
        private readonly Issue23ContentVersionStub _versions;

        public WorkflowTestFactory(WorkflowEntryStub entries, Issue23ContentVersionStub versions)
        {
            _entries  = entries;
            _versions = versions;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("SKIP_MIGRATIONS",   "true");
            builder.UseSetting("Auth:Mode",         "DevBypass");
            builder.UseSetting("Auth:DevBypassAllowedUsers:0", "alice@va.gov");
            builder.UseSetting("Jwt:SigningKey",    "workflow-transition-tests-key-32!");
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
                Replace<IUserRepository>(services,           _ => new Issue68UserStub());
                Replace<IDbMonitorRepository>(services,      _ => new Issue68DbMonitorStub());
                AuthTestStubs.UseInMemoryAuth(services);
                Replace<IContentEntryRepository>(services,   _ => _entries);
                Replace<IContentVersionRepository>(services, _ => _versions);
                Replace<IContentTypeRepository>(services,    _ => new Issue23ContentTypeStub());
                Replace<IMediaAltTextGuardRepository>(services, _ => new NoMissingAltTextStub());
                Replace<INotificationRepository>(services,   _ => new Issue38NotificationStub());
            });
        }

        private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
            if (existing != null) services.Remove(existing);
            services.AddScoped<T>(factory);
        }
    }
}

/// <summary>Alt-text guard stub: nothing blocks publish.</summary>
internal sealed class NoMissingAltTextStub : IMediaAltTextGuardRepository
{
    public Task<IReadOnlyList<MissingAltTextAsset>> GetMissingAltTextAsync(long contentEntryId)
        => Task.FromResult<IReadOnlyList<MissingAltTextAsset>>([]);
}
