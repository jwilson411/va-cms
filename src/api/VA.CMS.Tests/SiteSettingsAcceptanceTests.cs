using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Controllers;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for epic #141 — runtime configuration and feature flags live in the database.
///
///   #142 SiteSetting table, SPs, definitions sync, cached ISiteSettingsService
///   #143 Admin settings API (list / set / reset, audit) + client / public read endpoints
///   #144 Feature flags read at request time (GraphQL 404, media upload 403, webhooks no-op)
///   #145 Media limits (max upload bytes, MIME allow-list) from settings
///   #146 Auth token lifetimes from settings
///   #147 Workflow return-comment rule and pagination clamps from settings
///
/// Migration: V040 adds the SiteSetting table, 4 SPs, fn_SiteSetting_GetBool and re-creates
/// usp_Workflow_Transition.
/// </summary>
[Collection("Database")]
public class SiteSettingsAcceptanceTests(DatabaseFixture fixture)
{
    private ISiteSettingRepository Repo() => new SiteSettingRepository(fixture.ConnectionString);

    /// <summary>A loaded service (definitions synced + snapshot read) against the shared test DB.</summary>
    private async Task<SiteSettingsService> LoadedServiceAsync()
    {
        var svc = new SiteSettingsService(Repo());
        await svc.RefreshAsync();
        return svc;
    }

    private async Task ResetAsync(string key)
    {
        await Repo().ResetAsync(key, 0);
    }

    // ── #142: V040 structural ─────────────────────────────────────────────────

    [Theory]
    [InlineData("usp_SiteSetting_List")]
    [InlineData("usp_SiteSetting_EnsureDefinition")]
    [InlineData("usp_SiteSetting_SetValue")]
    [InlineData("usp_SiteSetting_Reset")]
    [InlineData("usp_Workflow_Transition")]
    public async Task V040_StoredProcedure_Exists(string name)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.objects WHERE [name] = @Name AND [type] = 'P';";
        cmd.Parameters.AddWithValue("@Name", name);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task V040_SiteSettingTable_And_GetBoolFunction_Exist()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT (SELECT COUNT(1) FROM sys.tables  WHERE [name] = 'SiteSetting')
                 + (SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'UX_SiteSetting_Key')
                 + (SELECT COUNT(1) FROM sys.objects WHERE [name] = 'fn_SiteSetting_GetBool' AND [type] = 'FN');";
        Assert.Equal(3, (int)(await cmd.ExecuteScalarAsync())!);
    }

    // ── #142: definitions sync + typed reads ──────────────────────────────────

    [Fact]
    public async Task EveryDeclaredSetting_IsProvisioned_WithItsCodeDefault()
    {
        var svc  = await LoadedServiceAsync();
        var rows = svc.Snapshot.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var d in SiteSettingDefinitions.All)
        {
            Assert.True(rows.ContainsKey(d.Key), $"{d.Key} missing from [SiteSetting]");
            Assert.Equal(d.Default,                         rows[d.Key].DefaultValue);
            Assert.Equal(d.Type.ToString().ToLowerInvariant(), rows[d.Key].DataType);
            Assert.Equal(d.Category,                        rows[d.Key].Category);
            Assert.Equal(d.Scope.ToString(),                rows[d.Key].Scope);
        }
        Assert.NotNull(svc.LoadedAtUtc);
    }

    [Fact]
    public void Definitions_HaveUniqueKeys_AndParseableDefaults()
    {
        Assert.Equal(SiteSettingDefinitions.All.Count,
            SiteSettingDefinitions.All.Select(d => d.Key.ToLowerInvariant()).Distinct().Count());

        foreach (var d in SiteSettingDefinitions.All)
            Assert.Null(SiteSettingParser.Validate(d, d.Default));
    }

    [Fact]
    public void Defaults_MatchThePreviouslyHardCodedValues()
    {
        var s = StaticSiteSettings.Defaults;
        Assert.Equal(104_857_600, s.GetLong(SiteSettingKeys.MediaMaxUploadBytes));
        Assert.Equal(1920,        s.GetInt(SiteSettingKeys.MediaImageMaxWidthPx));
        Assert.Equal(80,          s.GetInt(SiteSettingKeys.MediaWebpQuality));
        Assert.Equal(15,          s.GetInt(SiteSettingKeys.AuthAccessTokenMinutes));
        Assert.Equal(8,           s.GetInt(SiteSettingKeys.AuthRefreshTokenHours));
        Assert.Equal(60,          s.GetInt(SiteSettingKeys.AuthPreviewTokenMinutes));
        Assert.Equal(60,          s.GetInt(SiteSettingKeys.WorkflowScheduledPublishPollSeconds));
        Assert.Equal(3,           s.GetInt(SiteSettingKeys.WebhooksMaxAttempts));
        Assert.Equal(new[] { 5, 25 }, s.GetIntList(SiteSettingKeys.WebhooksRetryDelaysSeconds));
        Assert.Equal(15,          s.GetInt(SiteSettingKeys.WebhooksTimeoutSeconds));
        Assert.Equal(100,         s.GetInt(SiteSettingKeys.SearchMaxPageSize));
        Assert.Equal(200,         s.GetInt(SiteSettingKeys.ApiMaxPageSize));
        Assert.Equal("Department of Veterans Affairs", s.GetString(SiteSettingKeys.SiteTitle));
        Assert.True(s.GetBool(SiteSettingKeys.FeatureWebhooks));
        Assert.False(s.GetBool(SiteSettingKeys.FeatureSwaggerUi));
        Assert.Contains("image/png", s.GetStringList(SiteSettingKeys.MediaAllowedMimeTypes));
    }

    [Fact]
    public async Task SetValue_ThenRefresh_IsVisibleToTypedGetters_AndResetRestoresDefault()
    {
        var key = SiteSettingKeys.MediaImageMaxWidthPx;
        try
        {
            var svc = await LoadedServiceAsync();
            Assert.Equal(1920, svc.GetInt(key));

            Assert.True(await Repo().SetValueAsync(key, "1280", 0));
            Assert.Equal(1920, svc.GetInt(key));        // snapshot until refreshed
            await svc.RefreshAsync();
            Assert.Equal(1280, svc.GetInt(key));

            Assert.True(await Repo().ResetAsync(key, 0));
            await svc.RefreshAsync();
            Assert.Equal(1920, svc.GetInt(key));
            Assert.Null(svc.Snapshot.Single(r => r.Key == key).Value);
        }
        finally { await ResetAsync(key); }
    }

    [Fact]
    public async Task SetValue_UnknownKey_ReturnsFalse()
    {
        Assert.False(await Repo().SetValueAsync("nope.notAKey", "x", 0));
        Assert.False(await Repo().ResetAsync("nope.notAKey", 0));
    }

    [Fact]
    public async Task Service_WithUnreachableDatabase_AnswersWithCodeDefaults_WithoutThrowing()
    {
        var svc = new SiteSettingsService(new SiteSettingRepository(
            "Server=localhost,1;Database=nope;User Id=sa;Password=x;Connection Timeout=1;TrustServerCertificate=True;"));

        Assert.Equal(15, svc.GetInt(SiteSettingKeys.AuthAccessTokenMinutes));
        Assert.False(svc.GetBool(SiteSettingKeys.FeatureGraphQl));   // off by default since #156
        Assert.Empty(svc.Snapshot);
        Assert.Null(svc.LoadedAtUtc);
        await Assert.ThrowsAnyAsync<Exception>(() => svc.RefreshAsync());   // explicit refresh surfaces the error
        Assert.Equal(15, svc.GetInt(SiteSettingKeys.AuthAccessTokenMinutes)); // still defaults afterwards
    }

    [Fact]
    public void Parser_Validate_RejectsWrongTypes()
    {
        var boolDef = SiteSettingDefinitions.Get(SiteSettingKeys.FeatureWebhooks);
        var intDef  = SiteSettingDefinitions.Get(SiteSettingKeys.ApiMaxPageSize);
        var jsonDef = SiteSettingDefinitions.Get(SiteSettingKeys.WebhooksRetryDelaysSeconds);

        Assert.Null(SiteSettingParser.Validate(boolDef, "true"));
        Assert.NotNull(SiteSettingParser.Validate(boolDef, "yes"));
        Assert.Null(SiteSettingParser.Validate(intDef, "42"));
        Assert.NotNull(SiteSettingParser.Validate(intDef, "-1"));
        Assert.NotNull(SiteSettingParser.Validate(intDef, "abc"));
        Assert.Null(SiteSettingParser.Validate(jsonDef, "[1,2]"));
        Assert.NotNull(SiteSettingParser.Validate(jsonDef, "[1,"));
    }

    // ── #147: workflow.requireReturnComment is honoured by usp_Workflow_Transition ──

    [Fact]
    public async Task Workflow_ReturnWithoutComment_IsRejectedByDefault_AndAllowedWhenSettingOff()
    {
        var key = SiteSettingKeys.WorkflowRequireReturnComment;
        await (await LoadedServiceAsync()).RefreshAsync();   // ensures the row exists
        try
        {
            var repo = new ContentEntryRepository(fixture.CreateDb());

            // Default (required): InReview → Draft without a comment fails.
            var (entryA, versionA, actor) = await SeedInReviewEntryAsync("settings/return-required");
            var (okA, errA) = await repo.TransitionAsync(entryA, versionA, "InReview", "Draft", actor, comment: null);
            Assert.False(okA);
            Assert.Contains("comment is required", errA, StringComparison.OrdinalIgnoreCase);

            // Setting off: the same transition succeeds.
            Assert.True(await Repo().SetValueAsync(key, "false", 0));
            var (entryB, versionB, _) = await SeedInReviewEntryAsync("settings/return-optional");
            var (okB, errB) = await repo.TransitionAsync(entryB, versionB, "InReview", "Draft", actor, comment: null);
            Assert.True(okB, errB);
        }
        finally { await ResetAsync(key); }
    }

    private async Task<(long EntryId, long VersionId, long ActorId)> SeedInReviewEntryAsync(string slugPrefix)
    {
        var actor  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var typeId = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "SettingsTestPage");
        var entry  = await TestSeeder.CreateEntryAsync(fixture.ConnectionString, typeId,
            $"{slugPrefix}-{Guid.NewGuid():N}", "en-US", actor);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentVersion_Create @ContentEntryId, @FieldsJson, NULL, @Status, @AuthorId, @ChangeNote, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@ContentEntryId", entry);
        cmd.Parameters.AddWithValue("@FieldsJson", "{\"title\":\"t\"}");
        cmd.Parameters.AddWithValue("@Status", "Draft");
        cmd.Parameters.AddWithValue("@AuthorId", actor);
        cmd.Parameters.AddWithValue("@ChangeNote", DBNull.Value);
        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var version = (long)outParam.Value;

        var repo = new ContentEntryRepository(fixture.CreateDb());
        var (ok, err) = await repo.TransitionAsync(entry, version, "Draft", "InReview", actor);
        Assert.True(ok, err);
        return (entry, version, actor);
    }

    // ── #145: media limits from settings ──────────────────────────────────────

    [Fact]
    public async Task Upload_LargerThanMaxUploadBytes_IsRejected()
    {
        var settings = StaticSiteSettings.Defaults.With(SiteSettingKeys.MediaMaxUploadBytes, 10);
        var service  = new MediaUploadService(new InMemoryStorageBackend(),
            new MediaAssetRepository(fixture.CreateDb()), new NoOpImageProcessingService(),
            new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()), settings);

        var (asset, error) = await service.UploadAsync(TestFile("big.txt", "text/plain", 11), uploadedById: 1);

        Assert.Null(asset);
        Assert.Contains("maximum upload size is 10", error);
    }

    [Fact]
    public async Task Upload_MimeNotInConfiguredAllowList_IsRejected_EvenIfInDefaultList()
    {
        var settings = StaticSiteSettings.Defaults.WithJson(SiteSettingKeys.MediaAllowedMimeTypes, new[] { "image/png" });
        var service  = new MediaUploadService(new InMemoryStorageBackend(),
            new MediaAssetRepository(fixture.CreateDb()), new NoOpImageProcessingService(),
            new NoOpVirusScanService(), new MediaExtendedRepository(fixture.CreateDb()), settings);

        var (asset, error) = await service.UploadAsync(TestFile("a.pdf", "application/pdf", 4), uploadedById: 1);

        Assert.Null(asset);
        Assert.Contains("not permitted", error);
        Assert.Contains("image/png", error);
        Assert.DoesNotContain("application/pdf.", error);
    }

    private static Microsoft.AspNetCore.Http.IFormFile TestFile(string name, string mime, int bytes)
    {
        var stream = new MemoryStream(new byte[bytes]);
        return new Microsoft.AspNetCore.Http.FormFile(stream, 0, bytes, "file", name) { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = mime };
    }

    // ── #146: token lifetimes ─────────────────────────────────────────────────

    [Fact]
    public void RefreshTokenLifetime_FollowsSetting()
    {
        var svc = new VA.CMS.API.Auth.InMemoryRefreshTokenService(
            StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthRefreshTokenHours, 2));
        Assert.Equal(TimeSpan.FromHours(2), svc.Lifetime);
        Assert.Equal(TimeSpan.FromHours(2),
            VA.CMS.API.Auth.AuthCookieHelper.BuildCookieOptions(secure: false, lifetime: svc.Lifetime).MaxAge);
    }

    [Fact]
    public void AccessTokenExpiry_FollowsSetting()
    {
        var opts = new VA.CMS.API.Auth.JwtOptions { SigningKey = "test-integration-signing-key-32b!" };
        var svc  = new VA.CMS.API.Auth.JwtService(opts, StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthAccessTokenMinutes, 90));
        var user = new VA.CMS.Infrastructure.Data.Pocos.User { Id = 1, ExternalId = "x", Email = "a@va.gov", DisplayName = "A" };

        var token = svc.IssueAccessToken(user, Array.Empty<VA.CMS.Infrastructure.Data.Pocos.UserRoleAssignment>());
        var jwt   = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.InRange(jwt.ValidTo, DateTime.UtcNow.AddMinutes(88), DateTime.UtcNow.AddMinutes(92));
    }

    // ── #147: pagination clamps ───────────────────────────────────────────────

    [Fact]
    public void PageSizeClamps_FollowSettings()
    {
        var s = StaticSiteSettings.Defaults
            .With(SiteSettingKeys.ApiMaxPageSize, 30)
            .With(SiteSettingKeys.SearchMaxPageSize, 12)
            .With(SiteSettingKeys.SearchDefaultPageSize, 7);

        Assert.Equal(30, s.ClampPageSize(500));
        Assert.Equal(1,  s.ClampPageSize(0));
        Assert.Equal(12, s.ClampSearchPageSize(99));
        Assert.Equal(7,  s.ClampSearchPageSize(null));
    }

    // ── #143 / #144: HTTP surface via the API host ────────────────────────────

    [Fact]
    public async Task AdminSettings_List_Set_Reset_RoundTrip_WithAudit()
    {
        using var factory = new SiteSettingsApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");
        var key = SiteSettingKeys.NotificationsPanelLimit;

        try
        {
            var list = await client.GetFromJsonAsync<SiteSettingsListResponse>("/api/v1/admin/settings");
            Assert.NotNull(list);
            Assert.Contains(list!.Items, i => i.Key == key && !i.IsOverridden);

            var put = await client.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new SetSiteSettingRequest("35"));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var dto = await put.Content.ReadFromJsonAsync<SiteSettingDto>();
            Assert.Equal("35", dto!.Value);
            Assert.True(dto.IsOverridden);

            // The running service sees it immediately (no restart, no rebuild).
            var live = factory.Services.GetRequiredService<ISiteSettingsService>();
            Assert.Equal(35, live.GetInt(key));

            // And the client endpoint (Admin scope) returns it.
            var clientMap = await client.GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/settings/client");
            Assert.Equal("35", clientMap![key]);

            var bad = await client.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new SetSiteSettingRequest("lots"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

            var unknown = await client.PutAsJsonAsync("/api/v1/admin/settings/no.such.key", new SetSiteSettingRequest("1"));
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

            var reset = await client.PostAsync($"/api/v1/admin/settings/{key}/reset", null);
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            Assert.Equal(20, live.GetInt(key));

            // Audit rows were written for both the update and the reset.
            await using var conn = new SqlConnection(fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM [AuditLog] WHERE [EntityType] = 'SiteSetting' AND [Action] IN ('SiteSettingUpdated','SiteSettingReset') AND [DiffJson] LIKE @Like";
            cmd.Parameters.AddWithValue("@Like", $"%{key}%");
            Assert.True((int)(await cmd.ExecuteScalarAsync())! >= 2);
        }
        finally { await ResetAsync(key); }
    }

    [Fact]
    public async Task PublicSettings_IsAnonymous_AndOnlyPublicScope()
    {
        using var factory = new SiteSettingsApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/v1/settings/public");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var map = await res.Content.ReadFromJsonAsync<Dictionary<string, string?>>();

        Assert.Equal("Department of Veterans Affairs", map![SiteSettingKeys.SiteTitle]);
        Assert.False(map.ContainsKey(SiteSettingKeys.AuthAccessTokenMinutes));   // Server scope
        Assert.False(map.ContainsKey(SiteSettingKeys.NotificationsPanelLimit));  // Admin scope

        var client403 = await client.GetAsync("/api/v1/settings/client");
        Assert.Equal(HttpStatusCode.Unauthorized, client403.StatusCode);

        var admin401 = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, admin401.StatusCode);
    }

    [Fact]
    public async Task FeatureFlags_GraphQlAndMediaUpload_AreEnforcedAtRequestTime()
    {
        using var factory = new SiteSettingsApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");
        var live = factory.Services.GetRequiredService<ISiteSettingsService>();

        try
        {
            // GraphQL is off by default (#156); switch it on to prove the gate opens, then off again.
            await Repo().SetValueAsync(SiteSettingKeys.FeatureGraphQl, "true", 0);
            await live.RefreshAsync();
            Assert.NotEqual(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync("/api/graphql", new { query = "{ __typename }" })).StatusCode);

            await Repo().SetValueAsync(SiteSettingKeys.FeatureGraphQl, "false", 0);
            await Repo().SetValueAsync(SiteSettingKeys.FeatureMediaUpload, "false", 0);
            await live.RefreshAsync();

            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync("/api/graphql", new { query = "{ __typename }" })).StatusCode);

            using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[3]), "file", "a.txt" } };
            var upload = await client.PostAsync("/api/v1/media/upload", form);
            Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        }
        finally
        {
            await ResetAsync(SiteSettingKeys.FeatureGraphQl);
            await ResetAsync(SiteSettingKeys.FeatureMediaUpload);
        }
    }
}

/// <summary>Runs Program.cs against the Testcontainers database in DevBypass mode.</summary>
public sealed class SiteSettingsApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("SKIP_MIGRATIONS", "true");   // DatabaseFixture already ran them
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Auth:Mode", "DevBypass");
        builder.UseSetting("Auth:DevBypassAllowedUsers:0", "alice@va.gov");
        builder.UseSetting("Jwt:SigningKey", "test-integration-signing-key-32b!");
        builder.UseSetting("Storage:Backend", "local");
        builder.UseSetting("Storage:LocalRootPath", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "va-cms-settings-tests"));
    }
}
