using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Unit tests for InMemoryRefreshTokenService — issuance, validation, rotation,
/// replay detection and revocation (#163 semantics, no database required).
/// The DB-backed service is covered by Issue163AcceptanceTests.
/// </summary>
public class RefreshTokenServiceTests
{
    private static InMemoryRefreshTokenService Build(ISiteSettingsService? settings = null) => new(settings);

    // ── Issue ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Issue_Returns_Non_Empty_Token()
    {
        var svc   = Build();
        var token = await svc.IssueAsync(userId: 1);
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task Issue_Returns_Different_Token_Each_Call()
    {
        var svc = Build();
        var t1  = await svc.IssueAsync(1);
        var t2  = await svc.IssueAsync(1);
        Assert.NotEqual(t1, t2);
    }

    // ── Validate ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_Returns_UserId_For_Valid_Token()
    {
        var svc    = Build();
        var token  = await svc.IssueAsync(userId: 99);
        var result = await svc.ValidateAsync(token);
        Assert.True(result.Ok);
        Assert.Equal(99L, result.Session!.UserId);
    }

    [Fact]
    public async Task Validate_Returns_Groups_Captured_At_Issue()
    {
        var svc    = Build();
        var token  = await svc.IssueAsync(userId: 7, adGroups: new[] { "VA-CMS-Editors", " ", "va-cms-editors", "S-1-5-21-1-2-3-1001" });
        var result = await svc.ValidateAsync(token);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "VA-CMS-Editors", "S-1-5-21-1-2-3-1001" }, result.Session!.AdGroups);
    }

    [Fact]
    public async Task Validate_Returns_Empty_Groups_When_None_Issued()
    {
        var svc = Build();
        Assert.Empty((await svc.ValidateAsync(await svc.IssueAsync(1))).Session!.AdGroups);
    }

    [Fact]
    public async Task Validate_Fails_Unknown_For_Unknown_Token()
    {
        var svc = Build();
        var result = await svc.ValidateAsync("not-a-real-token");
        Assert.False(result.Ok);
        Assert.Equal(RefreshFailure.Unknown, result.Failure);
    }

    // ── Rotation and replay ──────────────────────────────────────────────

    [Fact]
    public async Task Rotate_Issues_New_Token_And_Old_Is_Replay_After_Grace()
    {
        var svc   = Build(StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthRefreshRotationGraceSeconds, 0));
        var first = await svc.IssueAsync(userId: 3, adGroups: new[] { "G1" });
        var s1    = (await svc.ValidateAsync(first)).Session!;

        var second = await svc.RotateAsync(first, s1);
        Assert.NotEqual(first, second);

        // The replacement carries the session on
        var s2 = await svc.ValidateAsync(second);
        Assert.True(s2.Ok);
        Assert.Equal(3L, s2.Session!.UserId);
        Assert.Equal(new[] { "G1" }, s2.Session.AdGroups);

        // Presenting the spent token again (grace = 0) is replay: the whole chain dies.
        svc.SetRevokedAt(first, DateTime.UtcNow.AddSeconds(-5));
        var replay = await svc.ValidateAsync(first);
        Assert.Equal(RefreshFailure.Replay, replay.Failure);
        Assert.Equal(RefreshFailure.Replay, (await svc.ValidateAsync(second)).Failure);
    }

    [Fact]
    public async Task Rotated_Token_Is_Still_Accepted_Within_Grace_Window()
    {
        var svc   = Build();   // default grace 30 s: two tabs refreshing at once must not lock each other out
        var first = await svc.IssueAsync(userId: 3);
        var s1    = (await svc.ValidateAsync(first)).Session!;
        var second = await svc.RotateAsync(first, s1);

        Assert.True((await svc.ValidateAsync(first)).Ok);
        Assert.True((await svc.ValidateAsync(second)).Ok);
    }

    [Fact]
    public async Task Validate_Fails_Idle_When_Unused_Past_Idle_Window()
    {
        var svc   = Build(StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthIdleTimeoutMinutes, 15));
        var token = await svc.IssueAsync(userId: 4);

        svc.SetLastUsed(token, DateTime.UtcNow.AddMinutes(-17));   // window is max(15, access 15 + 1) = 16

        Assert.Equal(RefreshFailure.Idle, (await svc.ValidateAsync(token)).Failure);
    }

    [Fact]
    public async Task Idle_Window_Never_Undercuts_The_Access_Token_Lifetime()
    {
        // idle 5 min with a 15-minute access token would reject every silent refresh
        var settings = StaticSiteSettings.Defaults
            .With(SiteSettingKeys.AuthIdleTimeoutMinutes, 5)
            .With(SiteSettingKeys.AuthAccessTokenMinutes, 15);
        Assert.Equal(16, settings.IdleTimeoutMinutes());

        var svc   = Build(settings);
        var token = await svc.IssueAsync(userId: 4);
        svc.SetLastUsed(token, DateTime.UtcNow.AddMinutes(-14));
        Assert.True((await svc.ValidateAsync(token)).Ok);
    }

    [Fact]
    public void Absolute_Session_Hours_Are_Clamped_To_Twelve()
    {
        Assert.Equal(12, StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthAbsoluteSessionHours, 48).AbsoluteSessionHours());
        Assert.Equal(1,  StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthAbsoluteSessionHours, 0).AbsoluteSessionHours());
        // and the per-token lifetime can never exceed the cap
        Assert.Equal(8,  StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthRefreshTokenHours, 24).RefreshTokenHours());
    }

    // ── Revoke ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_Makes_Token_Invalid()
    {
        var svc   = Build(StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthRefreshRotationGraceSeconds, 0));
        var token = await svc.IssueAsync(userId: 5);

        await svc.RevokeAsync(token);

        Assert.False((await svc.ValidateAsync(token)).Ok);
    }

    [Fact]
    public async Task Revoke_Unknown_Token_Does_Not_Throw()
    {
        var svc = Build();
        var ex  = await Record.ExceptionAsync(() => svc.RevokeAsync("ghost-token"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task RevokeAllForUser_Ends_Every_Session_Of_That_User_Only()
    {
        var svc = Build(StaticSiteSettings.Defaults.With(SiteSettingKeys.AuthRefreshRotationGraceSeconds, 0));
        var a1  = await svc.IssueAsync(userId: 8);
        var a2  = await svc.IssueAsync(userId: 8);
        var b   = await svc.IssueAsync(userId: 9);

        await svc.RevokeAllForUserAsync(8, actorId: 1, RefreshRevokeReason.Deactivated);

        Assert.False((await svc.ValidateAsync(a1)).Ok);
        Assert.False((await svc.ValidateAsync(a2)).Ok);
        Assert.True((await svc.ValidateAsync(b)).Ok);
        Assert.Contains((8L, RefreshRevokeReason.Deactivated), svc.RevokedUsers);
    }

    // ── Concurrent access ────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_Issue_And_Validate_Does_Not_Deadlock()
    {
        var svc    = Build();
        var tasks  = Enumerable.Range(1, 50).Select(async i =>
        {
            var token = await svc.IssueAsync(i);
            await Task.Yield();
            var uid = (await svc.ValidateAsync(token)).Session?.UserId;
            Assert.Equal((long)i, uid);
        });
        await Task.WhenAll(tasks);
    }

    // ── Codec ────────────────────────────────────────────────────────────

    [Fact]
    public void Codec_Hashes_Are_Stable_And_Url_Safe()
    {
        var token = RefreshTokenCodec.NewToken();
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        Assert.Equal(RefreshTokenCodec.Hash(token), RefreshTokenCodec.Hash(token));
        Assert.Equal(32, RefreshTokenCodec.Hash(token).Length);
        Assert.Equal(new[] { "A", "B" }, RefreshTokenCodec.GroupsFromJson(RefreshTokenCodec.GroupsToJson(new[] { "A", "a", "B" })));
    }
}
