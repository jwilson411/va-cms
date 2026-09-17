using System.Net.Sockets;
using System.Text;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// ICAP (RFC 3507) RESPMOD adapter (#159): the upload is wrapped in a synthetic
/// HTTP response and handed to the enterprise scanner's service path. A 204
/// answer means unmodified/clean; a 200 answer carries the engine's verdict in
/// X-Infection-Found / X-Virus-ID headers (or an encapsulated 403 block page).
/// Anything else — connection failure, timeout, ICAP 4xx/5xx — is Unavailable.
/// </summary>
public sealed class IcapVirusScanService(MediaScannerOptions options) : IVirusScanService
{
    private const int ChunkSize = 64 * 1024;

    public async Task<bool> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        var result = await ScanDetailedAsync(stream, ct);
        return result.Verdict switch
        {
            VirusScanVerdict.Clean    => true,
            VirusScanVerdict.Infected => false,
            _ => throw new IOException(result.Detail ?? "ICAP scanner unavailable"),
        };
    }

    public async Task<VirusScanResult> ScanDetailedAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.Timeout);
            var token = timeout.Token;

            using var client = new TcpClient();
            await client.ConnectAsync(options.Host, options.EffectivePort, token);
            await using var net = client.GetStream();

            var length  = stream.CanSeek ? stream.Length - stream.Position : (long?)null;
            var reqHdr  = "GET /upload HTTP/1.1\r\nHost: va-cms\r\n\r\n";
            var resHdr  = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
                        + (length is null ? string.Empty : $"Content-Length: {length}\r\n") + "\r\n";
            var icapHdr = $"RESPMOD icap://{options.Host}:{options.EffectivePort}{options.ServicePath} ICAP/1.0\r\n"
                        + $"Host: {options.Host}\r\n"
                        + "User-Agent: VA-CMS/1.0\r\n"
                        + "Allow: 204\r\n"
                        + "Connection: close\r\n"
                        + $"Encapsulated: req-hdr=0, res-hdr={reqHdr.Length}, res-body={reqHdr.Length + resHdr.Length}\r\n\r\n";

            await net.WriteAsync(Encoding.ASCII.GetBytes(icapHdr + reqHdr + resHdr), token);

            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                await net.WriteAsync(Encoding.ASCII.GetBytes($"{read:X}\r\n"), token);
                await net.WriteAsync(buffer.AsMemory(0, read), token);
                await net.WriteAsync("\r\n"u8.ToArray(), token);
            }
            await net.WriteAsync("0\r\n\r\n"u8.ToArray(), token);
            await net.FlushAsync(token);

            var response = await ReadResponseAsync(net, token);
            return Parse(response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return VirusScanResult.Unavailable($"ICAP {options.Host}:{options.EffectivePort}{options.ServicePath}: {ex.Message}");
        }
    }

    /// <summary>Interprets the ICAP status line and headers (and, for 200, the encapsulated HTTP status).</summary>
    public static VirusScanResult Parse(string response)
    {
        var lines = response.Split("\r\n");
        if (lines.Length == 0 || !lines[0].StartsWith("ICAP/1.0 ", StringComparison.OrdinalIgnoreCase))
            return VirusScanResult.Unavailable("malformed ICAP response");

        var statusText = lines[0]["ICAP/1.0 ".Length..].Trim();
        if (!int.TryParse(statusText.Split(' ')[0], out var status))
            return VirusScanResult.Unavailable($"malformed ICAP status '{statusText}'");

        if (status == 204)
            return VirusScanResult.Clean;

        if (status != 200)
            return VirusScanResult.Unavailable($"ICAP status {statusText}");

        var headerEnd = Array.IndexOf(lines, string.Empty);
        var headers = lines.Skip(1).Take(headerEnd < 0 ? lines.Length - 1 : headerEnd - 1)
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (headers.TryGetValue("X-Infection-Found", out var infection))
            return VirusScanResult.Infected(ThreatFromInfectionHeader(infection));
        if (headers.TryGetValue("X-Virus-ID", out var virusId))
            return VirusScanResult.Infected(virusId);
        if (headers.TryGetValue("X-Violations-Found", out _))
            return VirusScanResult.Infected(null);

        // Encapsulated HTTP status: a block page (403) means the engine substituted the body.
        var encapsulated = lines.Skip(headerEnd + 1).FirstOrDefault(l => l.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase));
        if (encapsulated is not null && !encapsulated.Contains(" 200 ", StringComparison.Ordinal))
            return VirusScanResult.Infected(null);

        return VirusScanResult.Clean;
    }

    private static string? ThreatFromInfectionHeader(string value)
    {
        // "Type=0; Resolution=2; Threat=Eicar-Test-Signature;"
        var threat = value.Split(';').Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("Threat=", StringComparison.OrdinalIgnoreCase));
        return threat?["Threat=".Length..];
    }

    private static async Task<string> ReadResponseAsync(Stream net, CancellationToken ct)
    {
        // The ICAP status line and headers decide the verdict; for a 200 the first
        // encapsulated HTTP status line is read as well. Servers may keep the
        // connection open despite Connection: close, so never wait for end-of-stream.
        using var ms = new MemoryStream();
        var buf = new byte[4096];
        int n;
        while ((n = await net.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > 64 * 1024) break;

            var text = Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            var end  = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;                                                      // headers incomplete
            if (!text.StartsWith("ICAP/1.0 200", StringComparison.OrdinalIgnoreCase)) break;   // nothing more needed
            if (text.IndexOf("\r\n", end + 4, StringComparison.Ordinal) >= 0) break;  // encapsulated status line arrived
        }
        return Encoding.ASCII.GetString(ms.ToArray());
    }
}
