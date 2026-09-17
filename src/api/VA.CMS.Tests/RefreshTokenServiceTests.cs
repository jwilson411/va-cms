using VA.CMS.API.Auth;

namespace VA.CMS.Tests;

/// <summary>
/// Unit tests for InMemoryRefreshTokenService — issuance, validation, revocation.
/// No database required.
/// </summary>
public class RefreshTokenServiceTests
{
    private static InMemoryRefreshTokenService Build() => new();

    // ── Issue ────────────────────────────────────────────────────────────

    [Fact]
    public void Issue_Returns_Non_Empty_Token()
    {
        var svc   = Build();
        var token = svc.Issue(userId: 1);
        Assert.NotEmpty(token);
    }

    [Fact]
    public void Issue_Returns_Different_Token_Each_Call()
    {
        var svc = Build();
        var t1  = svc.Issue(1);
        var t2  = svc.Issue(1);
        Assert.NotEqual(t1, t2);
    }

    // ── Validate ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Returns_UserId_For_Valid_Token()
    {
        var svc   = Build();
        var token = svc.Issue(userId: 99);
        var result = svc.Validate(token);
        Assert.Equal(99L, result?.UserId);
    }

    [Fact]
    public void Validate_Returns_Groups_Captured_At_Issue()
    {
        var svc    = Build();
        var token  = svc.Issue(userId: 7, adGroups: new[] { "VA-CMS-Editors", " ", "va-cms-editors", "S-1-5-21-1-2-3-1001" });
        var result = svc.Validate(token);

        Assert.NotNull(result);
        Assert.Equal(new[] { "VA-CMS-Editors", "S-1-5-21-1-2-3-1001" }, result!.AdGroups);
    }

    [Fact]
    public void Validate_Returns_Empty_Groups_When_None_Issued()
    {
        var svc = Build();
        Assert.Empty(svc.Validate(svc.Issue(1))!.AdGroups);
    }

    [Fact]
    public void Validate_Returns_Null_For_Unknown_Token()
    {
        var svc = Build();
        Assert.Null(svc.Validate("not-a-real-token"));
    }

    // ── Revoke ───────────────────────────────────────────────────────────

    [Fact]
    public void Revoke_Makes_Token_Invalid()
    {
        var svc   = Build();
        var token = svc.Issue(userId: 5);

        svc.Revoke(token);

        Assert.Null(svc.Validate(token));
    }

    [Fact]
    public void Revoke_Unknown_Token_Does_Not_Throw()
    {
        var svc = Build();
        var ex  = Record.Exception(() => svc.Revoke("ghost-token"));
        Assert.Null(ex);
    }

    // ── Concurrent access ────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_Issue_And_Validate_Does_Not_Deadlock()
    {
        var svc    = Build();
        var tasks  = Enumerable.Range(1, 50).Select(async i =>
        {
            var token = svc.Issue(i);
            await Task.Yield();
            var uid = svc.Validate(token)?.UserId;
            Assert.Equal((long)i, uid);
        });
        await Task.WhenAll(tasks);
    }
}
