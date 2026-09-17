using System.IO.Compression;
using System.Text;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Determines what an upload actually is from its bytes (#158). The client's
/// Content-Type and file extension are claims; this is the evidence. Only the
/// allow-listed families are recognised — anything else is "unknown" and rejected.
/// </summary>
public static class MediaContentSniffer
{
    /// <summary>Bytes needed to classify every supported type (OOXML/zip needs the archive itself; see <see cref="Detect"/>).</summary>
    public const int HeaderLength = 512;

    /// <summary>
    /// MIME types the content could legitimately be. Several entries mean the
    /// bytes are a container shared by more than one type (OLE2, plain text).
    /// Empty means the content matches no supported type.
    /// </summary>
    public static IReadOnlyList<string> Detect(Stream content)
    {
        var header = new byte[HeaderLength];
        var read = ReadFully(content, header);
        if (read == 0)
            return Array.Empty<string>();

        if (Starts(header, read, 0xFF, 0xD8, 0xFF))                                   return ["image/jpeg"];
        if (Starts(header, read, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))     return ["image/png"];
        if (StartsAscii(header, read, "GIF87a") || StartsAscii(header, read, "GIF89a")) return ["image/gif"];
        if (StartsAscii(header, read, "RIFF") && read >= 12 && AsciiAt(header, 8, "WEBP")) return ["image/webp"];
        if (Starts(header, read, 0x49, 0x49, 0x2A, 0x00) || Starts(header, read, 0x4D, 0x4D, 0x00, 0x2A)) return ["image/tiff"];
        if (StartsAscii(header, read, "BM"))                                          return ["image/bmp"];
        if (StartsAscii(header, read, "%PDF-"))                                       return ["application/pdf"];

        // OLE2 compound file: legacy Office formats share one container.
        if (Starts(header, read, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1))
            return ["application/msword", "application/vnd.ms-excel", "application/vnd.ms-powerpoint"];

        // ZIP container: OOXML documents are zips with a tell-tale top-level folder.
        if (Starts(header, read, 0x50, 0x4B, 0x03, 0x04) || Starts(header, read, 0x50, 0x4B, 0x05, 0x06))
        {
            content.Position = 0;
            return DetectZipFamily(content);
        }

        if (LooksLikeSvg(header, read))
            return ["image/svg+xml"];

        if (LooksLikeText(header, read))
            return ["text/plain", "text/csv"];

        return Array.Empty<string>();
    }

    /// <summary>
    /// Extensions that may carry each supported MIME type. The declared type,
    /// the sniffed type and the extension must all agree before a file is stored.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ExtensionsByMime =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"]      = ["jpg", "jpeg", "jpe", "jfif"],
            ["image/png"]       = ["png"],
            ["image/gif"]       = ["gif"],
            ["image/webp"]      = ["webp"],
            ["image/tiff"]      = ["tif", "tiff"],
            ["image/bmp"]       = ["bmp"],
            ["image/svg+xml"]   = ["svg"],
            ["application/pdf"] = ["pdf"],
            ["application/msword"]        = ["doc", "dot"],
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]   = ["docx"],
            ["application/vnd.ms-excel"]  = ["xls", "xlt"],
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]         = ["xlsx"],
            ["application/vnd.ms-powerpoint"] = ["ppt", "pps"],
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ["pptx"],
            ["text/plain"]      = ["txt", "text", "md", "log"],
            ["text/csv"]        = ["csv"],
            ["application/zip"] = ["zip"],
            ["application/x-zip-compressed"] = ["zip"],
        };

    public static bool ExtensionMatches(string mimeType, string extension)
        => ExtensionsByMime.TryGetValue(mimeType, out var exts)
        && exts.Contains(extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> DetectZipFamily(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            if (names.Any(n => n.StartsWith("word/", StringComparison.OrdinalIgnoreCase)))
                return ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"];
            if (names.Any(n => n.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)))
                return ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"];
            if (names.Any(n => n.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase)))
                return ["application/vnd.openxmlformats-officedocument.presentationml.presentation"];
            return ["application/zip", "application/x-zip-compressed"];
        }
        catch (InvalidDataException)
        {
            return Array.Empty<string>();
        }
        finally
        {
            content.Position = 0;
        }
    }

    private static bool LooksLikeSvg(byte[] h, int len)
    {
        var text = DecodeLeading(h, len);
        if (text is null) return false;
        var t = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        // Skip an XML declaration, comments and a DOCTYPE to find the root element.
        var i = 0;
        while (i < t.Length)
        {
            if (t.AsSpan(i).StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))      { i = t.IndexOf("?>", i, StringComparison.Ordinal); if (i < 0) return false; i += 2; }
            else if (t.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))             { i = t.IndexOf("-->", i, StringComparison.Ordinal); if (i < 0) return false; i += 3; }
            else if (t.AsSpan(i).StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) { i = t.IndexOf('>', i); if (i < 0) return false; i += 1; }
            else if (char.IsWhiteSpace(t[i])) i++;
            else break;
        }
        return t.AsSpan(i).StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeText(byte[] h, int len)
    {
        if (DecodeLeading(h, len) is null) return false;
        for (var i = 0; i < len; i++)
        {
            var b = h[i];
            if (b == 0) return false;
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D or 0x0C)) return false;
        }
        return true;
    }

    /// <summary>Decodes the sampled bytes as UTF-8, tolerating a multi-byte sequence cut at the sample edge.</summary>
    private static string? DecodeLeading(byte[] h, int len)
    {
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            // Trim up to 3 trailing bytes so a truncated multi-byte sequence is not counted as invalid.
            for (var trim = 0; trim <= 3 && len - trim > 0; trim++)
            {
                try { return strict.GetString(h, 0, len - trim); }
                catch (DecoderFallbackException) { /* try shorter */ }
            }
            return null;
        }
        catch { return null; }
    }

    private static int ReadFully(Stream s, byte[] buffer)
    {
        var total = 0;
        int n;
        while (total < buffer.Length && (n = s.Read(buffer, total, buffer.Length - total)) > 0)
            total += n;
        return total;
    }

    private static bool Starts(byte[] h, int len, params byte[] magic)
    {
        if (len < magic.Length) return false;
        for (var i = 0; i < magic.Length; i++)
            if (h[i] != magic[i]) return false;
        return true;
    }

    private static bool StartsAscii(byte[] h, int len, string magic)
        => len >= magic.Length && AsciiAt(h, 0, magic);

    private static bool AsciiAt(byte[] h, int offset, string magic)
    {
        for (var i = 0; i < magic.Length; i++)
            if (h[offset + i] != magic[i]) return false;
        return true;
    }
}
