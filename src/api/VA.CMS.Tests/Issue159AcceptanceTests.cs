using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #159: real virus scanning (SI-3).
///
///   - ICAP RESPMOD and ClamAV INSTREAM adapters speak their wire protocols
///     (proved against in-process fake servers) and classify Clean / Infected /
///     Unavailable.
///   - FailClosed: an unreachable engine rejects the upload with 503, stores
///     nothing and writes an audit event; fail-open stores with a NULL verdict.
///   - Production refuses Media:Scanner:Mode=Disabled.
///   - EICAR against a live clamd runs when CLAMAV_HOST is set (docker compose --profile clamav).
/// </summary>
public class Issue159AcceptanceTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // ── protocol parsing ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("stream: OK\0",                          VirusScanVerdict.Clean,       null)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0",  VirusScanVerdict.Infected,    "Eicar-Test-Signature")]
    [InlineData("INSTREAM size limit exceeded. ERROR\0", VirusScanVerdict.Unavailable, null)]
    [InlineData("",                                      VirusScanVerdict.Unavailable, null)]
    public void ClamAv_Reply_Parsing(string reply, VirusScanVerdict verdict, string? threat)
    {
        var result = ClamAvVirusScanService.Parse(reply);
        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(threat, result.ThreatName);
    }

    [Fact]
    public void Icap_Response_Parsing()
    {
        Assert.Equal(VirusScanVerdict.Clean,
            IcapVirusScanService.Parse("ICAP/1.0 204 No Content\r\nISTag: \"x\"\r\n\r\n").Verdict);

        var infected = IcapVirusScanService.Parse(
            "ICAP/1.0 200 OK\r\nX-Infection-Found: Type=0; Resolution=2; Threat=Eicar-Test-Signature;\r\nEncapsulated: res-hdr=0, res-body=80\r\n\r\nHTTP/1.1 403 Forbidden\r\n");
        Assert.Equal(VirusScanVerdict.Infected, infected.Verdict);
        Assert.Equal("Eicar-Test-Signature", infected.ThreatName);

        Assert.Equal(VirusScanVerdict.Infected,
            IcapVirusScanService.Parse("ICAP/1.0 200 OK\r\nX-Virus-ID: Trojan.Foo\r\n\r\nHTTP/1.1 200 OK\r\n").Verdict);
        Assert.Equal(VirusScanVerdict.Infected,
            IcapVirusScanService.Parse("ICAP/1.0 200 OK\r\nEncapsulated: res-hdr=0\r\n\r\nHTTP/1.1 403 Forbidden\r\n").Verdict);
        Assert.Equal(VirusScanVerdict.Clean,
            IcapVirusScanService.Parse("ICAP/1.0 200 OK\r\nEncapsulated: res-hdr=0\r\n\r\nHTTP/1.1 200 OK\r\n").Verdict);
        Assert.Equal(VirusScanVerdict.Unavailable,
            IcapVirusScanService.Parse("ICAP/1.0 500 Server Error\r\n\r\n").Verdict);
        Assert.Equal(VirusScanVerdict.Unavailable,
            IcapVirusScanService.Parse("garbage").Verdict);
    }

    // ── wire protocols against fake servers ───────────────────────────────────

    [Theory]
    [InlineData("stream: OK\0",                         VirusScanVerdict.Clean)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0", VirusScanVerdict.Infected)]
    public async Task ClamAv_Instream_Round_Trip(string reply, VirusScanVerdict expected)
    {
        var payload = Encoding.ASCII.GetBytes("hello clamd");
        byte[]? received = null;

        using var server = FakeTcpServer.Start(async (net, ct) =>
        {
            received = await FakeTcpServer.ReadClamdInstreamAsync(net, ct);
            await net.WriteAsync(Encoding.ASCII.GetBytes(reply), ct);
        });

        var svc = new ClamAvVirusScanService(new MediaScannerOptions { Mode = MediaScannerMode.ClamAv, Host = "127.0.0.1", Port = server.Port, TimeoutSeconds = 5 });
        var result = await svc.ScanDetailedAsync(new MemoryStream(payload));

        Assert.Equal(expected, result.Verdict);
        Assert.Equal(payload, received);
    }

    [Theory]
    [InlineData("ICAP/1.0 204 No Content\r\nISTag: \"1\"\r\n\r\n", VirusScanVerdict.Clean)]
    [InlineData("ICAP/1.0 200 OK\r\nX-Infection-Found: Type=0; Resolution=2; Threat=EICAR;\r\nEncapsulated: res-hdr=0, res-body=50\r\n\r\nHTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n", VirusScanVerdict.Infected)]
    [InlineData("ICAP/1.0 503 Service Unavailable\r\n\r\n", VirusScanVerdict.Unavailable)]
    public async Task Icap_Respmod_Round_Trip(string reply, VirusScanVerdict expected)
    {
        string? request = null;
        using var server = FakeTcpServer.Start(async (net, ct) =>
        {
            request = await FakeTcpServer.ReadUntilChunkTerminatorAsync(net, ct);
            await net.WriteAsync(Encoding.ASCII.GetBytes(reply), ct);
        });

        var svc = new IcapVirusScanService(new MediaScannerOptions { Mode = MediaScannerMode.Icap, Host = "127.0.0.1", Port = server.Port, ServicePath = "/avscan", TimeoutSeconds = 5 });
        var result = await svc.ScanDetailedAsync(new MemoryStream(Png));

        Assert.Equal(expected, result.Verdict);
        Assert.StartsWith($"RESPMOD icap://127.0.0.1:{server.Port}/avscan ICAP/1.0\r\n", request);
        Assert.Contains("Allow: 204", request);
        Assert.Contains("Encapsulated: req-hdr=0, res-hdr=", request);
        Assert.EndsWith("0\r\n\r\n", request);
    }

    [Fact]
    public async Task Unreachable_Engine_Is_Unavailable_Not_Infected()
    {
        // Port 1 on loopback is not listening.
        var clam = new ClamAvVirusScanService(new MediaScannerOptions { Mode = MediaScannerMode.ClamAv, Host = "127.0.0.1", Port = 1, TimeoutSeconds = 2 });
        var icap = new IcapVirusScanService(new MediaScannerOptions { Mode = MediaScannerMode.Icap, Host = "127.0.0.1", Port = 1, TimeoutSeconds = 2 });

        Assert.Equal(VirusScanVerdict.Unavailable, (await clam.ScanDetailedAsync(new MemoryStream(Png))).Verdict);
        Assert.Equal(VirusScanVerdict.Unavailable, (await icap.ScanDetailedAsync(new MemoryStream(Png))).Verdict);
        await Assert.ThrowsAsync<IOException>(() => clam.ScanAsync(new MemoryStream(Png)));
    }

    [Fact]
    public void Options_Defaults()
    {
        Assert.Equal(1344, new MediaScannerOptions { Mode = MediaScannerMode.Icap }.EffectivePort);
        Assert.Equal(3310, new MediaScannerOptions { Mode = MediaScannerMode.ClamAv }.EffectivePort);
        Assert.True(new MediaScannerOptions().ResolveFailClosed(isDevelopment: false));
        Assert.False(new MediaScannerOptions().ResolveFailClosed(isDevelopment: true));
        Assert.True(new MediaScannerOptions { FailClosed = true }.ResolveFailClosed(isDevelopment: true));
    }

    // ── upload service: fail closed / fail open / infected ────────────────────

    [Fact]
    public async Task FailClosed_Unavailable_Engine_Rejects_Stores_Nothing_And_Audits()
    {
        var storage = new InMemoryStorageBackend();
        var assets  = new CountingAssetRepo();
        var audit   = new Issue67AuditLogStub();
        var service = new MediaUploadService(storage, assets, new NoOpImageProcessingService(),
            new FixedScanner(VirusScanResult.Unavailable("down")), new NoOpUsageRepo(), StaticSiteSettings.Defaults,
            failClosed: true, audit: audit);

        var outcome = await service.UploadAsync(FormFile(Png, "a.png", "image/png"), 7);

        Assert.Null(outcome.Asset);
        Assert.Equal(MediaUploadFailure.ScannerUnavailable, outcome.Failure);
        Assert.Equal(0, assets.Created);
        Assert.Empty(storage.Paths);
        Assert.Contains(audit.Writes, w => w.Action == "VirusScanUnavailable" && w.ActorId == 7);
    }

    [Fact]
    public async Task FailOpen_Unavailable_Engine_Stores_With_Null_Verdict_And_Audits()
    {
        var storage = new InMemoryStorageBackend();
        var usage   = new NoOpUsageRepo();
        var audit   = new Issue67AuditLogStub();
        var service = new MediaUploadService(storage, new CountingAssetRepo(), new NoOpImageProcessingService(),
            new FixedScanner(VirusScanResult.Unavailable("down")), usage, StaticSiteSettings.Defaults,
            failClosed: false, audit: audit);

        var (asset, error) = await service.UploadAsync(FormFile(Png, "a.png", "image/png"), 7);

        Assert.Null(error);
        Assert.Null(asset!.IsVirusScanPassed);
        Assert.Empty(usage.ScanResults);                     // never marked passed
        Assert.Single(storage.Paths);
        Assert.Contains(audit.Writes, w => w.Action == "VirusScanSkipped");
    }

    [Fact]
    public async Task Infected_File_Is_Removed_Tombstoned_And_Audited()
    {
        var storage = new InMemoryStorageBackend();
        var usage   = new NoOpUsageRepo();
        var audit   = new Issue67AuditLogStub();
        var service = new MediaUploadService(storage, new CountingAssetRepo(), new NoOpImageProcessingService(),
            new FixedScanner(VirusScanResult.Infected("Eicar-Test-Signature")), usage, StaticSiteSettings.Defaults,
            failClosed: true, audit: audit);

        var outcome = await service.UploadAsync(FormFile(Png, "a.png", "image/png"), 7);

        Assert.Equal(MediaUploadFailure.Infected, outcome.Failure);
        Assert.Empty(storage.Paths);
        Assert.Equal([false], usage.ScanResults);
        Assert.Contains(audit.Writes, w => w.Action == "VirusDetected" && w.DiffJson!.Contains("Eicar-Test-Signature"));
    }

    [Fact]
    public async Task Disabled_Mode_Still_Passes_Clean_Uploads()
    {
        var service = new MediaUploadService(new InMemoryStorageBackend(), new CountingAssetRepo(), new NoOpImageProcessingService(),
            new NoOpVirusScanService(), new NoOpUsageRepo());

        var (asset, error) = await service.UploadAsync(FormFile(Png, "a.png", "image/png"), 1);

        Assert.Null(error);
        Assert.True(asset!.IsVirusScanPassed);
    }

    // ── host ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_Returns_503_When_Engine_Unavailable_And_FailClosed()
    {
        await using var factory = new ScannerHostFactory(new FixedScanner(VirusScanResult.Unavailable("down")), failClosed: true);
        var client = factory.CreateAuthenticatedClient(CmsRoles.Editor);

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "a.png");

        var resp = await client.PostAsync("/api/v1/media/upload", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Contains("scanning service is unavailable", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Production_Refuses_Disabled_Scanner()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new ScannerHostFactory(new NoOpVirusScanService(), failClosed: true, environment: "Production", mode: "Disabled");
            factory.CreateClient();
        });

        Assert.Contains("Media:Scanner:Mode=Disabled", ex.Message);
    }

    // ── live ClamAV (opt-in) ──────────────────────────────────────────────────

    [Fact]
    public async Task Eicar_Is_Rejected_By_Live_ClamAv_When_Configured()
    {
        var host = Environment.GetEnvironmentVariable("CLAMAV_HOST");
        if (string.IsNullOrWhiteSpace(host))
            return;   // docker compose --profile clamav up -d && CLAMAV_HOST=localhost dotnet test

        var port = int.TryParse(Environment.GetEnvironmentVariable("CLAMAV_PORT"), out var p) ? p : 3310;
        var svc  = new ClamAvVirusScanService(new MediaScannerOptions { Mode = MediaScannerMode.ClamAv, Host = host, Port = port, TimeoutSeconds = 60 });

        const string eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var infected = await svc.ScanDetailedAsync(new MemoryStream(Encoding.ASCII.GetBytes(eicar)));
        var clean    = await svc.ScanDetailedAsync(new MemoryStream(Png));

        Assert.Equal(VirusScanVerdict.Infected, infected.Verdict);
        Assert.Contains("Eicar", infected.ThreatName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(VirusScanVerdict.Clean, clean.Verdict);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IFormFile FormFile(byte[] content, string fileName, string contentType)
        => new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(), ContentType = contentType,
        };

    private sealed class FixedScanner(VirusScanResult result) : IVirusScanService
    {
        public Task<bool> ScanAsync(Stream stream, CancellationToken ct = default) => Task.FromResult(result.IsClean);
        public Task<VirusScanResult> ScanDetailedAsync(Stream stream, CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class CountingAssetRepo : IMediaAssetRepository
    {
        public int Created { get; private set; }
        public Task<MediaAsset?> GetByIdAsync(long id) => Task.FromResult<MediaAsset?>(null);
        public Task<PetaPoco.Page<MediaAsset>> ListAsync(int page, int pageSize, string? mimeTypePrefix = null, string? searchTerm = null)
            => Task.FromResult(new PetaPoco.Page<MediaAsset> { Items = [] });
        public Task<long> CreateAsync(MediaAsset asset) { Created++; return Task.FromResult((long)Created); }
        public Task UpdateAsync(MediaAsset asset) => Task.CompletedTask;
        public Task UpdateWebPPathAsync(long id, string webPStoragePath) => Task.CompletedTask;
    }

    private sealed class NoOpUsageRepo : IMediaExtendedRepository
    {
        public List<bool> ScanResults { get; } = new();
        public Task SetVirusScanResultAsync(long assetId, bool passed) { ScanResults.Add(passed); return Task.CompletedTask; }
        public Task<IEnumerable<MediaUsageDetail>> GetUsageAsync(long mediaAssetId) => Task.FromResult<IEnumerable<MediaUsageDetail>>([]);
        public Task<IEnumerable<MediaUsageWithTitle>> GetUsageWithTitleAsync(long mediaAssetId) => Task.FromResult<IEnumerable<MediaUsageWithTitle>>([]);
        public Task<int> SafeDeleteAsync(long assetId) => Task.FromResult(0);
        public Task UpsertUsageAsync(long mediaAssetId, long contentEntryId, string fieldName) => Task.CompletedTask;
        public Task DeleteUsageForEntryAsync(long contentEntryId) => Task.CompletedTask;
    }

    private sealed class ScannerHostFactory(IVirusScanService scanner, bool failClosed, string environment = "Development", string mode = "ClamAv")
        : WebApplicationFactory<Program>
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
            builder.UseSetting("AllowedHosts", "localhost");   // #162: wildcard refused outside Development
            builder.UseSetting("DataProtection:KeysPath", TestKeyRing.Path);   // #168: key ring required outside Development
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Media:Scanner:Mode", mode);
            builder.UseSetting("Media:Scanner:Host", "127.0.0.1");
            builder.UseSetting("Media:Scanner:Port", "1");
            builder.UseSetting("Media:Scanner:FailClosed", failClosed ? "true" : "false");
            builder.UseSetting("Jwt:SigningKey",  "issue-159-acceptance-key-32chars!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
            builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("AzureAd:ClientSecret", "test-secret");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

            builder.ConfigureServices(services =>
            {
                Replace<IVirusScanService>(services,        _ => scanner);
                Replace<IMediaAssetRepository>(services,    _ => new CountingAssetRepo());
                Replace<IMediaExtendedRepository>(services, _ => new NoOpUsageRepo());
                Replace<IStorageBackend>(services,          _ => new InMemoryStorageBackend());
                Replace<IAuditLogRepository>(services,      _ => new Issue67AuditLogStub());
                Replace<IDbMonitorRepository>(services,     _ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
                services.AddSingleton<ISiteSettingsService>(StaticSiteSettings.Defaults);
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

/// <summary>Loopback TCP server that runs one handler per accepted connection.</summary>
internal sealed class FakeTcpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    private FakeTcpServer(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static FakeTcpServer Start(Func<NetworkStream, CancellationToken, Task> handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new FakeTcpServer(listener);
        _ = Task.Run(async () =>
        {
            try
            {
                while (!server._cts.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(server._cts.Token);
                    await using var net = client.GetStream();
                    await handler(net, server._cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });
        return server;
    }

    /// <summary>Consumes "zINSTREAM\0" plus length-prefixed chunks up to the zero chunk; returns the payload.</summary>
    public static async Task<byte[]> ReadClamdInstreamAsync(NetworkStream net, CancellationToken ct)
    {
        var command = new byte[10];
        await ReadExactAsync(net, command, ct);
        Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));

        using var payload = new MemoryStream();
        var prefix = new byte[4];
        while (true)
        {
            await ReadExactAsync(net, prefix, ct);
            var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(prefix);
            if (len == 0) break;
            var chunk = new byte[len];
            await ReadExactAsync(net, chunk, ct);
            payload.Write(chunk);
        }
        return payload.ToArray();
    }

    /// <summary>Reads an ICAP request until the chunked-body terminator.</summary>
    public static async Task<string> ReadUntilChunkTerminatorAsync(NetworkStream net, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[4096];
        while (true)
        {
            var n = await net.ReadAsync(buf, ct);
            if (n == 0) break;
            ms.Write(buf, 0, n);
            if (Encoding.ASCII.GetString(ms.ToArray()).EndsWith("0\r\n\r\n", StringComparison.Ordinal)) break;
        }
        return Encoding.ASCII.GetString(ms.ToArray());
    }

    private static async Task ReadExactAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) throw new EndOfStreamException();
            total += n;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
