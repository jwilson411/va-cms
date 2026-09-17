using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #165 — audit log coverage (NIST AU-2/AU-3).
///
///   - V043: AuditLog carries IpAddress, UserAgent, CorrelationId and Outcome; usp_AuditLog_Write
///     takes them as parameters or reads them from SESSION_CONTEXT.
///   - CmsDatabase stamps the request's audit context onto every connection so a stored
///     procedure that audits itself records who/where.
///   - Every mutating stored procedure listed in docs/DATABASE_LAYER.md §4.9 writes its row
///     (table-driven below — add a case when adding an audited SP).
///   - The viewer filters by outcome and IP.
/// </summary>
[Collection("Database")]
public class Issue165AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<T> ScalarAsync<T>(string sql, params (string, object)[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Runs SQL through the EXECUTE-only login the way the API does: the connection goes
    /// through CmsDatabase.OnConnectionOpened so the audit context lands in SESSION_CONTEXT.
    /// Placeholders are @p0, @p1, … (raw SqlCommand — the scripts declare their own @variables).
    /// </summary>
    private async Task<long> ExecAsAppAsync(string sql, AuditContext? ctx = null, params object[] args)
    {
        var db = new CmsDatabase(fixture.AppConnectionString(), ctx);
        await using var conn = new SqlConnection(fixture.AppConnectionString());
        await conn.OpenAsync();
        db.OnConnectionOpened(conn);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static readonly AuditContext Ctx = new()
    {
        SourceIp = "192.0.2.44", UserAgent = "Issue165/1.0", CorrelationId = "corr-165",
    };

    // ── V043: row shape ────────────────────────────────────────────────────────

    [Fact]
    public async Task V043_Adds_Outcome_And_CorrelationId_To_AuditLog_And_Archive()
    {
        foreach (var table in new[] { "AuditLog", "AuditLogArchive" })
        foreach (var col in new[] { "IpAddress", "UserAgent", "CorrelationId", "Outcome" })
            Assert.Equal(1, await ScalarAsync<int>(
                $"SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.{table}') AND [name] = '{col}'"));
    }

    [Fact]
    public async Task Write_Takes_Explicit_Where_And_Outcome()
    {
        var actor = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var db    = new CmsDatabase(fixture.AppConnectionString(), Ctx);
        await new AuditLogRepository(db).WriteAsync(actor, "Session", actor, "LogonFailure", "{\"reason\":\"test\"}", AuditOutcome.Failure);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.AuditLog WHERE ActorId = @a AND [Action] = 'LogonFailure' AND Outcome = 'Failure' " +
            "AND IpAddress = '192.0.2.44' AND UserAgent = 'Issue165/1.0' AND CorrelationId = 'corr-165'", ("@a", actor)));
    }

    [Fact]
    public async Task Outcome_Is_Constrained_To_Success_Or_Failure()
    {
        var actor = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        // Anything other than 'Failure' is recorded as Success (the SP normalises), and the
        // CHECK constraint guards the column against direct writes.
        await ExecAsAppAsync("EXEC usp_AuditLog_Write @p0, 'Test', @p1, 'Ping', NULL, 'Bogus'; SELECT 1;", null, actor, actor);
        Assert.Equal("Success", await ScalarAsync<string>(
            "SELECT TOP 1 Outcome FROM dbo.AuditLog WHERE ActorId = @a AND [Action] = 'Ping'", ("@a", actor)));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(1) FROM sys.check_constraints WHERE [name] = 'CK_AuditLog_Outcome'"));
    }

    // ── SESSION_CONTEXT plumbing ───────────────────────────────────────────────

    [Fact]
    public async Task Stored_Procedure_Audit_Reads_Actor_And_Source_From_Session_Context()
    {
        var actor = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var owner = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var id = await ExecAsAppAsync(
            "DECLARE @id BIGINT; EXEC usp_Redirect_Create '/old-165', '/new-165', 301, @p0, @id OUTPUT; SELECT @id;", null, owner);

        // usp_Redirect_Deactivate has no explicit actor: it comes from the connection's context
        var ctx = new AuditContext { ActorId = actor, SourceIp = Ctx.SourceIp, UserAgent = Ctx.UserAgent, CorrelationId = Ctx.CorrelationId };
        await ExecAsAppAsync("EXEC usp_Redirect_Deactivate @p0; SELECT 1;", ctx, id);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.AuditLog WHERE EntityType = 'Redirect' AND [Action] = 'Deactivate' AND EntityId = CAST(@id AS NVARCHAR(100)) " +
            "AND ActorId = @a AND ActorEmail IS NOT NULL AND IpAddress = '192.0.2.44' AND CorrelationId = 'corr-165'",
            ("@id", id), ("@a", actor)));
    }

    [Fact]
    public async Task Anonymous_Scope_Sets_No_Session_Context()
    {
        // No actor → CmsDatabase skips sp_set_session_context; the SP audits with a NULL actor.
        var owner = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var id = await ExecAsAppAsync(
            "DECLARE @id BIGINT; EXEC usp_Redirect_Create '/old-165b', '/new-165b', 301, @p0, @id OUTPUT; SELECT @id;", null, owner);
        await ExecAsAppAsync("EXEC usp_Redirect_Deactivate @p0; SELECT 1;", new AuditContext { SourceIp = "1.2.3.4" }, id);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.AuditLog WHERE EntityType = 'Redirect' AND [Action] = 'Deactivate' AND EntityId = CAST(@id AS NVARCHAR(100)) AND ActorId IS NULL AND IpAddress IS NULL",
            ("@id", id)));
    }

    // ── Every mutating SP audits (docs/DATABASE_LAYER.md §4.9) ─────────────────

    /// <summary>The events DATABASE_LAYER.md §4.9 promises. Each case executes the SP and looks for its row.</summary>
    public static TheoryData<string> AuditedProcedures() => new()
    {
        "usp_User_Upsert", "usp_User_AssignRole", "usp_User_RevokeRole", "usp_User_Deactivate",
        "usp_ContentEntry_Create", "usp_ContentEntry_UpdateStatus", "usp_ContentVersion_Create",
        "usp_MediaAsset_Create", "usp_MediaAsset_UpdateMetadata", "usp_MediaAsset_SafeDelete",
        "usp_Navigation_CreateMenu", "usp_Navigation_UpdateMenu", "usp_Navigation_BulkReorder", "usp_Navigation_DeleteMenu",
        "usp_Navigation_UpsertItem(create)", "usp_Navigation_UpsertItem(update)", "usp_Navigation_DeleteItem",
        "usp_Redirect_Create", "usp_Redirect_Update", "usp_Redirect_Deactivate",
        "usp_Webhook_Create", "usp_Webhook_Delete",
        "usp_SearchPin_Create", "usp_SearchPin_Delete",
        "usp_AdGroupMapping_Upsert", "usp_AdGroupMapping_Delete",
        "usp_RefreshToken_RevokeAllForUser",
    };

    [Theory]
    [MemberData(nameof(AuditedProcedures))]
    public async Task Mutating_Procedure_Writes_Its_Audit_Row(string sp)
    {
        var actor  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctx    = new AuditContext { ActorId = actor, SourceIp = Ctx.SourceIp, UserAgent = Ctx.UserAgent, CorrelationId = Ctx.CorrelationId };
        var typeId = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "Issue165Type");
        var roleId = await ScalarAsync<long>("SELECT TOP 1 Id FROM dbo.[Role] ORDER BY Id");
        var tag    = Guid.NewGuid().ToString("N")[..8];

        // Each case: (entityType, action, entityId, explicit actor expected?)
        (string entityType, string action, long entityId, bool actorIsExplicit) row = sp switch
        {
            "usp_User_Upsert" => ("User", "Provision",
                await ExecAsAppAsync("DECLARE @u BIGINT; EXEC usp_User_Upsert @p0, @p1, 'New', @u OUTPUT; SELECT @u;", ctx, $"ext-{tag}", $"{tag}@va.gov"), true),

            "usp_User_AssignRole" => ("User", "AssignRole", await Run(
                "EXEC usp_User_AssignRole @p0, @p1, NULL, @p2; SELECT @p0;", await NewUser(), roleId, actor), true),

            "usp_User_RevokeRole" => await RevokeRoleCase(roleId, ctx),

            "usp_User_Deactivate" => ("User", "Deactivate", await Run("EXEC usp_User_Deactivate @p0, @p1; SELECT @p0;", await NewUser(), actor), true),

            "usp_ContentEntry_Create" => ("ContentEntry", "Create", await Run(
                "DECLARE @id BIGINT; EXEC usp_ContentEntry_Create @p0, @p1, 'en-US', @p2, @id OUTPUT; SELECT @id;", typeId, $"slug-{tag}", actor), true),

            "usp_ContentEntry_UpdateStatus" => ("ContentEntry", "UpdateStatus", await Run(
                "EXEC usp_ContentEntry_UpdateStatus @p0, 'InReview'; SELECT @p0;", await NewEntry(typeId, tag, actor)), false),

            "usp_ContentVersion_Create" => ("ContentVersion", "Create", await Run(
                "DECLARE @id BIGINT; EXEC usp_ContentVersion_Create @p0, '{}', NULL, 'Draft', @p1, 'note', @id OUTPUT; SELECT @id;",
                await NewEntry(typeId, tag, actor), actor), true),

            "usp_MediaAsset_Create" => ("MediaAsset", "Create", await NewAsset(tag, actor), true),
            "usp_MediaAsset_UpdateMetadata" => ("MediaAsset", "UpdateMetadata", await Run(
                "EXEC usp_MediaAsset_UpdateMetadata @p0, 'alt'; SELECT @p0;", await NewAsset(tag, actor)), false),
            "usp_MediaAsset_SafeDelete" => ("MediaAsset", "Delete", await Run(
                "DECLARE @r INT; EXEC usp_MediaAsset_SafeDelete @p0, @r OUTPUT; SELECT @p0;", await NewAsset(tag, actor)), false),

            "usp_Navigation_CreateMenu" => ("NavigationMenu", "Create", await NewMenu(tag), false),
            "usp_Navigation_UpdateMenu" => ("NavigationMenu", "Update", await Run("EXEC usp_Navigation_UpdateMenu @p0, 'Renamed'; SELECT @p0;", await NewMenu(tag)), false),
            "usp_Navigation_BulkReorder" => ("NavigationMenu", "Reorder", await Run("EXEC usp_Navigation_BulkReorder @p0, '[]'; SELECT @p0;", await NewMenu(tag)), false),
            "usp_Navigation_DeleteMenu" => ("NavigationMenu", "Delete", await Run("EXEC usp_Navigation_DeleteMenu @p0; SELECT @p0;", await NewMenu(tag)), false),
            "usp_Navigation_UpsertItem(create)" => ("NavigationItem", "Create", await NewItem(await NewMenu(tag)), false),
            "usp_Navigation_UpsertItem(update)" => await UpdateItemCase(tag),
            "usp_Navigation_DeleteItem" => ("NavigationItem", "Delete", await Run("EXEC usp_Navigation_DeleteItem @p0; SELECT @p0;", await NewItem(await NewMenu(tag))), false),

            "usp_Redirect_Create" => ("Redirect", "Create", await NewRedirect(tag, actor), true),
            "usp_Redirect_Update" => ("Redirect", "Update", await Run("EXEC usp_Redirect_Update @p0, @p1, '/to2'; SELECT @p0;", await NewRedirect(tag, actor), $"/from-{tag}-b"), false),
            "usp_Redirect_Deactivate" => ("Redirect", "Deactivate", await Run("EXEC usp_Redirect_Deactivate @p0; SELECT @p0;", await NewRedirect(tag, actor)), false),

            "usp_Webhook_Create" => ("Webhook", "Create", await NewWebhook(tag, actor), true),
            "usp_Webhook_Delete" => ("Webhook", "Delete", await Run("EXEC usp_Webhook_Delete @p0; SELECT @p0;", await NewWebhook(tag, actor)), false),

            "usp_SearchPin_Create" => ("SearchPin", "Create", await NewPin(tag, await NewEntry(typeId, tag, actor), actor), true),
            "usp_SearchPin_Delete" => ("SearchPin", "Delete", await Run("EXEC usp_SearchPin_Delete @p0; SELECT @p0;", await NewPin(tag, await NewEntry(typeId, tag, actor), actor)), false),

            "usp_AdGroupMapping_Upsert" => ("AdGroupRoleMapping", "AdGroupMappingUpserted", await NewMapping(tag, roleId, actor), true),
            "usp_AdGroupMapping_Delete" => ("AdGroupRoleMapping", "AdGroupMappingDeleted", await Run("EXEC usp_AdGroupMapping_Delete @p0; SELECT @p0;", await NewMapping(tag, roleId, actor)), false),

            "usp_RefreshToken_RevokeAllForUser" => ("Session", "SessionsRevoked", await Run("EXEC usp_RefreshToken_RevokeAllForUser @p0, @p1, 'Admin'; SELECT @p0;", await NewUser(), actor), true),

            _ => throw new ArgumentOutOfRangeException(nameof(sp), sp, "add a case for the new audited procedure"),
        };

        // Row exists, with the acting user (explicit parameter or SESSION_CONTEXT — both
        // resolve to the same actor here) and the request's source details.
        var expectedActor = sp == "usp_User_Upsert" ? row.entityId : actor;
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.AuditLog WHERE EntityType = @t AND [Action] = @a AND EntityId = CAST(@e AS NVARCHAR(100)) " +
            "AND ActorId = @actor AND IpAddress = '192.0.2.44' AND CorrelationId = 'corr-165' AND Outcome = 'Success'",
            ("@t", row.entityType), ("@a", row.action), ("@e", row.entityId), ("@actor", expectedActor)));

        // — local helpers, all through the app login with the audit context —
        Task<long> Run(string sql, params object[] args) => ExecAsAppAsync(sql, ctx, args);
        Task<long> NewUser() => TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        Task<long> NewEntry(long t, string s, long owner) => Run("DECLARE @id BIGINT; EXEC usp_ContentEntry_Create @p0, @p1, 'en-US', @p2, @id OUTPUT; SELECT @id;", t, $"e-{s}-{Guid.NewGuid():N}", owner);
        Task<long> NewAsset(string s, long by) => Run("DECLARE @id BIGINT; EXEC usp_MediaAsset_Create @p0, '/p', 'local', 'image/png', 10, NULL, NULL, @p1, @id OUTPUT; SELECT @id;", $"f-{s}.png", by);
        Task<long> NewMenu(string s) => Run("DECLARE @id BIGINT; EXEC usp_Navigation_CreateMenu @p0, @p1, @id OUTPUT; SELECT @id;", $"Menu {s}", $"menu-{s}-{Guid.NewGuid():N}");
        Task<long> NewItem(long menu) => Run("DECLARE @id BIGINT; EXEC usp_Navigation_UpsertItem NULL, @p0, NULL, 'Item', '/x', NULL, '_self', 0, 1, @id OUTPUT; SELECT @id;", menu);
        Task<long> NewRedirect(string s, long by) => Run("DECLARE @id BIGINT; EXEC usp_Redirect_Create @p0, '/to', 301, @p1, @id OUTPUT; SELECT @id;", $"/from-{s}-{Guid.NewGuid():N}", by);
        Task<long> NewWebhook(string s, long by) => Run("DECLARE @id BIGINT; EXEC usp_Webhook_Create @p0, 'https://example.test/h', 'secret', '[]', @p1, @id OUTPUT; SELECT @id;", $"hook-{s}", by);
        Task<long> NewPin(string s, long entry, long by) => Run("DECLARE @id BIGINT; EXEC usp_SearchPin_Create @p0, @p1, @p2, @id OUTPUT; SELECT @id;", $"q-{s}-{Guid.NewGuid():N}", entry, by);
        Task<long> NewMapping(string s, long role, long by) => Run("DECLARE @id BIGINT; EXEC usp_AdGroupMapping_Upsert @p0, @p1, @p2, @id OUTPUT; SELECT @id;", $"VA-{s}-{Guid.NewGuid():N}", role, by);

        async Task<(string, string, long, bool)> RevokeRoleCase(long role, AuditContext c)
        {
            var u = await NewUser();
            await Run("EXEC usp_User_AssignRole @p0, @p1, NULL, @p2; SELECT 1;", u, role, actor);
            await Run("EXEC usp_User_RevokeRole @p0, @p1, NULL; SELECT 1;", u, role);   // actor from context
            return ("User", "RevokeRole", u, false);
        }

        async Task<(string, string, long, bool)> UpdateItemCase(string s)
        {
            var item = await NewItem(await NewMenu(s));
            await Run("DECLARE @id BIGINT; EXEC usp_Navigation_UpsertItem @p0, 0, NULL, 'Item2', '/y', NULL, '_self', 1, 1, @id OUTPUT; SELECT @id;", item);
            return ("NavigationItem", "Update", item, false);
        }
    }

    // ── Viewer filters ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_Filters_By_Outcome_And_Ip_And_Exports_The_Columns()
    {
        var actor = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ip    = $"198.51.100.{Random.Shared.Next(1, 254)}";
        var repo  = new AuditLogRepository(new CmsDatabase(fixture.AppConnectionString(), new AuditContext { SourceIp = ip, UserAgent = "ua", CorrelationId = "c1" }));
        await repo.WriteAsync(actor, "Session", actor, "Logon");
        await repo.WriteAsync(actor, "Session", actor, "LogonFailure", null, AuditOutcome.Failure);

        var failures = await repo.ListPagedAsync(actorId: actor, outcome: AuditOutcome.Failure);
        Assert.Single(failures.Items);
        Assert.Equal("LogonFailure", failures.Items[0].Action);
        Assert.Equal(ip, failures.Items[0].IpAddress);
        Assert.Equal("c1", failures.Items[0].CorrelationId);

        var byIp = await repo.ListPagedAsync(ipAddress: ip);
        Assert.Equal(2, byIp.TotalItems);

        var export = await repo.ExportAsync(actorId: actor, outcome: AuditOutcome.Success, ipAddress: ip);
        Assert.Single(export);
        Assert.Equal("Logon", export[0].Action);
        Assert.Equal("ua", export[0].UserAgent);
    }
}
