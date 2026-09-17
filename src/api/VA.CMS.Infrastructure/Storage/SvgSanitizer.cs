using System.Xml;
using System.Xml.Linq;

namespace VA.CMS.Infrastructure.Storage;

/// <summary>
/// Strips active content from an SVG document before it is stored (#158):
/// script and foreignObject elements, event-handler attributes, external
/// references (href/xlink:href to anything but a fragment or a data: image),
/// and style content that pulls in external resources. The result is parsed
/// with DTD processing disabled, so entity expansion attacks are rejected too.
/// </summary>
public static class SvgSanitizer
{
    private static readonly HashSet<string> ForbiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "embed", "object", "audio", "video", "set", "animate", "animateTransform", "animateMotion",
    };

    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    /// <summary>Returns the sanitized SVG bytes, or an error message when the input is not a well-formed SVG.</summary>
    public static (byte[]? Sanitized, string? Error) Sanitize(Stream input)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing    = DtdProcessing.Prohibit,
                XmlResolver      = null,
                IgnoreProcessingInstructions = true,
                MaxCharactersFromEntities = 0,
            };
            using var reader = XmlReader.Create(input, settings);
            doc = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            return (null, $"SVG could not be parsed: {ex.Message}");
        }

        if (doc.Root is null || !string.Equals(doc.Root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            return (null, "File is not an SVG document.");

        foreach (var el in doc.Descendants().ToList())
        {
            if (ForbiddenElements.Contains(el.Name.LocalName))
            {
                el.Remove();
                continue;
            }

            foreach (var attr in el.Attributes().ToList())
            {
                var local = attr.Name.LocalName;
                var value = attr.Value.Trim();

                if (local.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    attr.Remove();
                else if ((local.Equals("href", StringComparison.OrdinalIgnoreCase) || attr.Name.Namespace == XlinkNs)
                         && !IsSafeReference(value))
                    attr.Remove();
                else if (local.Equals("style", StringComparison.OrdinalIgnoreCase) && ContainsExternalUrl(value))
                    attr.Remove();
            }

            if (el.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && ContainsExternalUrl(el.Value))
                el.Remove();
        }

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { OmitXmlDeclaration = false, Indent = false, Encoding = new System.Text.UTF8Encoding(false) }))
            doc.Save(writer);
        return (ms.ToArray(), null);
    }

    /// <summary>Fragments (#id) and embedded raster images are the only references kept.</summary>
    private static bool IsSafeReference(string value)
        => value.StartsWith('#')
        || value.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("data:image/jpeg", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("data:image/gif", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("data:image/webp", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsExternalUrl(string css)
        => css.Contains("url(", StringComparison.OrdinalIgnoreCase) && !css.Contains("url(#", StringComparison.OrdinalIgnoreCase)
        || css.Contains("@import", StringComparison.OrdinalIgnoreCase)
        || css.Contains("expression(", StringComparison.OrdinalIgnoreCase);
}
