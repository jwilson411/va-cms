using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using VA.CMS.API;
using VA.CMS.API.Auth;
using VA.CMS.API.Middleware;
using VA.CMS.API.Observability;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Logging;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #166: observability.
///   - X-Correlation-Id accepted/generated, echoed, pushed onto the Serilog LogContext.
///   - Unhandled exceptions become ProblemDetails with the correlation id and no exception text outside Development.
///   - /health/live is dependency-free; /health/ready runs SQL/storage/settings/SMTP and hides detail from non-developers.
///   - Logging:Sinks options validate at startup; levels map from Logging:LogLevel; PII masking helper.
/// </summary>
public class Issue166AcceptanceTests
{
    // ── correlation id ────────────────────────────────────────────────────────

    [Fact]
    public async Task Correlation_Id_Is_Generated_Echoed_And_Pushed_To_LogContext()
    {
        var events = new List<LogEvent>();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(new ListSink(events)).CreateLogger();

        string? seenInside = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            seenInside = ctx.GetCorrelationId();
            logger.Information("inside");
            return Task.CompletedTask;
        });

        var http = new DefaultHttpContext();
        await middleware.InvokeAsync(http);

        Assert.False(string.IsNullOrEmpty(seenInside));
        Assert.Equal(seenInside, http.Response.Headers[CorrelationIdMiddleware.Header].ToString());
        Assert.Equal(seenInside, ((ScalarValue)events.Single().Properties["CorrelationId"]).Value);

        // Inbound id honoured, sanitised, and applied to the LogContext too.
        events.Clear();
        var withHeader = new DefaultHttpContext();
        withHeader.Request.Headers[CorrelationIdMiddleware.Header] = "lb-42 <x>";
        await middleware.InvokeAsync(withHeader);
        Assert.Equal("lb-42x", withHeader.Response.Headers[CorrelationIdMiddleware.Header].ToString());
        Assert.Equal("lb-42x", ((ScalarValue)events.Single().Properties["CorrelationId"]).Value);
    }

    [Fact]
    public async Task Unhandled_Exception_Becomes_ProblemDetails_With_Correlation_Id_And_No_Exception_Text()
    {
        await using var factory = new ObservabilityFactory(environment: "Staging", contentThrows: true);
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/content/hr/test");
        req.Headers.Add(CorrelationIdMiddleware.Header, "ticket-9001");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
        Assert.Equal("ticket-9001", resp.Headers.GetValues(CorrelationIdMiddleware.Header).Single());
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());   // security headers survive the re-execute

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("ticket-9001", doc.RootElement.GetProperty("correlationId").GetString());
        Assert.False(doc.RootElement.TryGetProperty("exception", out _));
        Assert.DoesNotContain("boom-secret-detail", doc.RootElement.ToString());
    }

    [Fact]
    public async Task Development_ProblemDetails_Carry_The_Exception_For_The_Developer()
    {
        await using var factory = new ObservabilityFactory(contentThrows: true);
        var resp = await factory.CreateClient().GetAsync("/api/v1/content/hr/test");

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out var detailEl), body);
        Assert.Equal("boom-secret-detail", detailEl.GetString());
        Assert.Contains("InvalidOperationException", doc.RootElement.GetProperty("exception").GetString());
    }

    // ── health ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/api/health")]
    [InlineData("/api/health/live")]
    public async Task Liveness_Is_200_Without_Dependencies(string path)
    {
        await using var factory = new ObservabilityFactory(sqlHealthy: false);   // SQL down: liveness does not care
        var resp = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains("no-store", resp.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Readiness_Is_503_When_Sql_Is_Down_And_Hides_Detail_From_Anonymous_Callers()
    {
        await using var factory = new ObservabilityFactory(sqlHealthy: false);
        var resp = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
        Assert.False(doc.RootElement.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task Readiness_Detail_Requires_The_Developer_Role()
    {
        await using var factory = new ObservabilityFactory(sqlHealthy: true);

        var editor = await factory.CreateAuthenticatedClient(CmsRoles.Editor).GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
        using (var doc = JsonDocument.Parse(await editor.Content.ReadAsStringAsync()))
            Assert.False(doc.RootElement.TryGetProperty("checks", out _));

        var dev = await factory.CreateAuthenticatedClient(CmsRoles.Developer).GetAsync("/api/health/ready");
        Assert.Equal(HttpStatusCode.OK, dev.StatusCode);
        using var detail = JsonDocument.Parse(await dev.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", detail.RootElement.GetProperty("status").GetString());
        var checks = detail.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c);
        Assert.Equal(["settings", "smtp", "sql", "storage"], checks.Keys.Order().ToArray());
        Assert.Equal("Healthy", checks["storage"].GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["settings"].GetProperty("status").GetString());
        Assert.Contains("not configured", checks["smtp"].GetProperty("description").GetString());
    }

    [Fact]
    public async Task Settings_Check_Reports_Unloaded_Snapshot_And_Http_Admin_Base_Url()
    {
        var never = new NeverLoadedSettings();
        var unloaded = await new SettingsHealthCheck(never, new EmailOptions(), new FakeEnv("Production")).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, unloaded.Status);

        var relay   = new EmailOptions { Smtp = new SmtpOptions { Host = "relay" } };
        var http    = StaticSiteSettings.Defaults;   // notifications.adminBaseUrl = http://localhost:5173
        var prod    = await new SettingsHealthCheck(http, relay, new FakeEnv("Production")).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, prod.Status);
        Assert.Contains(SiteSettingKeys.NotificationsAdminBaseUrl, prod.Description);

        var dev = await new SettingsHealthCheck(http, relay, new FakeEnv("Development")).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, dev.Status);

        var fixedUp = StaticSiteSettings.Defaults.With(SiteSettingKeys.NotificationsAdminBaseUrl, "https://cms-admin.va.gov");
        Assert.Equal(HealthStatus.Healthy, (await new SettingsHealthCheck(fixedUp, relay, new FakeEnv("Production")).CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task Smtp_Check_Is_Degraded_Not_Unhealthy_When_The_Relay_Is_Down()
    {
        var down = new EmailOptions { Smtp = new SmtpOptions { Host = "127.0.0.1", Port = 9 } };   // discard port: nothing listens
        var result = await new SmtpHealthCheck(down, StaticSiteSettings.Defaults).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, result.Status);

        var off = await new SmtpHealthCheck(down, StaticSiteSettings.Defaults.With(SiteSettingKeys.NotificationsEmailEnabled, false)).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, off.Status);
    }

    // ── logging configuration ─────────────────────────────────────────────────

    [Fact]
    public void Sink_Options_Validate_At_Startup()
    {
        var fileNoPath = new LoggingSinkOptions { File = new FileSinkOptions { Enabled = true } };
        Assert.Contains(fileNoPath.Validate(isDevelopment: false), m => m.Contains("Logging:Sinks:File:Path"));

        var splunkHttp = new LoggingSinkOptions { Splunk = new SplunkSinkOptions { Enabled = true, HecUrl = "http://splunk:8088", Token = "t" } };
        Assert.Empty(splunkHttp.Validate(isDevelopment: true));
        Assert.Contains(splunkHttp.Validate(isDevelopment: false), m => m.Contains("https://"));

        var splunkNoToken = new LoggingSinkOptions { Splunk = new SplunkSinkOptions { Enabled = true, HecUrl = "https://splunk:8088" } };
        Assert.Contains(splunkNoToken.Validate(isDevelopment: false), m => m.Contains("Token"));

        var splunkBadUrl = new LoggingSinkOptions { Splunk = new SplunkSinkOptions { Enabled = true, HecUrl = "splunk", Token = "t" } };
        Assert.Contains(splunkBadUrl.Validate(isDevelopment: false), m => m.Contains("absolute"));

        if (!OperatingSystem.IsWindows())
        {
            var eventLog = new LoggingSinkOptions { EventLog = new EventLogSinkOptions { Enabled = true } };
            Assert.Contains(eventLog.Validate(isDevelopment: false), m => m.Contains("not Windows"));
        }

        Assert.Empty(new LoggingSinkOptions().Validate(isDevelopment: false));   // console only: the default

        // …and they are part of the #173 numbered list.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=x;Database=y;",
            ["Logging:Sinks:File:Enabled"]          = "true",
            ["Logging:Sinks:Splunk:Enabled"]        = "true",
            ["Logging:Sinks:Splunk:QueueLimit"]     = "1",
        }).Build();
        var problems = StartupValidation.Evaluate(config, "Development");
        Assert.Contains(problems, p => p.Contains("Logging:Sinks:File:Path"));
        Assert.Contains(problems, p => p.Contains("Logging:Sinks:Splunk:HecUrl"));
        Assert.Contains(problems, p => p.StartsWith("Logging:Sinks:Splunk:QueueLimit:"));
    }

    [Fact]
    public void Log_Levels_Map_From_The_Standard_Logging_Section()
    {
        Assert.Equal(LogEventLevel.Verbose, SerilogSetup.ParseLevel("Trace", LogEventLevel.Information));
        Assert.Equal(LogEventLevel.Fatal,   SerilogSetup.ParseLevel("Critical", LogEventLevel.Information));
        Assert.Equal(LevelAlias.Off,        SerilogSetup.ParseLevel("None", LogEventLevel.Information));
        Assert.Equal(LogEventLevel.Warning, SerilogSetup.ParseLevel("warn", LogEventLevel.Information));
        Assert.Equal(LogEventLevel.Error,   SerilogSetup.ParseLevel("bogus", LogEventLevel.Error));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"]              = "Warning",
            ["Logging:LogLevel:VA.CMS"]               = "Debug",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "None",
        }).Build();

        var events = new List<LogEvent>();
        var cfg = new LoggerConfiguration().WriteTo.Sink(new ListSink(events));
        SerilogSetup.ApplyLevels(cfg, config.GetSection("Logging:LogLevel"));
        using var logger = cfg.CreateLogger();

        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "VA.CMS.API.Thing").Debug("kept");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Other.Thing").Information("dropped");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Microsoft.AspNetCore.Hosting").Fatal("dropped too");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Other.Thing").Warning("kept");

        Assert.Equal(["kept", "kept"], events.Select(e => e.MessageTemplate.Text).ToArray());
    }

    [Fact]
    public void Serilog_Pipeline_Builds_From_Configuration_With_Enrichers()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"]      = "Information",
            ["Logging:Sinks:Console:Enabled"] = "false",
        }).Build();

        var events = new List<LogEvent>();
        var cfg = new LoggerConfiguration().WriteTo.Sink(new ListSink(events));
        SerilogSetup.Configure(cfg, config, new FakeEnv("Staging"));
        using var logger = cfg.CreateLogger();

        logger.Information("hello");

        var props = events.Single().Properties;
        Assert.Equal("va-cms-api", ((ScalarValue)props["Application"]).Value);
        Assert.Equal("Staging",    ((ScalarValue)props["Environment"]).Value);
        Assert.Equal(Environment.MachineName, ((ScalarValue)props["MachineName"]).Value);
    }

    [Fact]
    public void Email_Addresses_Are_Masked_In_Log_Lines()
    {
        Assert.Equal("a***@va.gov", PiiMask.Email("alice.smith@va.gov"));
        Assert.Equal("***", PiiMask.Email("not-an-address"));
        Assert.Equal("***", PiiMask.Email(null));
        Assert.Equal("***", PiiMask.Email("@va.gov"));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class ListSink(List<LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    private sealed class FakeEnv(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "VA.CMS.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class NeverLoadedSettings : SiteSettingsBase
    {
        public override IReadOnlyList<SiteSettingRow> Snapshot => [];
        public override DateTime? LoadedAtUtc => null;
        public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        protected override string? Lookup(string key) => null;
    }

    private sealed class ThrowingContentRepo : Issue23ContentEntryStub
    {
        public override Task<PublishedContentEntry?> GetPublishedBySlugAsync(string slug, string locale = "en-US")
            => throw new InvalidOperationException("boom-secret-detail");
    }

    private sealed class StubHealthCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
            => Task.FromResult(new HealthCheckResult(status, "stubbed"));
    }

    private sealed class ObservabilityFactory(
        string environment = "Development",
        bool contentThrows = false,
        bool sqlHealthy = true) : WebApplicationFactory<Program>
    {
        public HttpClient CreateAuthenticatedClient(string roleName)
        {
            var client = CreateClient();
            var jwt    = Services.GetRequiredService<IJwtService>();
            var user   = new User { Id = 1, ExternalId = "x", Email = "x@va.gov", DisplayName = "X", IsActive = true };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                jwt.IssueAccessToken(user, [new UserRoleAssignment { RoleId = 1, RoleName = roleName }]));
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Logging:Sinks:Console:Enabled", "false");
            if (environment == "Development")
            {
                builder.UseSetting("Auth:Mode", "WindowsAuth");
                builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
            }
            else
            {
                builder.UseSetting("Auth:Mode", "AzureAd");
                builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
                builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
                builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
                builder.UseSetting("AzureAd:ClientSecret", "test-secret");
            }
            builder.UseSetting("Jwt:SigningKey",  "issue-166-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                Replace<IContentEntryRepository>(services,  _ => contentThrows ? new ThrowingContentRepo() : new Issue23ContentEntryStub());
                Replace<IMediaAssetRepository>(services,    _ => new Issue158AcceptanceTests.AssetRepoStub());
                Replace<IMediaExtendedRepository>(services, _ => new Issue158AcceptanceTests.UsageRepoStub());
                Replace<IStorageBackend>(services,          _ => new Issue158AcceptanceTests.StorageStub());
                Replace<IDbMonitorRepository>(services,     _ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults);

                // The real SQL check would reach for a server; swap its registration for a stub.
                services.PostConfigure<HealthCheckServiceOptions>(o =>
                {
                    var sql = o.Registrations.Single(r => r.Name == "sql");
                    o.Registrations.Remove(sql);
                    o.Registrations.Add(new HealthCheckRegistration("sql",
                        _ => new StubHealthCheck(sqlHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy), null, sql.Tags));
                });
            });
        }

        private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
        {
            foreach (var existing in services.Where(d => d.ServiceType == typeof(T)).ToList())
                services.Remove(existing);
            services.AddScoped<T>(factory);
        }
    }
}
