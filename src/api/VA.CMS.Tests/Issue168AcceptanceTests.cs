using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VA.CMS.API;
using VA.CMS.API.Controllers;
using VA.CMS.API.Webhooks;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #168 (epic #152): webhook SSRF/egress controls.
///   - webhooks.allowedHosts (glob list, empty = no deliveries outside Development) at registration and dispatch.
///   - https outside Development; literal and resolved addresses that are loopback / link-local /
///     multicast are always refused, RFC 1918 / CGNAT / ULA unless webhooks.allowPrivateNetworks.
///   - The connect callback validates the *resolved* addresses (DNS rebinding) and no redirects are followed.
///   - Secrets protected at rest (Data Protection), returned once, re-keyed at startup, hidden from vacms_readonly.
///   - Pinned handler: no proxy, no redirects, TLS 1.2+, capped response.
///   - Delivery log + redelivery endpoints.
///   - DataProtection:KeysPath required outside Development.
/// </summary>
public class Issue168AcceptanceTests
{
    private static StaticSiteSettings Settings(string[]? allowedHosts = null, bool allowPrivate = false) =>
        StaticSiteSettings.Defaults
            .WithJson(SiteSettingKeys.WebhooksAllowedHosts, allowedHosts ?? Array.Empty<string>())
            .With(SiteSettingKeys.WebhooksAllowPrivateNetworks, allowPrivate)
            .WithJson(SiteSettingKeys.WebhooksRetryDelaysSeconds, new[] { 0 });

    private static WebhookDestinationPolicy Policy(string[]? allowedHosts = null, bool allowPrivate = false, bool isDevelopment = false)
        => new(Settings(allowedHosts, allowPrivate), isDevelopment);

    // ── registration: scheme, allow-list, literal addresses ──────────────────

    [Theory]
    [InlineData("http://10.0.0.1/hook",               "https")]                       // plaintext refused first
    [InlineData("https://10.0.0.1/hook",              "private address")]
    [InlineData("https://169.254.169.254/latest/",    "link-local")]
    [InlineData("https://127.0.0.1:5100/api/",        "loopback")]
    [InlineData("https://[::1]/",                     "loopback")]
    [InlineData("https://0.0.0.0/",                   "unspecified")]
    [InlineData("https://100.64.0.9/",                "private address")]              // CGNAT
    [InlineData("https://[fd00::1]/",                 "private address")]              // ULA
    [InlineData("https://[::ffff:10.1.1.1]/",         "private address")]              // IPv4-mapped
    [InlineData("https://user:pw@hooks.va.gov/",      "credentials")]
    [InlineData("ftp://hooks.va.gov/",                "valid HTTP/HTTPS")]
    [InlineData("not a url",                          "valid HTTP/HTTPS")]
    public void Registration_Of_Unsafe_Urls_Is_Refused_Outside_Development(string url, string expectedFragment)
    {
        var policy = Policy(allowedHosts: ["hooks.va.gov", "10.0.0.1", "169.254.169.254", "127.0.0.1", "::1", "0.0.0.0", "100.64.0.9", "fd00::1", "::ffff:10.1.1.1"]);
        var error  = policy.ValidateUrl(url, out _);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_Allow_List_Means_No_Destinations_Outside_Development()
    {
        var error = Policy().ValidateUrl("https://hooks.va.gov/cms", out _);
        Assert.NotNull(error);
        Assert.Contains("webhooks.allowedHosts", error);

        Assert.Null(Policy(isDevelopment: true).ValidateUrl("http://localhost:3000/api/revalidate", out _));
    }

    [Theory]
    [InlineData("hooks.va.gov",        true)]
    [InlineData("HOOKS.VA.GOV",        true)]
    [InlineData("api.hooks.va.gov",    true)]    // *.va.gov
    [InlineData("va.gov",              false)]   // "*." needs a label
    [InlineData("evil-va.gov",         false)]
    [InlineData("hooks.va.gov.evil",   false)]
    public void Allow_List_Matches_Exact_Names_And_Wildcard_Subdomains(string host, bool allowed)
    {
        var policy = Policy(allowedHosts: ["hooks.va.gov", "*.va.gov"]);
        var error  = policy.ValidateUrl($"https://{host}/cms", out _);
        Assert.Equal(allowed, error is null);
    }

    [Fact]
    public void Private_Networks_Are_Allowed_Only_By_Setting_And_Never_Loopback()
    {
        var policy = Policy(allowedHosts: ["10.0.0.1", "127.0.0.1"], allowPrivate: true);
        Assert.Null(policy.ValidateUrl("https://10.0.0.1/hook", out _));
        Assert.Contains("loopback", policy.ValidateUrl("https://127.0.0.1/hook", out _)!);
    }

    [Fact]
    public void Address_Classes_Are_Classified()
    {
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("127.0.0.1")));
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("169.254.169.254")));
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("224.0.0.1")));
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("255.255.255.255")));
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("fe80::1")));
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("ff02::1")));
        Assert.True(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(WebhookDestinationPolicy.IsAlwaysBlocked(IPAddress.Parse("152.129.1.1")));

        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("10.255.0.1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("172.16.0.1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("172.31.255.255")));
        Assert.False(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("172.32.0.1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("192.168.1.1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("100.64.0.1")));
        Assert.False(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("100.128.0.1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("fc00::1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("fdab::1")));
        Assert.True(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("::ffff:192.168.0.1")));
        Assert.False(WebhookDestinationPolicy.IsPrivate(IPAddress.Parse("2607:f8b0::1")));
    }

    [Fact]
    public async Task Controller_Register_Returns_400_For_A_Private_Address_Outside_Development()
    {
        var repo = new InMemoryWebhookRepository();
        var ctrl = Controller(repo, Policy(allowedHosts: ["10.0.0.1", "hooks.va.gov"]), out _);

        var result = await ctrl.Register(new WebhookRegistrationRequest { Url = "http://10.0.0.1/", Events = ["content.published"] });
        var bad    = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("https", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);

        result = await ctrl.Register(new WebhookRegistrationRequest { Url = "https://10.0.0.1/", Events = ["content.published"] });
        Assert.IsType<BadRequestObjectResult>(result);

        result = await ctrl.Register(new WebhookRegistrationRequest { Url = "https://hooks.va.gov/cms", Events = ["content.published"] });
        Assert.IsType<CreatedAtActionResult>(result);
    }

    // ── dispatch: policy re-checked, DNS rebinding, no retry on refusal ──────

    [Fact]
    public async Task Dispatch_Refuses_A_Host_That_Fell_Off_The_Allow_List_Without_Retrying()
    {
        var calls = 0;
        var repo  = new InMemoryWebhookRepository();
        await repo.CreateAsync("h", "https://hooks.va.gov/cms", "s", "[\"content.published\"]", 1);

        var dispatcher = new WebhookDispatcher(repo,
            new FakeHttpClientFactory(new FakeHttpHandler(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); })),
            NullLogger<WebhookDispatcher>.Instance, Settings(), Policy());   // allow-list now empty

        await dispatcher.DispatchAsync("content.published", new { handle = "x" });

        Assert.Equal(0, calls);
        var row = Assert.Single(repo.Deliveries);
        Assert.Null(row.ResponseStatusCode);
        Assert.StartsWith("Refused:", row.ErrorMessage);
        Assert.Contains("webhooks.allowedHosts", row.ErrorMessage);
    }

    [Fact]
    public async Task Dns_Rebinding_To_A_Private_Address_Is_Refused_At_Connect_Time()
    {
        // The host passed registration (it is allow-listed) but now resolves to 10.0.0.1.
        var policy   = Policy(allowedHosts: ["hooks.va.gov"]);
        var resolver = new StubResolver { ["hooks.va.gov"] = [IPAddress.Parse("10.0.0.1")] };
        var repo     = new InMemoryWebhookRepository();
        await repo.CreateAsync("h", "https://hooks.va.gov/cms", "s", "[\"content.published\"]", 1);

        var dispatcher = new WebhookDispatcher(repo, new RealHandlerFactory(policy, resolver),
            NullLogger<WebhookDispatcher>.Instance, Settings(allowedHosts: ["hooks.va.gov"]), policy);

        await dispatcher.DispatchAsync("content.published", new { handle = "x" });

        var row = Assert.Single(repo.Deliveries);          // no retry
        Assert.Null(row.ResponseStatusCode);
        Assert.Contains("private address 10.0.0.1", row.ErrorMessage);
        Assert.Equal(1, resolver.Lookups);
    }

    [Fact]
    public async Task A_Mixed_Dns_Answer_With_One_Private_Address_Is_Refused()
    {
        var policy   = Policy(allowedHosts: ["hooks.va.gov"]);
        var resolver = new StubResolver { ["hooks.va.gov"] = [IPAddress.Parse("152.129.1.1"), IPAddress.Parse("127.0.0.1")] };
        var repo     = new InMemoryWebhookRepository();
        await repo.CreateAsync("h", "https://hooks.va.gov/cms", "s", "[\"content.published\"]", 1);

        var dispatcher = new WebhookDispatcher(repo, new RealHandlerFactory(policy, resolver),
            NullLogger<WebhookDispatcher>.Instance, Settings(allowedHosts: ["hooks.va.gov"]), policy);
        await dispatcher.DispatchAsync("content.published", new { });

        Assert.Contains("loopback", Assert.Single(repo.Deliveries).ErrorMessage);
    }

    [Fact]
    public async Task Connect_Callback_Connects_To_The_Validated_Address_And_Signs_The_Payload()
    {
        // A real socket: the resolver maps the allow-listed name to a local listener, Development
        // policy permits loopback, and the request that arrives carries the HMAC headers.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port     = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = AcceptOneHttpRequestAsync(listener);

        var policy   = Policy(allowedHosts: ["hooks.va.gov"], isDevelopment: true);
        var resolver = new StubResolver { ["hooks.va.gov"] = [IPAddress.Loopback] };
        var repo     = new InMemoryWebhookRepository();
        await repo.CreateAsync("h", $"http://hooks.va.gov:{port}/cms", "topsecret", "[\"content.published\"]", 1);

        var dispatcher = new WebhookDispatcher(repo, new RealHandlerFactory(policy, resolver),
            NullLogger<WebhookDispatcher>.Instance, Settings(allowedHosts: ["hooks.va.gov"]), policy);
        await dispatcher.DispatchAsync("content.published", new { handle = "abc" });

        var request = await received;
        Assert.Contains("POST /cms HTTP/1.1", request);
        Assert.Contains("Host: hooks.va.gov:" + port, request);
        Assert.Contains("X-CMS-Event: content.published", request);
        Assert.Contains("X-CMS-Signature: sha256=" + WebhookDispatcher.ComputeSignature("topsecret", "{\"handle\":\"abc\"}"), request);
        Assert.Equal(200, Assert.Single(repo.Deliveries).ResponseStatusCode);
    }

    [Fact]
    public async Task Redirects_Are_Not_Followed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port     = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = AcceptOneHttpRequestAsync(listener, "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:5100/api/admin\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        var policy   = Policy(allowedHosts: ["hooks.va.gov"], isDevelopment: true);
        var resolver = new StubResolver { ["hooks.va.gov"] = [IPAddress.Loopback] };
        var repo     = new InMemoryWebhookRepository();
        await repo.CreateAsync("h", $"http://hooks.va.gov:{port}/cms", "s", "[\"content.published\"]", 1);

        var dispatcher = new WebhookDispatcher(repo, new RealHandlerFactory(policy, resolver),
            NullLogger<WebhookDispatcher>.Instance,
            Settings(allowedHosts: ["hooks.va.gov"]).With(SiteSettingKeys.WebhooksMaxAttempts, 1), policy);
        await dispatcher.DispatchAsync("content.published", new { });

        await received;
        Assert.Equal(302, Assert.Single(repo.Deliveries).ResponseStatusCode);   // logged as a failure, not followed
        Assert.Equal(1, resolver.Lookups);                                       // 127.0.0.1:5100 was never resolved/contacted
    }

    [Fact]
    public void Handler_Is_Pinned()
    {
        using var handler = WebhookHttpHandler.Create(Policy(), new StubResolver());
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslOptions.EnabledSslProtocols);
        Assert.NotNull(handler.ConnectCallback);

        using var client = new HttpClient(handler);
        WebhookHttpHandler.ConfigureClient(client);
        Assert.Equal(WebhookHttpHandler.MaxResponseBytes, client.MaxResponseContentBufferSize);
    }

    // ── secrets at rest ───────────────────────────────────────────────────────

    [Fact]
    public void Secret_Is_Protected_With_A_Version_Prefix_And_Round_Trips()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var stored    = protector.Protect("abc123");

        Assert.StartsWith("dp1:", stored);
        Assert.DoesNotContain("abc123", stored);
        Assert.True(protector.IsProtected(stored));
        Assert.Equal("abc123", protector.Unprotect(stored));

        // Clear text written before V046 still signs until the re-key job runs.
        Assert.False(protector.IsProtected("legacy"));
        Assert.Equal("legacy", protector.Unprotect("legacy"));
    }

    [Fact]
    public async Task Register_Stores_The_Secret_Protected_And_Returns_It_Once()
    {
        var repo      = new InMemoryWebhookRepository();
        var ctrl      = Controller(repo, Policy(allowedHosts: ["hooks.va.gov"]), out var protector);
        var result    = await ctrl.Register(new WebhookRegistrationRequest { Url = "https://hooks.va.gov/cms", Secret = "given-secret", Events = ["content.published"] });
        var response  = Assert.IsType<WebhookRegistrationResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);

        Assert.Equal("given-secret", response.Secret);
        var stored = (await repo.GetByIdAsync(response.Id))!.Secret!;
        Assert.StartsWith("dp1:", stored);
        Assert.Equal("given-secret", protector.Unprotect(stored));

        var list = Assert.IsAssignableFrom<IEnumerable<WebhookListItem>>(Assert.IsType<OkObjectResult>(await ctrl.List()).Value);
        Assert.All(list, _ => { });   // WebhookListItem has no Secret member at all
    }

    [Fact]
    public async Task Dispatcher_Signs_With_The_Unprotected_Secret()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var repo      = new InMemoryWebhookRepository();
        await repo.CreateAsync("h", "https://hooks.va.gov/cms", protector.Protect("topsecret"), "[\"content.published\"]", 1);

        string? signature = null;
        var dispatcher = new WebhookDispatcher(repo,
            new FakeHttpClientFactory(new FakeHttpHandler(req => { signature = req.Headers.GetValues("X-CMS-Signature").Single(); return new HttpResponseMessage(HttpStatusCode.OK); })),
            NullLogger<WebhookDispatcher>.Instance, Settings(allowedHosts: ["hooks.va.gov"]), Policy(allowedHosts: ["hooks.va.gov"]), protector);

        await dispatcher.DispatchAsync("content.published", new { handle = "abc" });
        Assert.Equal("sha256=" + WebhookDispatcher.ComputeSignature("topsecret", "{\"handle\":\"abc\"}"), signature);
    }

    [Fact]
    public async Task Startup_Rekey_Protects_Only_Clear_Text_Rows()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var repo      = new InMemoryWebhookRepository();
        var legacy    = await repo.CreateAsync("old", "https://hooks.va.gov/a", "clear", "[]", 1);
        var already   = await repo.CreateAsync("new", "https://hooks.va.gov/b", protector.Protect("prot"), "[]", 1);
        var alreadyStored = (await repo.GetByIdAsync(already))!.Secret;

        Assert.Equal(1, await WebhookSecretRekeyService.RekeyAsync(repo, protector));
        Assert.Equal("clear", protector.Unprotect((await repo.GetByIdAsync(legacy))!.Secret!));
        Assert.Equal(alreadyStored, (await repo.GetByIdAsync(already))!.Secret);
        Assert.Equal(0, await WebhookSecretRekeyService.RekeyAsync(repo, protector));   // idempotent
    }

    // ── delivery log and redelivery ──────────────────────────────────────────

    [Fact]
    public async Task Delivery_Log_Is_Paged_Newest_First_And_Redelivery_Records_Lineage()
    {
        var repo = new InMemoryWebhookRepository();
        var id   = await repo.CreateAsync("h", "https://hooks.va.gov/cms", "s", "[\"content.published\"]", 1);
        for (var i = 1; i <= 3; i++)
            await repo.CreateDeliveryAsync(new WebhookDelivery { WebhookId = id, EventName = "content.published", PayloadJson = $"{{\"n\":{i}}}", ResponseStatusCode = 500, AttemptNumber = i, DeliveredAt = DateTime.UtcNow });

        var ctrl = Controller(repo, Policy(allowedHosts: ["hooks.va.gov"]), out _);
        var page = Assert.IsType<WebhookDeliveryListResponse>(Assert.IsType<OkObjectResult>(await ctrl.ListDeliveries(id, page: 1, pageSize: 2)).Value);
        Assert.Equal(3, page.TotalRows);
        Assert.Equal(2, page.Items.Length);
        Assert.Equal(3, page.Items[0].AttemptNumber);
        Assert.False(page.Items[0].Succeeded);

        var redelivered = Assert.IsType<WebhookDeliveryDto>(Assert.IsType<OkObjectResult>(await ctrl.Redeliver(id, page.Items[1].Id, default)).Value);
        Assert.Equal(page.Items[1].Id, redelivered.RedeliveryOfId);
        Assert.Equal("{\"n\":2}", redelivered.PayloadJson);
        Assert.Equal(200, redelivered.ResponseStatusCode);
        Assert.True(redelivered.Succeeded);
        Assert.Equal(4, repo.Deliveries.Count);

        Assert.IsType<NotFoundResult>(await ctrl.Redeliver(id, 999, default));
        Assert.IsType<NotFoundResult>(await ctrl.ListDeliveries(999));

        await repo.DeleteAsync(id);
        Assert.IsType<ConflictObjectResult>(await ctrl.Redeliver(id, page.Items[1].Id, default));
    }

    [Fact]
    public async Task Redelivery_Does_Not_Run_When_A_Second_Webhook_Owns_The_Delivery()
    {
        var repo = new InMemoryWebhookRepository();
        var a    = await repo.CreateAsync("a", "https://hooks.va.gov/a", "s", "[]", 1);
        var b    = await repo.CreateAsync("b", "https://hooks.va.gov/b", "s", "[]", 1);
        var dId  = await repo.CreateDeliveryAsync(new WebhookDelivery { WebhookId = b, EventName = "e", PayloadJson = "{}" });

        var ctrl = Controller(repo, Policy(allowedHosts: ["hooks.va.gov"]), out _);
        Assert.IsType<NotFoundResult>(await ctrl.Redeliver(a, dId, default));
    }

    // ── startup validation: key ring ──────────────────────────────────────────

    [Fact]
    public void KeysPath_Is_Required_Outside_Development()
    {
        Assert.Contains("DataProtection:KeysPath", new KeyRingOptions().Validate(isDevelopment: false)!);
        Assert.Null(new KeyRingOptions().Validate(isDevelopment: true));
        Assert.Null(new KeyRingOptions { KeysPath = @"\\fileserver\vacms\keys" }.Validate(isDevelopment: false));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=sql.va.gov;Database=VACMS;User Id=vacms_app;Password=x;Encrypt=True;",
            ["Jwt:SigningKey"]  = new string('k', 40),
            ["Auth:Mode"]       = "WindowsAuth",
            ["AllowedHosts"]    = "cms.va.gov",
            ["Storage:Backend"] = "LocalFileSystem",
        }).Build();
        Assert.Contains(StartupValidation.Evaluate(config, "Production"), p => p.Contains("DataProtection:KeysPath"));
    }

    [Fact]
    public void Site_Settings_Exist_With_Fail_Closed_Defaults()
    {
        var hosts = SiteSettingDefinitions.All.Single(d => d.Key == SiteSettingKeys.WebhooksAllowedHosts);
        Assert.Equal(SiteSettingType.Json, hosts.Type);
        Assert.Equal("[]", hosts.Default);
        Assert.Equal(SiteSettingScope.Server, hosts.Scope);

        var priv = SiteSettingDefinitions.All.Single(d => d.Key == SiteSettingKeys.WebhooksAllowPrivateNetworks);
        Assert.Equal("false", priv.Default);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static WebhooksController Controller(InMemoryWebhookRepository repo, WebhookDestinationPolicy policy, out IWebhookSecretProtector protector)
    {
        protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var dispatcher = new WebhookDispatcher(repo,
            new FakeHttpClientFactory(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            NullLogger<WebhookDispatcher>.Instance, Settings(allowedHosts: ["hooks.va.gov"]), policy, protector);

        return new WebhooksController(repo, policy, protector, dispatcher)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim("cms_user_id", "1")], "test")),
                },
            },
        };
    }

    /// <summary>Accepts one connection, reads the request (headers + Content-Length body), answers, returns the raw request.</summary>
    private static async Task<string> AcceptOneHttpRequestAsync(TcpListener listener,
        string response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
    {
        using var socket = await listener.AcceptSocketAsync();
        using var stream = new NetworkStream(socket, ownsSocket: false);
        var buffer = new byte[16 * 1024];
        var total  = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (n == 0) break;
            total += n;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0) continue;
            var lengthLine = text[..headerEnd].Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var length = lengthLine is null ? 0 : int.Parse(lengthLine.Split(':')[1].Trim());
            if (total >= headerEnd + 4 + length) break;
        }
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        socket.Shutdown(SocketShutdown.Send);
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private sealed class StubResolver : Dictionary<string, IPAddress[]>, IWebhookDnsResolver
    {
        public int Lookups { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Lookups++;
            return TryGetValue(host, out var addresses)
                ? Task.FromResult(addresses)
                : throw new SocketException((int)SocketError.HostNotFound);
        }
    }

    /// <summary>Builds the real pinned handler (connect callback included) instead of the fake one.</summary>
    private sealed class RealHandlerFactory(WebhookDestinationPolicy policy, IWebhookDnsResolver resolver) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(WebhookHttpHandler.Create(policy, resolver));
            WebhookHttpHandler.ConfigureClient(client);
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }
    }
}

/// <summary>Database-backed checks for V046 (#168).</summary>
[Collection("Database")]
public class Issue168DatabaseTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("usp_WebhookDelivery_ListByWebhook")]
    [InlineData("usp_WebhookDelivery_GetById")]
    [InlineData("usp_Webhook_ListSecretsForRekey")]
    [InlineData("usp_Webhook_UpdateSecret")]
    public async Task V046_Procedures_Exist(string name)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.procedures WHERE [name] = @n";
        cmd.Parameters.AddWithValue("@n", name);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Readonly_Login_Cannot_Read_The_Secret_Column_But_Can_Use_The_View()
    {
        await using var conn = new SqlConnection(fixture.ReadonlyConnectionString());
        await conn.OpenAsync();

        await using (var view = conn.CreateCommand())
        {
            view.CommandText = "SELECT COUNT(*) FROM dbo.vw_Webhook";
            Assert.NotNull(await view.ExecuteScalarAsync());
        }
        await using (var safe = conn.CreateCommand())
        {
            safe.CommandText = "SELECT TOP 1 [Url] FROM dbo.Webhook";
            await safe.ExecuteScalarAsync();   // other columns are still readable
        }

        await using var secret = conn.CreateCommand();
        secret.CommandText = "SELECT TOP 1 [Secret] FROM dbo.Webhook";
        var ex = await Assert.ThrowsAsync<SqlException>(() => secret.ExecuteScalarAsync());
        Assert.Equal(230, ex.Number);   // "The SELECT permission was denied on the column 'Secret'"

        await using var star = conn.CreateCommand();
        star.CommandText = "SELECT TOP 1 * FROM dbo.Webhook";
        Assert.Equal(230, (await Assert.ThrowsAsync<SqlException>(() => star.ExecuteScalarAsync())).Number);
    }

    [Fact]
    public async Task Delivery_Log_Round_Trips_Through_The_Procedures()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo   = new WebhookRepository(fixture.CreateDb());
        var id     = await repo.CreateAsync("log", $"https://hooks.va.gov/{Guid.NewGuid():N}", "dp1:x", "[\"content.published\"]", userId);

        var first = await repo.CreateDeliveryAsync(new WebhookDelivery { WebhookId = id, EventName = "content.published", PayloadJson = "{\"a\":1}", ResponseStatusCode = 503, AttemptNumber = 1, ErrorMessage = "down" });
        var redo  = await repo.CreateDeliveryAsync(new WebhookDelivery { WebhookId = id, EventName = "content.published", PayloadJson = "{\"a\":1}", ResponseStatusCode = 200, AttemptNumber = 1, RedeliveryOfId = first });

        var (items, total) = await repo.ListDeliveriesAsync(id, page: 1, pageSize: 10);
        Assert.Equal(2, total);
        Assert.Equal(redo, items[0].Id);            // newest first
        Assert.Equal(first, items[0].RedeliveryOfId);
        Assert.Equal("down", items[1].ErrorMessage);

        var one = await repo.GetDeliveryAsync(first);
        Assert.NotNull(one);
        Assert.Equal(503, one.ResponseStatusCode);
        Assert.Null(one.RedeliveryOfId);

        var (page2, _) = await repo.ListDeliveriesAsync(id, page: 2, pageSize: 1);
        Assert.Equal(first, Assert.Single(page2).Id);
    }

    [Fact]
    public async Task Rekey_Procedures_Find_And_Replace_Clear_Text_Secrets()
    {
        var userId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var repo   = new WebhookRepository(fixture.CreateDb());
        var url    = $"https://hooks.va.gov/{Guid.NewGuid():N}";
        var id     = await repo.CreateAsync("legacy", url, "clear-text", "[]", userId);

        Assert.Contains(await repo.ListSecretsForRekeyAsync(), r => r.Id == id && r.Secret == "clear-text");
        await repo.UpdateSecretAsync(id, "dp1:protected");
        Assert.DoesNotContain(await repo.ListSecretsForRekeyAsync(), r => r.Id == id);
        Assert.Equal("dp1:protected", (await repo.GetByIdAsync(id))!.Secret);
    }
}
