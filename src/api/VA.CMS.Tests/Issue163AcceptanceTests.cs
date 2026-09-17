using Microsoft.Data.SqlClient;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #163 — DB-backed refresh tokens with rotation, replay
/// detection and per-user revocation (BRD FR-SECURITY-02), against the real
/// usp_RefreshToken_* procedures from V044.
/// </summary>
[Collection("Database")]
public class Issue163AcceptanceTests(DatabaseFixture fixture)
{
    private static ISiteSettingsService Settings(int grace = 0, int idle = 15) => StaticSiteSettings.Defaults
        .With(SiteSettingKeys.AuthRefreshRotationGraceSeconds, grace)
        .With(SiteSettingKeys.AuthIdleTimeoutMinutes, idle);

    private DbRefreshTokenService Service(ISiteSettingsService? settings = null)
        => new(new RefreshTokenRepository(new CmsDatabase(fixture.AppConnectionString())), settings ?? Settings());

    private static readonly RefreshClient Client = new("10.9.8.7", "xunit/1.0");

    private async Task<T> ScalarAsync<T>(string sql, params (string, object)[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private Task ExecAsync(string sql, params (string, object)[] args) => ScalarAsync<object>("SET NOCOUNT ON; " + sql + "; SELECT 1;", args);

    // ── Schema ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task V044_Creates_RefreshToken_Table_And_SessionVersion_Column()
    {
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(1) FROM sys.tables WHERE [name] = 'RefreshToken'"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.User') AND [name] = 'SessionVersion'"));
        foreach (var sp in new[] { "Issue", "Validate", "Rotate", "Revoke", "RevokeAllForUser" })
            Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_RefreshToken_{sp}'"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_Maint_PurgeRefreshTokens'"));
    }

    [Fact]
    public async Task App_Login_Reaches_Tokens_Through_Procedures_Only()
    {
        // The EXECUTE-only login can issue (through the SP) …
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var token  = await Service().IssueAsync(userId, client: Client);
        Assert.NotEmpty(token);

        // … but cannot read the table itself.
        await using var conn = new SqlConnection(fixture.AppConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 TokenHash FROM dbo.RefreshToken";
        var ex = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteScalarAsync());
        Assert.Contains("SELECT permission was denied", ex.Message);
    }

    // ── Issue / validate / rotate ──────────────────────────────────────────────

    [Fact]
    public async Task Only_A_Hash_Is_Stored_And_Login_Groups_Survive_Rotation()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service();
        var token  = await svc.IssueAsync(userId, new[] { "VA-CMS-Editors", "S-1-5-21-1" }, Client);

        var stored = await ScalarAsync<int>("SELECT COUNT(1) FROM dbo.RefreshToken WHERE UserId = @u AND CreatedByIp = '10.9.8.7' AND UserAgent = 'xunit/1.0'", ("@u", userId));
        Assert.Equal(1, stored);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(1) FROM dbo.RefreshToken WHERE CAST(GroupsJson AS NVARCHAR(MAX)) LIKE @t", ("@t", $"%{token}%")));

        var v1 = await svc.ValidateAsync(token, Client);
        Assert.True(v1.Ok);
        Assert.Equal(userId, v1.Session!.UserId);
        Assert.Equal(new[] { "VA-CMS-Editors", "S-1-5-21-1" }, v1.Session.AdGroups);

        var next = await svc.RotateAsync(token, v1.Session, Client);
        Assert.NotEqual(token, next);
        var v2 = await svc.ValidateAsync(next, Client);
        Assert.True(v2.Ok);
        Assert.Equal(new[] { "VA-CMS-Editors", "S-1-5-21-1" }, v2.Session!.AdGroups);

        // Old row is linked to its replacement and marked Rotated
        var reason = await ScalarAsync<string>("SELECT RevokedReason FROM dbo.RefreshToken WHERE Id = @id", ("@id", v1.Session.TokenId));
        Assert.Equal("Rotated", reason);
        Assert.Equal(v2.Session.TokenId, await ScalarAsync<long>("SELECT ReplacedById FROM dbo.RefreshToken WHERE Id = @id", ("@id", v1.Session.TokenId)));
    }

    [Fact]
    public async Task Replay_Of_A_Rotated_Token_Revokes_The_Whole_Chain_And_Is_Audited()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service(Settings(grace: 0));
        var t1     = await svc.IssueAsync(userId, client: Client);
        var s1     = (await svc.ValidateAsync(t1, Client)).Session!;
        var t2     = await svc.RotateAsync(t1, s1, Client);
        var s2     = (await svc.ValidateAsync(t2, Client)).Session!;
        var t3     = await svc.RotateAsync(t2, s2, Client);

        // Grace is 0 but the same second still counts as "just rotated": push the rotation back.
        await ExecAsync("UPDATE dbo.RefreshToken SET RevokedAt = DATEADD(SECOND, -60, SYSUTCDATETIME()) WHERE Id = @id", ("@id", s1.TokenId));

        var replay = await svc.ValidateAsync(t1, Client);
        Assert.Equal(RefreshFailure.Replay, replay.Failure);

        // The live tail of the chain is dead too
        Assert.Equal(RefreshFailure.Replay, (await svc.ValidateAsync(t3, Client)).Failure);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(1) FROM dbo.RefreshToken WHERE UserId = @u AND RevokedAt IS NULL", ("@u", userId)));

        // …and an AuditLog row per presented dead token says so, with the caller's address
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.AuditLog WHERE EntityType = 'Session' AND [Action] = 'RefreshReplay' AND EntityId = CAST(@id AS NVARCHAR(100)) " +
            "AND ActorId = @u AND Outcome = 'Failure' AND IpAddress = '10.9.8.7'",
            ("@id", s1.TokenId), ("@u", userId)));
    }

    [Fact]
    public async Task Rotated_Token_Stays_Valid_Inside_The_Grace_Window()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service(Settings(grace: 30));
        var t1     = await svc.IssueAsync(userId, client: Client);
        var s1     = (await svc.ValidateAsync(t1, Client)).Session!;
        var t2     = await svc.RotateAsync(t1, s1, Client);

        Assert.True((await svc.ValidateAsync(t1, Client)).Ok);   // second tab, same instant
        Assert.True((await svc.ValidateAsync(t2, Client)).Ok);
    }

    // ── Session limits (#164) ──────────────────────────────────────────────────

    [Fact]
    public async Task Idle_And_Absolute_Limits_Are_Enforced_Server_Side()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        // idle 15 is raised to accessTokenMinutes + 1 = 16 so silent refresh always fits
        var svc    = Service(Settings(idle: 15));

        var idle = await svc.IssueAsync(userId, client: Client);
        var idleId = (await svc.ValidateAsync(idle, Client)).Session!.TokenId;
        await ExecAsync("UPDATE dbo.RefreshToken SET LastUsedAt = DATEADD(MINUTE, -16, SYSUTCDATETIME()) WHERE Id = @id", ("@id", idleId));
        Assert.True((await svc.ValidateAsync(idle, Client)).Ok);
        await ExecAsync("UPDATE dbo.RefreshToken SET LastUsedAt = DATEADD(MINUTE, -17, SYSUTCDATETIME()) WHERE Id = @id", ("@id", idleId));
        Assert.Equal(RefreshFailure.Idle, (await svc.ValidateAsync(idle, Client)).Failure);

        var capped = await svc.IssueAsync(userId, client: Client);
        var cappedId = (await svc.ValidateAsync(capped, Client)).Session!.TokenId;
        await ExecAsync("UPDATE dbo.RefreshToken SET AbsoluteExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE Id = @id", ("@id", cappedId));
        Assert.Equal(RefreshFailure.Expired, (await svc.ValidateAsync(capped, Client)).Failure);
    }

    [Fact]
    public async Task Rotation_Never_Extends_Past_The_Absolute_Cap()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service();
        var t1     = await svc.IssueAsync(userId, client: Client);
        var s1     = (await svc.ValidateAsync(t1, Client)).Session!;
        // Pretend the session started almost 8 h ago: the cap is 5 minutes away
        await ExecAsync("UPDATE dbo.RefreshToken SET AbsoluteExpiresAt = DATEADD(MINUTE, 5, SYSUTCDATETIME()) WHERE Id = @id", ("@id", s1.TokenId));

        var t2 = await svc.RotateAsync(t1, s1, Client);
        var s2 = (await svc.ValidateAsync(t2, Client)).Session!;

        var minutesLeft = await ScalarAsync<int>("SELECT DATEDIFF(MINUTE, SYSUTCDATETIME(), ExpiresAt) FROM dbo.RefreshToken WHERE Id = @id", ("@id", s2.TokenId));
        Assert.InRange(minutesLeft, 0, 5);
    }

    // ── Revocation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAllForUser_Ends_Sessions_Bumps_SessionVersion_And_Audits()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var actor  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service();
        var a      = await svc.IssueAsync(userId, client: Client);
        var b      = await svc.IssueAsync(userId, client: Client);
        var before = await ScalarAsync<int>("SELECT SessionVersion FROM dbo.[User] WHERE Id = @u", ("@u", userId));

        await svc.RevokeAllForUserAsync(userId, actor, RefreshRevokeReason.Admin);

        Assert.False((await svc.ValidateAsync(a, Client)).Ok);
        Assert.False((await svc.ValidateAsync(b, Client)).Ok);
        Assert.Equal(before + 1, await ScalarAsync<int>("SELECT SessionVersion FROM dbo.[User] WHERE Id = @u", ("@u", userId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.AuditLog WHERE EntityType = 'Session' AND [Action] = 'SessionsRevoked' AND EntityId = CAST(@u AS NVARCHAR(100)) AND ActorId = @a",
            ("@u", userId), ("@a", actor)));
    }

    [Fact]
    public async Task Deactivate_And_Role_Changes_Revoke_Sessions()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var admin  = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service();
        var roleId = await ScalarAsync<long>("SELECT TOP 1 Id FROM dbo.[Role] ORDER BY Id");

        var t = await svc.IssueAsync(userId, client: Client);
        await ExecAsync("EXEC usp_User_AssignRole @u, @r, NULL, @a", ("@u", userId), ("@r", roleId), ("@a", admin));
        Assert.False((await svc.ValidateAsync(t, Client)).Ok);
        Assert.Equal(1, await ScalarAsync<int>("SELECT SessionVersion FROM dbo.[User] WHERE Id = @u", ("@u", userId)));

        t = await svc.IssueAsync(userId, client: Client);
        await ExecAsync("EXEC usp_User_RevokeRole @u, @r, NULL, @a", ("@u", userId), ("@r", roleId), ("@a", admin));
        Assert.False((await svc.ValidateAsync(t, Client)).Ok);
        Assert.Equal(2, await ScalarAsync<int>("SELECT SessionVersion FROM dbo.[User] WHERE Id = @u", ("@u", userId)));

        t = await svc.IssueAsync(userId, client: Client);
        await ExecAsync("EXEC usp_User_Deactivate @u, @a", ("@u", userId), ("@a", admin));
        Assert.False((await svc.ValidateAsync(t, Client)).Ok);
        Assert.Equal(3, await ScalarAsync<int>("SELECT SessionVersion FROM dbo.[User] WHERE Id = @u", ("@u", userId)));
        Assert.Equal("Deactivated", await ScalarAsync<string>("SELECT TOP 1 RevokedReason FROM dbo.RefreshToken WHERE UserId = @u ORDER BY Id DESC", ("@u", userId)));
    }

    // ── Purge ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Purge_Removes_Old_Dead_Rows_And_Keeps_Live_Ones()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var svc    = Service();
        var live   = await svc.IssueAsync(userId, client: Client);
        var t1     = await svc.IssueAsync(userId, client: Client);
        var s1     = (await svc.ValidateAsync(t1, Client)).Session!;
        var t2     = await svc.RotateAsync(t1, s1, Client);   // t1 → Rotated, ReplacedById = t2's row
        var s2     = (await svc.ValidateAsync(t2, Client)).Session!;
        await svc.RevokeAsync(t2, RefreshRevokeReason.Logout);

        await ExecAsync("UPDATE dbo.RefreshToken SET RevokedAt = DATEADD(DAY, -40, SYSUTCDATETIME()) WHERE Id IN (@a, @b)", ("@a", s1.TokenId), ("@b", s2.TokenId));
        await ExecAsync("EXEC usp_Maint_PurgeRefreshTokens @RetentionDays = 30");

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(1) FROM dbo.RefreshToken WHERE Id IN (@a, @b)", ("@a", s1.TokenId), ("@b", s2.TokenId)));
        Assert.True((await svc.ValidateAsync(live, Client)).Ok);
    }
}
