using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// ClamAV clamd adapter using the INSTREAM command (#159): the file is streamed
/// in length-prefixed chunks over TCP; clamd answers "stream: OK" or
/// "stream: &lt;signature&gt; FOUND". Used for local development and CI
/// (docker compose --profile clamav).
/// </summary>
public sealed class ClamAvVirusScanService(MediaScannerOptions options) : IVirusScanService
{
    private const int ChunkSize = 64 * 1024;

    public async Task<bool> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        var result = await ScanDetailedAsync(stream, ct);
        return result.Verdict switch
        {
            VirusScanVerdict.Clean    => true,
            VirusScanVerdict.Infected => false,
            _ => throw new IOException(result.Detail ?? "ClamAV unavailable"),
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

            await net.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), token);

            var buffer = new byte[ChunkSize];
            var prefix = new byte[4];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(prefix, read);
                await net.WriteAsync(prefix, token);
                await net.WriteAsync(buffer.AsMemory(0, read), token);
            }
            BinaryPrimitives.WriteInt32BigEndian(prefix, 0);
            await net.WriteAsync(prefix, token);
            await net.FlushAsync(token);

            var reply = await ReadReplyAsync(net, token);
            return Parse(reply);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return VirusScanResult.Unavailable($"clamd {options.Host}:{options.EffectivePort}: {ex.Message}");
        }
    }

    /// <summary>"stream: OK" → Clean; "stream: Foo FOUND" → Infected(Foo); anything else → Unavailable.</summary>
    public static VirusScanResult Parse(string reply)
    {
        var text = reply.Trim('\0', '\r', '\n', ' ');
        if (text.EndsWith(" OK", StringComparison.Ordinal))
            return VirusScanResult.Clean;
        if (text.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var afterColon = text.IndexOf(':') is var i && i >= 0 ? text[(i + 1)..].Trim() : text;
            return VirusScanResult.Infected(afterColon[..^" FOUND".Length].Trim());
        }
        return VirusScanResult.Unavailable($"unexpected clamd reply '{text}'");
    }

    private static async Task<string> ReadReplyAsync(Stream net, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[256];
        int n;
        while ((n = await net.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (buf[..n].Contains((byte)0) || buf[..n].Contains((byte)'\n')) break;
        }
        return Encoding.ASCII.GetString(ms.ToArray());
    }
}
