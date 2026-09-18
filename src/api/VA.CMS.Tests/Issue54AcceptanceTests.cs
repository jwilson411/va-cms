using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Controllers;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #54 — Implement webhook registration, delivery, and HMAC signing.
///
/// BRD FR-DEV-07.
///
/// Acceptance criteria:
///   AC1: POST /api/v1/webhooks registers endpoint — controller returns 201 + { id, secret }.
///   AC2: Events: content.published, content.unpublished, content.archived, media.uploaded supported.
///   AC3: Delivery signed with HMAC-SHA256 in X-CMS-Signature header.
///   AC4: WebhookDelivery log records attempt, status code, and error.
///   AC5: Retry up to 3 times with exponential backoff on non-2xx.
///
/// Migration: V028 adds usp_Webhook_Create, usp_Webhook_List, usp_Webhook_GetById,
///            usp_Webhook_Delete (and refreshes usp_Webhook_GetActiveForEvent,
///            usp_WebhookDelivery_Create which were seeded in V008).
/// </summary>
[Collection("Database")]
public class Issue54AcceptanceTests(DatabaseFixture fixture)
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private IWebhookRepository Repo() => new WebhookRepository(fixture.CreateDb());

    /// <summary>Seed a test user and return its ID. Used to satisfy FK_Webhook_CreatedBy.</summary>
    private Task<long> SeedUserAsync() =>
        TestSeeder.UpsertUserAsync(fixture.ConnectionString);

    private WebhooksController Controller(long userId)
    {
        var repo = Repo();
        // Development-mode policy (any host), pass-through protector and a dispatcher that never
        // sends: these tests cover #54 registration/listing, not the #168 egress controls.
        var policy = new WebhookDestinationPolicy(StaticSiteSettings.Defaults, isDevelopment: true);
        var ctrl = new WebhooksController(repo, policy, new PassthroughProtector(),
            new WebhookDispatcher(repo, new FakeHttpClientFactory(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookDispatcher>.Instance));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[]
                        {
                            new System.Security.Claims.Claim("cms_user_id", userId.ToString()),
                        },
                        authenticationType: "test")),
            },
        };
        return ctrl;
    }

    // ── AC1: V028 migration applied — SPs exist ────────────────────────────────

    [Fact]
    public async Task V028_usp_Webhook_Create_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_Webhook_Create';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V028_usp_Webhook_List_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_Webhook_List';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V028_usp_WebhookDelivery_Create_Exists()
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sys.procedures WHERE [name] = 'usp_WebhookDelivery_Create';";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    // ── AC1: POST /api/v1/webhooks → 201 + id + secret ───────────────────────

    [Fact]
    public async Task RegisterWebhook_Returns201WithIdAndSecret()
    {
        var userId = await SeedUserAsync();
        var ctrl = Controller(userId);
        var request = new WebhookRegistrationRequest
        {
            Name   = "Test Webhook",
            Url    = "https://example.com/hooks/cms",
            Events = new[] { "content.published" },
        };

        var result = await ctrl.Register(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<WebhookRegistrationResponse>(created.Value);
        Assert.True(response.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(response.Secret));
        Assert.Equal("https://example.com/hooks/cms", response.Url);
    }

    // ── AC1: Webhook persisted — GET /api/v1/webhooks lists it ───────────────

    [Fact]
    public async Task RegisterThenList_WebhookAppears()
    {
        var userId = await SeedUserAsync();
        var ctrl = Controller(userId);
        var url = $"https://example.com/hooks/{Guid.NewGuid():N}";

        await ctrl.Register(new WebhookRegistrationRequest
        {
            Url    = url,
            Events = new[] { "media.uploaded" },
        });

        var listResult = await ctrl.List();
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var items = Assert.IsAssignableFrom<IEnumerable<WebhookListItem>>(ok.Value);
        Assert.Contains(items, w => w.Url == url);
    }

    // ── AC1: DELETE /api/v1/webhooks/{id} removes it ─────────────────────────

    [Fact]
    public async Task DeleteWebhook_Returns204_AndIsRemovedFromList()
    {
        var userId = await SeedUserAsync();
        var ctrl = Controller(userId);
        var url  = $"https://example.com/hooks/{Guid.NewGuid():N}";

        var registerResult = await ctrl.Register(new WebhookRegistrationRequest
        {
            Url    = url,
            Events = new[] { "content.archived" },
        });
        var created  = Assert.IsType<CreatedAtActionResult>(registerResult);
        var response = Assert.IsType<WebhookRegistrationResponse>(created.Value);

        var deleteResult = await ctrl.Delete(response.Id);
        Assert.IsType<NoContentResult>(deleteResult);

        // After delete it should not appear as active in the list
        var listResult = await ctrl.List();
        var ok = Assert.IsType<OkObjectResult>(listResult);
        var items = Assert.IsAssignableFrom<IEnumerable<WebhookListItem>>(ok.Value);
        Assert.DoesNotContain(items, w => w.Url == url && w.IsActive);
    }

    // ── AC2: Invalid event name rejected ─────────────────────────────────────

    [Fact]
    public async Task RegisterWebhook_InvalidEvent_Returns400()
    {
        var userId = await SeedUserAsync();
        var ctrl   = Controller(userId);
        var result = await ctrl.Register(new WebhookRegistrationRequest
        {
            Url    = "https://example.com/hooks/bad",
            Events = new[] { "not.a.valid.event" },
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── AC2: All four supported event names accepted ──────────────────────────

    [Theory]
    [InlineData("content.published")]
    [InlineData("content.unpublished")]
    [InlineData("content.archived")]
    [InlineData("media.uploaded")]
    public async Task RegisterWebhook_SupportedEvent_Returns201(string eventName)
    {
        var userId = await SeedUserAsync();
        var ctrl   = Controller(userId);
        var result = await ctrl.Register(new WebhookRegistrationRequest
        {
            Url    = $"https://example.com/hooks/{Guid.NewGuid():N}",
            Events = new[] { eventName },
        });
        Assert.IsType<CreatedAtActionResult>(result);
    }

    // ── AC3: HMAC-SHA256 signature ────────────────────────────────────────────

    [Fact]
    public void ComputeSignature_MatchesReferenceImpl()
    {
        const string secret  = "my-signing-secret";
        const string payload = "{\"event\":\"content.published\",\"entryId\":42}";

        // Reference: compute expected with .NET stdlib
        var keyBytes     = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac   = new HMACSHA256(keyBytes);
        var expected     = Convert.ToHexString(hmac.ComputeHash(payloadBytes)).ToLowerInvariant();

        var actual = WebhookDispatcher.ComputeSignature(secret, payload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeSignature_DifferentSecrets_ProduceDifferentSignatures()
    {
        const string payload = "{\"test\":true}";
        var sig1 = WebhookDispatcher.ComputeSignature("secret-one", payload);
        var sig2 = WebhookDispatcher.ComputeSignature("secret-two", payload);
        Assert.NotEqual(sig1, sig2);
    }

    // ── AC4: WebhookDelivery log records attempt, status code, error ──────────

    [Fact]
    public async Task CreateDelivery_RecordsAttemptAndStatusCode()
    {
        var userId = await SeedUserAsync();
        var repo = Repo();

        // First register a webhook to get a valid Id
        var webhookId = await repo.CreateAsync(
            name:        "Delivery-Test",
            url:         "https://example.com/delivery-test",
            secret:      "secret123",
            eventsJson:  "[\"content.published\"]",
            createdById: userId);

        // Log a successful delivery
        var deliveryId = await repo.CreateDeliveryAsync(new WebhookDelivery
        {
            WebhookId          = webhookId,
            EventName          = "content.published",
            PayloadJson        = "{\"entryId\":99}",
            ResponseStatusCode = 200,
            AttemptNumber      = 1,
        });
        Assert.True(deliveryId > 0);

        // Log a failed delivery attempt
        var failedId = await repo.CreateDeliveryAsync(new WebhookDelivery
        {
            WebhookId          = webhookId,
            EventName          = "content.published",
            PayloadJson        = "{\"entryId\":99}",
            ResponseStatusCode = 500,
            AttemptNumber      = 2,
            ErrorMessage       = "Internal Server Error",
        });
        Assert.True(failedId > 0);
        Assert.NotEqual(deliveryId, failedId);
    }

    // ── AC5: Retry logic — dispatcher retries on non-2xx up to 3 times ────────

    [Fact]
    public async Task Dispatcher_RetriesUpToThreeTimes_OnNonSuccessResponse()
    {
        // Arrange: set up a fake HTTP handler that always returns 500
        var callCount = 0;
        var handler   = new FakeHttpHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var repo      = new InMemoryWebhookRepository();
        var webhookId = await repo.CreateAsync("Test", "https://example.com/cb", "sec",
                            "[\"content.published\"]", 1);

        var factory = new FakeHttpClientFactory(handler);
        var logger  = Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookDispatcher>.Instance;
        var dispatcher = new WebhookDispatcher(repo, factory, logger);

        // Act — use very short retry delays for test speed
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await dispatcher.DispatchAsync("content.published",
            new { entryId = 1 }, cts.Token);

        // Assert: exactly 3 attempts logged
        Assert.Equal(3, repo.Deliveries.Count(d => d.WebhookId == webhookId));
        Assert.Equal(3, callCount);
        // All three should record 500
        Assert.All(repo.Deliveries.Where(d => d.WebhookId == webhookId),
            d => Assert.Equal(500, d.ResponseStatusCode));
    }

    [Fact]
    public async Task Dispatcher_StopsRetryingAfterFirstSuccess()
    {
        // Arrange: first call fails, second succeeds
        var callCount = 0;
        var handler   = new FakeHttpHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var repo      = new InMemoryWebhookRepository();
        var webhookId = await repo.CreateAsync("Test2", "https://example.com/cb2", "sec2",
                            "[\"content.published\"]", 1);

        var factory = new FakeHttpClientFactory(handler);
        var logger  = Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookDispatcher>.Instance;
        var dispatcher = new WebhookDispatcher(repo, factory, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await dispatcher.DispatchAsync("content.published",
            new { entryId = 2 }, cts.Token);

        // Exactly 2 attempts: 1 fail + 1 success
        Assert.Equal(2, callCount);
        Assert.Equal(2, repo.Deliveries.Count(d => d.WebhookId == webhookId));
    }

    // ── AC4: usp_Webhook_GetActiveForEvent filters by event ──────────────────

    [Fact]
    public async Task GetActiveForEvent_OnlyReturnsMatchingWebhooks()
    {
        var userId    = await SeedUserAsync();
        var repo      = Repo();
        var uniqueUrl = $"https://example.com/active-test/{Guid.NewGuid():N}";

        await repo.CreateAsync(
            name:        "ActiveTest",
            url:         uniqueUrl,
            secret:      "secret",
            eventsJson:  "[\"content.published\"]",
            createdById: userId);

        var targets = await repo.GetActiveForEventAsync("content.published");
        Assert.Contains(targets, t => t.Url == uniqueUrl);

        var noTargets = await repo.GetActiveForEventAsync("media.uploaded");
        // The one we just registered is for content.published — should not appear for media.uploaded
        Assert.DoesNotContain(noTargets, t => t.Url == uniqueUrl);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>Stores secrets as given (the #168 protector is exercised in Issue168AcceptanceTests).</summary>
internal sealed class PassthroughProtector : IWebhookSecretProtector
{
    public string Protect(string secret) => secret;
    public string Unprotect(string stored) => stored;
    public bool IsProtected(string stored) => false;
}

/// <summary>In-memory webhook repository for dispatcher tests (no DB required).</summary>
internal class InMemoryWebhookRepository : IWebhookRepository
{
    private readonly List<Webhook> _webhooks = new();
    public List<WebhookDelivery> Deliveries { get; } = new();

    public Task<long> CreateAsync(string name, string url, string secret,
        string eventsJson, long createdById)
    {
        var id = _webhooks.Count + 1;
        _webhooks.Add(new Webhook
        {
            Id          = id,
            Name        = name,
            Url         = url,
            Secret      = secret,
            EventsJson  = eventsJson,
            IsActive    = true,
            CreatedById = createdById,
            CreatedAt   = DateTime.UtcNow,
        });
        return Task.FromResult((long)id);
    }

    public Task<IReadOnlyList<Webhook>> ListAllAsync() =>
        Task.FromResult<IReadOnlyList<Webhook>>(_webhooks.Where(w => w.IsActive).ToList());

    public Task<Webhook?> GetByIdAsync(long id) =>
        Task.FromResult(_webhooks.FirstOrDefault(w => w.Id == id));

    public Task DeleteAsync(long id)
    {
        var w = _webhooks.FirstOrDefault(x => x.Id == id);
        if (w is not null) w.IsActive = false;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Webhook>> GetActiveForEventAsync(string eventName)
    {
        var results = _webhooks
            .Where(w => w.IsActive && w.EventsJson.Contains($"\"{eventName}\""))
            .AsEnumerable();
        return Task.FromResult(results);
    }

    public Task<long> CreateDeliveryAsync(WebhookDelivery delivery)
    {
        var id = Deliveries.Count + 1;
        delivery.Id = id;
        Deliveries.Add(delivery);
        return Task.FromResult((long)id);
    }

    // ── #168 ──
    public Task<(IReadOnlyList<WebhookDelivery> Items, int TotalRows)> ListDeliveriesAsync(long webhookId, int page, int pageSize)
    {
        var all = Deliveries.Where(d => d.WebhookId == webhookId).OrderByDescending(d => d.Id).ToList();
        IReadOnlyList<WebhookDelivery> pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((pageItems, all.Count));
    }

    public Task<WebhookDelivery?> GetDeliveryAsync(long deliveryId) =>
        Task.FromResult(Deliveries.FirstOrDefault(d => d.Id == deliveryId));

    public Task<IReadOnlyList<(long Id, string Secret)>> ListSecretsForRekeyAsync() =>
        Task.FromResult<IReadOnlyList<(long, string)>>(
            _webhooks.Where(w => w.Secret is not null && !w.Secret.StartsWith("dp1:", StringComparison.Ordinal))
                     .Select(w => (w.Id, w.Secret!)).ToList());

    public Task UpdateSecretAsync(long id, string protectedSecret)
    {
        var w = _webhooks.First(x => x.Id == id);
        w.Secret = protectedSecret;
        return Task.CompletedTask;
    }
}

/// <summary>Fake HTTP handler for testing without real HTTP calls.</summary>
internal class FakeHttpHandler : DelegatingHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
        InnerHandler = new HttpClientHandler();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_handler(request));
}

/// <summary>Fake IHttpClientFactory that uses a custom handler.</summary>
internal class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly FakeHttpHandler _handler;

    public FakeHttpClientFactory(FakeHttpHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new HttpClient(_handler)
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
}
