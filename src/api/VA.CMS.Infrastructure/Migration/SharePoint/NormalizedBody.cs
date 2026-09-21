namespace VA.CMS.Infrastructure.Migration.SharePoint;

/// <summary>
/// Output of <see cref="SharePointHtmlNormalizer.Normalize"/> (#192, BRD MIG-02): the page body
/// as CommonMark Markdown that renders through <c>UswdsMarkdownRenderer</c> without raw HTML,
/// plus one warning per element or link the normalizer had to drop or guess at, for the
/// migration report (#196).
/// </summary>
public sealed record NormalizedBody(string Markdown, IReadOnlyList<NormalizerWarning> Warnings);

/// <summary>
/// Something the normalizer could not carry over faithfully. <paramref name="Element"/> is the
/// offending source markup (opening tag and a little content, whitespace-collapsed, truncated)
/// so a content owner can find it on the SharePoint page.
/// </summary>
public sealed record NormalizerWarning(string Code, string Message, string PageId, string Element);

/// <summary>Warning codes emitted by <see cref="SharePointHtmlNormalizer"/>; stable so reports (#196) can key on them.</summary>
public static class NormalizerWarningCodes
{
    /// <summary>An <c>&lt;img&gt;</c> without <c>alt</c>. The image is kept with empty alt; it needs one in the CMS (508).</summary>
    public const string ImageAltMissing        = "image-alt-missing";
    /// <summary>An <c>&lt;img&gt;</c> whose source the document resolver did not know. The image is dropped; its alt text stays as text.</summary>
    public const string ImageUnresolved        = "image-unresolved";
    /// <summary>A link into <c>/_layouts/</c> (SharePoint system pages). The link is dropped; its text stays.</summary>
    public const string LinkLayoutsDropped     = "link-layouts-dropped";
    /// <summary>A <c>javascript:</c> (or other scripting/unsupported scheme) link. The link is removed; its text stays.</summary>
    public const string LinkScriptRemoved      = "link-script-removed";
    /// <summary>A site link neither resolver could map to a CMS page or media item. The link is dropped; its text stays.</summary>
    public const string LinkUnresolved         = "link-unresolved";
    /// <summary>A table used for layout (headings, lists or nested tables in cells, single row/column, spans). Its cells are unwrapped in reading order.</summary>
    public const string LayoutTableUnwrapped   = "layout-table-unwrapped";
    /// <summary>A data table with no header row. The first row became the header because pipe tables require one.</summary>
    public const string TableHeaderInferred    = "table-header-inferred";
    /// <summary>A web part (zone, embed box or placeholder). Its rendered output is not in the export, so nothing can be carried over.</summary>
    public const string WebPartDropped         = "webpart-dropped";
    /// <summary>Any other element that cannot be expressed in Markdown and was removed with its content (iframe, form controls, media, …).</summary>
    public const string ElementDropped         = "element-dropped";
}

/// <summary>
/// Link targets the normalizer cannot decide on its own. The page importer (#193) supplies the
/// page resolver, the document importer (#194) the document resolver; both receive the
/// server-relative SharePoint path without query string or fragment (for example
/// <c>/sites/vba/Pages/About-Us.aspx</c> or <c>/sites/vba/Documents/Checklist.pdf</c>) and return
/// the CMS URL to link to, or <c>null</c> when the target is unknown — the link is then dropped
/// with a <see cref="NormalizerWarningCodes.LinkUnresolved"/> warning.
/// </summary>
public sealed class SharePointLinkResolver
{
    /// <summary>Maps a <c>.aspx</c> page path to a CMS path such as <c>/about-us</c>.</summary>
    public Func<string, string?> ResolvePage { get; init; } = _ => null;

    /// <summary>Maps a document-library file path (any non-page path) to a CMS media URL.</summary>
    public Func<string, string?> ResolveDocument { get; init; } = _ => null;

    /// <summary>
    /// Absolute URL of the exported web, e.g. <c>https://intranet.example.va.gov/sites/vba</c>.
    /// Absolute links on the same host are treated as server-relative so they go through the
    /// resolvers instead of pointing at the retired farm. Any other absolute link is kept verbatim.
    /// </summary>
    public string? SourceWebUrl { get; init; }

    /// <summary>Resolves nothing: every site link is dropped with a warning. Useful for inspection runs.</summary>
    public static SharePointLinkResolver None { get; } = new();
}
