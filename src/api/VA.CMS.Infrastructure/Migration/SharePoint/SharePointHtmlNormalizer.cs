using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace VA.CMS.Infrastructure.Migration.SharePoint;

/// <summary>
/// Issue #192 (epic #13, BRD MIG-02): SharePoint page HTML → USWDS-safe Markdown.
///
/// SharePoint 2016 page bodies are full of <c>&lt;span style&gt;</c>, <c>&lt;font&gt;</c>,
/// <c>ms-rte*</c> classes, layout tables, web-part chrome, <c>_layouts</c> links and Word residue.
/// The CMS stores rich text as Markdown and renders it through Markdig with <c>DisableHtml()</c>,
/// so raw HTML would be escaped to visible text. This walks the parsed DOM once and writes
/// CommonMark that the renderer turns into plain semantic HTML: headings, paragraphs, lists,
/// emphasis, links, images, pipe tables, block quotes, code. Everything else is stripped,
/// unwrapped or dropped — and every drop that loses something a reader could see produces a
/// <see cref="NormalizerWarning"/> for the migration report (#196).
///
/// Rules, in the order a content owner would ask about them:
///
///   - <c>h1</c> becomes <c>h2</c> (the page title is the <c>h1</c>); <c>h2</c>–<c>h6</c> keep their level.
///   - Inline <c>style</c>, <c>class</c>, <c>id</c> and every other attribute are ignored; <c>span</c>,
///     <c>font</c>, <c>u</c>, <c>div</c> and unknown elements are unwrapped; <c>&amp;nbsp;</c> runs collapse.
///   - Tables whose cells hold only inline content (2+ rows and columns, no spans) become GFM pipe
///     tables; the first row is the header (inferred with a warning when it is not <c>th</c>).
///     Anything else is a layout table: its cells are emitted in reading order with a warning.
///   - Links: <c>/…/Pages/x.aspx</c> → the page resolver; other site paths → the document resolver;
///     <c>_layouts</c> and <c>javascript:</c> links are removed (text kept, warning); unresolved
///     targets likewise; external, <c>mailto:</c>, <c>tel:</c> and <c>#anchor</c> links are kept.
///   - Images keep their <c>alt</c>; a missing <c>alt</c> attribute is a warning (an explicit empty
///     <c>alt=""</c> is respected as decorative).
///   - Word paste: <c>MsoListParagraph</c> runs become real lists (bullet glyphs and <c>mso-list</c>
///     levels drive the nesting); <c>&lt;o:p&gt;</c>, <c>&lt;xml&gt;</c>, VML and conditional comments vanish.
///   - Web parts (zones, embed boxes, placeholders) are dropped with a warning: their output is not in the export.
///
/// Pure function of its inputs: no I/O, no network (the parser is used without a loader), no
/// clock, warnings in document order — so golden-file tests are stable.
/// </summary>
public static partial class SharePointHtmlNormalizer
{
    private static readonly HtmlParser Parser = new();

    /// <param name="html">Raw body HTML as exported (<c>pages/&lt;id&gt;.html</c>). Fragments and full documents both work.</param>
    /// <param name="pageId">Manifest page id; stamped on every warning.</param>
    /// <param name="links">Resolvers for page and document links. <see cref="SharePointLinkResolver.None"/> drops all site links.</param>
    /// <param name="pageUrl">Server-relative URL of the page, used to resolve relative hrefs such as <c>../Documents/x.pdf</c>.</param>
    public static NormalizedBody Normalize(string? html, string pageId, SharePointLinkResolver links, string? pageUrl = null)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(links);

        var source = ServerDirective().Replace(html ?? string.Empty, string.Empty);
        var document = Parser.ParseDocument(source);
        var walker = new Walker(pageId, links, pageUrl);
        var blocks = walker.RenderBlocks(document.Body ?? (INode)document);
        var markdown = string.Join("\n\n", blocks.Select(b => b.Text));
        return new NormalizedBody(markdown.Length == 0 ? string.Empty : markdown + "\n", walker.Warnings);
    }

    // ── DOM walker ────────────────────────────────────────────────────────────

    private enum BlockKind { Paragraph, Heading, List, Quote, Code, Table, Rule }

    private readonly record struct Block(string Text, BlockKind Kind);

    /// <summary>Marks a hard line break (<c>&lt;br&gt;</c>) inside accumulated inline text until the block is finalised.</summary>
    private const char HardBreak = '\u0001';

    private sealed class Walker(string pageId, SharePointLinkResolver links, string? pageUrl)
    {
        private readonly List<NormalizerWarning> _warnings = [];
        public IReadOnlyList<NormalizerWarning> Warnings => _warnings;

        private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
        {
            "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "table", "blockquote", "pre", "hr",
            "dl", "dt", "dd", "section", "article", "main", "aside", "header", "footer", "nav", "center", "figure",
            "figcaption", "fieldset", "form", "address", "details", "summary", "tr", "td", "th", "thead", "tbody",
            "tfoot", "caption", "html", "body", "asp:content",
        };

        /// <summary>Removed together with their content, silently: not something a reader saw.</summary>
        private static readonly HashSet<string> ChromeTags = new(StringComparer.Ordinal)
        {
            "script", "style", "noscript", "template", "head", "meta", "link", "title", "base", "xml", "o:p",
            "v:shape", "v:shapetype", "v:imagedata", "w:wrap", "col", "colgroup", "area", "map",
        };

        /// <summary>Removed together with their content, with a warning: a reader saw something here.</summary>
        private static readonly HashSet<string> DroppedContentTags = new(StringComparer.Ordinal)
        {
            "iframe", "object", "embed", "applet", "video", "audio", "source", "track", "canvas", "svg", "math",
            "input", "button", "select", "textarea", "option", "datalist", "progress", "meter", "dialog", "marquee",
        };

        private static readonly string[] WebPartClasses =
        [
            "ms-webpart-zone", "ms-webpartzone", "ms-webpart-chrome", "ms-rte-wpbox", "ms-rte-embedcode",
            "ms-rte-embedwp", "ms-rtestate-notify",
        ];

        // ── blocks ──

        public List<Block> RenderBlocks(INode container)
        {
            var blocks = new List<Block>();
            var inline = new StringBuilder();
            var children = container.ChildNodes.ToList();

            for (var i = 0; i < children.Count; i++)
            {
                var node = children[i];
                if (node is IText text) { inline.Append(EscapeText(CollapseWhitespace(text.Data))); continue; }
                if (node is not IElement el) continue;                // comments, processing instructions
                var tag = el.LocalName;

                if (IsWebPart(el)) { Warn(NormalizerWarningCodes.WebPartDropped, "web part dropped; its rendered output is not in the export", el); continue; }
                if (ChromeTags.Contains(tag)) continue;
                if (DroppedContentTags.Contains(tag)) { Warn(NormalizerWarningCodes.ElementDropped, $"<{tag}> cannot be expressed in Markdown and was dropped", el); continue; }

                if (tag == "p" && IsWordListParagraph(el))
                {
                    FlushParagraph(inline, blocks);
                    var run = new List<IElement>();
                    while (i < children.Count)
                    {
                        if (children[i] is IElement p && p.LocalName == "p" && IsWordListParagraph(p)) run.Add(p);
                        else if (children[i] is IText t && string.IsNullOrWhiteSpace(t.Data)) { }
                        else break;
                        i++;
                    }
                    i--;
                    var list = RenderWordList(run);
                    if (list.Length > 0) blocks.Add(new Block(list, BlockKind.List));
                    continue;
                }

                if (!BlockTags.Contains(tag) && !ContainsBlock(el))
                {
                    inline.Append(RenderInline(el, InlineContext.Default));
                    continue;
                }

                FlushParagraph(inline, blocks);
                switch (tag)
                {
                    case "p" or "dd" or "address" or "figcaption" or "summary":
                        blocks.AddRange(RenderBlocks(el));           // a p never nests blocks after parsing; dd/address may
                        break;
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        AddHeading(el, blocks);
                        break;
                    case "ul" or "ol":
                        var list = RenderList(el, ordered: tag == "ol");
                        if (list.Length > 0) blocks.Add(new Block(list, BlockKind.List));
                        break;
                    case "blockquote":
                        AddQuote(el, blocks);
                        break;
                    case "pre":
                        AddCode(el, blocks);
                        break;
                    case "hr":
                        blocks.Add(new Block("---", BlockKind.Rule));
                        break;
                    case "table":
                        AddTable(el, blocks);
                        break;
                    case "dt":
                        var term = FinishInlineLine(RenderInlineChildren(el, InlineContext.Default));
                        if (term.Length > 0) blocks.Add(new Block($"**{term}**", BlockKind.Paragraph));
                        break;
                    default:
                        blocks.AddRange(RenderBlocks(el));           // div, section, li outside a list, td outside a table, …
                        break;
                }
            }

            FlushParagraph(inline, blocks);
            return blocks;
        }

        private void FlushParagraph(StringBuilder inline, List<Block> blocks)
        {
            if (inline.Length == 0) return;
            var text = FinishInlineBlock(inline.ToString());
            inline.Clear();
            if (text.Length > 0) blocks.Add(new Block(text, BlockKind.Paragraph));
        }

        private void AddHeading(IElement el, List<Block> blocks)
        {
            var level = el.LocalName[1] - '0';
            if (level == 1) level = 2;                               // the page title is the h1
            var text = FinishInlineLine(RenderInlineChildren(el, InlineContext.Default));
            if (text.Length == 0) return;
            if (text.EndsWith('#')) text = text[..^1] + "\\#";       // would otherwise read as a closing sequence
            blocks.Add(new Block($"{new string('#', level)} {text}", BlockKind.Heading));
        }

        private void AddQuote(IElement el, List<Block> blocks)
        {
            var inner = string.Join("\n\n", RenderBlocks(el).Select(b => b.Text));
            if (inner.Length == 0) return;
            var quoted = string.Join("\n", inner.Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l));
            blocks.Add(new Block(quoted, BlockKind.Quote));
        }

        private static void AddCode(IElement el, List<Block> blocks)
        {
            var code = el.TextContent.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ').TrimEnd('\n');
            if (code.Trim().Length == 0) return;
            var longest = LongestRun(code, '`');
            var fence = new string('`', Math.Max(3, longest + 1));
            var language = el.QuerySelector("code")?.ClassList.FirstOrDefault(c => c.StartsWith("language-", StringComparison.OrdinalIgnoreCase));
            var info = language is null ? string.Empty : language["language-".Length..];
            blocks.Add(new Block($"{fence}{info}\n{code}\n{fence}", BlockKind.Code));
        }

        // ── lists ──

        private string RenderList(IElement list, bool ordered)
        {
            var lines = new List<string>();
            var number = ordered && int.TryParse(list.GetAttribute("start"), out var start) && start >= 0 ? start : 1;
            var lastIndent = 0;

            foreach (var child in list.Children)
            {
                switch (child.LocalName)
                {
                    case "li":
                        var marker = ordered ? $"{number++}. " : "- ";
                        var body = JoinItemBlocks(RenderBlocks(child));
                        if (body.Length == 0) { number = ordered ? number - 1 : number; continue; }
                        lastIndent = marker.Length;
                        lines.Add(IndentItem(body, marker));
                        break;
                    case "ul" or "ol":                               // SharePoint nests lists directly under <ul>; hang it off the previous item
                        var nested = RenderList(child, child.LocalName == "ol");
                        if (nested.Length > 0) lines.Add(IndentAll(nested, lastIndent));
                        break;
                }
            }
            return string.Join("\n", lines);
        }

        private static string JoinItemBlocks(List<Block> blocks)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < blocks.Count; i++)
            {
                if (i > 0) sb.Append(blocks[i].Kind == BlockKind.List ? "\n" : "\n\n");
                sb.Append(blocks[i].Text);
            }
            return sb.ToString();
        }

        private static string IndentItem(string body, string marker)
        {
            var lines = body.Split('\n');
            var pad = new string(' ', marker.Length);
            var sb = new StringBuilder(marker).Append(lines[0]);
            for (var i = 1; i < lines.Length; i++)
                sb.Append('\n').Append(lines[i].Length == 0 ? string.Empty : pad + lines[i]);
            return sb.ToString();
        }

        private static string IndentAll(string text, int width)
        {
            if (width == 0) return text;
            var pad = new string(' ', width);
            return string.Join("\n", text.Split('\n').Select(l => l.Length == 0 ? l : pad + l));
        }

        // Word paste: consecutive <p class="MsoListParagraph…" style="…mso-list:l0 level2 lfo1"> with the bullet
        // glyph in a <span style="mso-list:Ignore"> child. Rebuilt as a real (nested) list.

        private static bool IsWordListParagraph(IElement p) =>
            p.ClassList.Any(c => c.StartsWith("MsoListParagraph", StringComparison.OrdinalIgnoreCase))
            || (p.GetAttribute("style") ?? string.Empty).Contains("mso-list:", StringComparison.OrdinalIgnoreCase);

        private readonly record struct WordItem(int Level, bool Ordered, string Body);

        private string RenderWordList(List<IElement> paragraphs)
        {
            var items = new List<WordItem>();
            foreach (var p in paragraphs)
            {
                var style = p.GetAttribute("style") ?? string.Empty;
                var level = WordListLevel().Match(style) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 1;
                var glyphSpan = p.Descendants<IElement>().FirstOrDefault(e =>
                    (e.GetAttribute("style") ?? string.Empty).Contains("mso-list:Ignore", StringComparison.OrdinalIgnoreCase));
                var glyph = glyphSpan?.TextContent.Trim() ?? string.Empty;
                glyphSpan?.Remove();
                var ordered = OrderedGlyph().IsMatch(glyph);
                var body = JoinItemBlocks(RenderBlocks(p));
                if (body.Length > 0) items.Add(new WordItem(Math.Max(1, level), ordered, body));
            }
            var index = 0;
            return items.Count == 0 ? string.Empty : RenderWordItems(items, ref index, items[0].Level);
        }

        private static string RenderWordItems(List<WordItem> items, ref int index, int level)
        {
            var lines = new List<string>();
            var number = 1;
            var lastIndent = 0;
            while (index < items.Count && items[index].Level >= level)
            {
                if (items[index].Level > level)
                {
                    var nested = RenderWordItems(items, ref index, items[index].Level);
                    lines.Add(IndentAll(nested, lastIndent));
                    continue;
                }
                var item = items[index++];
                var marker = item.Ordered ? $"{number++}. " : "- ";
                lastIndent = marker.Length;
                lines.Add(IndentItem(item.Body, marker));
            }
            return string.Join("\n", lines);
        }

        // ── tables ──

        private void AddTable(IElement table, List<Block> blocks)
        {
            var caption = table.Children.FirstOrDefault(c => c.LocalName == "caption");
            if (caption is not null)
            {
                var text = FinishInlineBlock(RenderInlineChildren(caption, InlineContext.Default));
                if (text.Length > 0) blocks.Add(new Block(text, BlockKind.Paragraph));
            }

            var rows = TableRows(table);
            if (rows.Count == 0) return;

            if (!IsDataTable(rows))
            {
                var unwrapped = rows.SelectMany(Cells).SelectMany(RenderBlocks).ToList();
                if (unwrapped.Count > 0)                             // an empty table lost nothing: no warning
                    Warn(NormalizerWarningCodes.LayoutTableUnwrapped, "layout table unwrapped; its cells were emitted in reading order", table);
                blocks.AddRange(unwrapped);
                return;
            }

            var headerIsExplicit = rows[0].ParentElement?.LocalName == "thead" || Cells(rows[0]).All(c => c.LocalName == "th");
            if (!headerIsExplicit)
                Warn(NormalizerWarningCodes.TableHeaderInferred, "table has no header row; the first row was used as the header", table);

            var grid = rows.Select(r => Cells(r).Select(RenderCell).ToList()).ToList();
            var width = grid.Max(r => r.Count);
            var sb = new StringBuilder();
            for (var r = 0; r < grid.Count; r++)
            {
                var cells = grid[r];
                while (cells.Count < width) cells.Add(string.Empty);
                sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |");
                if (r == 0) sb.Append('\n').Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", width))).Append(" |");
                if (r < grid.Count - 1) sb.Append('\n');
            }
            blocks.Add(new Block(sb.ToString(), BlockKind.Table));
        }

        /// <summary>Rows of this table only (not of nested tables), whether or not wrapped in thead/tbody/tfoot.</summary>
        private static List<IElement> TableRows(IElement table)
        {
            var rows = new List<IElement>();
            foreach (var child in table.Children)
            {
                if (child.LocalName == "tr") rows.Add(child);
                else if (child.LocalName is "thead" or "tbody" or "tfoot")
                    rows.AddRange(child.Children.Where(c => c.LocalName == "tr"));
            }
            return rows;
        }

        private static List<IElement> Cells(IElement row) => row.Children.Where(c => c.LocalName is "td" or "th").ToList();

        private static readonly HashSet<string> NotAllowedInDataCell = new(StringComparer.Ordinal)
        {
            "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "table", "blockquote", "pre", "hr", "dl", "form",
        };

        /// <summary>
        /// A data table is one a pipe table can express: at least two rows and two columns, no
        /// rowspan/colspan, and no cell holding anything but inline content (paragraphs are allowed —
        /// Word wraps every cell in one). Everything else is layout.
        /// </summary>
        private static bool IsDataTable(List<IElement> rows)
        {
            if (rows.Count < 2) return false;
            var columns = rows.Select(r => Cells(r).Count).ToList();
            if (columns.Max() < 2 || columns.Any(c => c == 0)) return false;
            foreach (var cell in rows.SelectMany(Cells))
            {
                if (Span(cell, "colspan") > 1 || Span(cell, "rowspan") > 1) return false;
                if (cell.Descendants<IElement>().Any(d => NotAllowedInDataCell.Contains(d.LocalName))) return false;
            }
            return true;

            static int Span(IElement cell, string attribute) =>
                int.TryParse(cell.GetAttribute(attribute), out var n) ? n : 1;
        }

        private string RenderCell(IElement cell)
        {
            var text = FinishInlineLine(RenderInlineChildren(cell, InlineContext.Default with { InTableCell = true }));
            return text.Replace("|", "\\|");
        }

        // ── inline ──

        private readonly record struct InlineContext(bool InStrong, bool InEm, bool InTableCell)
        {
            public static InlineContext Default => new(false, false, false);
        }

        private string RenderInlineChildren(INode parent, InlineContext ctx)
        {
            var sb = new StringBuilder();
            foreach (var child in parent.ChildNodes)
            {
                switch (child)
                {
                    case IText text:
                        sb.Append(EscapeText(CollapseWhitespace(text.Data)));
                        break;
                    case IElement el:
                        sb.Append(RenderInline(el, ctx));
                        break;
                }
            }
            return sb.ToString();
        }

        private string RenderInline(IElement el, InlineContext ctx)
        {
            var tag = el.LocalName;

            if (IsWebPart(el)) { Warn(NormalizerWarningCodes.WebPartDropped, "web part dropped; its rendered output is not in the export", el); return string.Empty; }
            if (ChromeTags.Contains(tag)) return string.Empty;
            if (DroppedContentTags.Contains(tag)) { Warn(NormalizerWarningCodes.ElementDropped, $"<{tag}> cannot be expressed in Markdown and was dropped", el); return string.Empty; }

            switch (tag)
            {
                case "br":
                    return ctx.InTableCell ? " " : HardBreak.ToString();
                case "img":
                    return RenderImage(el);
                case "a":
                    return RenderLink(el, ctx);
                case "strong" or "b":
                    return ctx.InStrong ? RenderInlineChildren(el, ctx) : Wrap(RenderInlineChildren(el, ctx with { InStrong = true }), "**");
                case "em" or "i" or "cite" or "dfn" or "var":
                    return ctx.InEm ? RenderInlineChildren(el, ctx) : Wrap(RenderInlineChildren(el, ctx with { InEm = true }), "*");
                case "s" or "strike" or "del":
                    return Wrap(RenderInlineChildren(el, ctx), "~~");
                case "code" or "kbd" or "samp" or "tt":
                    return CodeSpan(CollapseWhitespace(el.TextContent));
                case "pre":
                    return CodeSpan(CollapseWhitespace(el.TextContent));   // a pre inside inline content (e.g. in a table cell)
                case "li":
                    return " " + RenderInlineChildren(el, ctx) + " ";
                default:
                    // span, font, u, sup, sub, mark, small, q, abbr, label, td/p inside a cell, unknown (st1:*, asp:*) …: unwrap.
                    var inner = RenderInlineChildren(el, ctx);
                    return BlockTags.Contains(tag) ? " " + inner + " " : inner;
            }
        }

        private static string Wrap(string inner, string delimiter)
        {
            // Emphasis delimiters must hug non-space text: "<b> bold </b>" → " **bold** ".
            var trimmed = inner.Trim(' ', HardBreak);
            if (trimmed.Length == 0) return inner;
            var leading = inner.Length - inner.TrimStart(' ', HardBreak).Length;
            var trailing = inner.Length - inner.TrimEnd(' ', HardBreak).Length;
            return inner[..leading] + delimiter + trimmed + delimiter + inner[(inner.Length - trailing)..];
        }

        private static string CodeSpan(string text)
        {
            var content = text.Trim();
            if (content.Length == 0) return string.Empty;
            var fence = new string('`', LongestRun(content, '`') + 1);
            var pad = content.StartsWith('`') || content.EndsWith('`') ? " " : string.Empty;
            return $"{fence}{pad}{content}{pad}{fence}";
        }

        private string RenderImage(IElement img)
        {
            var src = img.GetAttribute("src") ?? string.Empty;
            var alt = img.GetAttribute("alt");
            if (alt is null)
                Warn(NormalizerWarningCodes.ImageAltMissing, $"image '{Shorten(src)}' has no alt text; it needs one in the CMS before publishing", img);
            var altText = EscapeText(CollapseWhitespace(alt ?? string.Empty)).Trim();

            var target = ResolveTarget(src, img, isImage: true);
            if (target is null) return altText;                       // dropped (warned in ResolveTarget); keep what a screen reader had
            return $"![{altText}]({EscapeUrl(target)})";
        }

        private string RenderLink(IElement a, InlineContext ctx)
        {
            var inner = RenderInlineChildren(a, ctx);
            var href = a.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)) return inner;         // <a name="…"> anchor targets

            var target = ResolveTarget(href, a, isImage: false);
            if (target is null) return inner;
            var trimmed = inner.Trim(' ', HardBreak);
            if (trimmed.Length == 0) return inner;
            var leading = inner.Length - inner.TrimStart(' ', HardBreak).Length;
            var trailing = inner.Length - inner.TrimEnd(' ', HardBreak).Length;
            return $"{inner[..leading]}[{trimmed}]({EscapeUrl(target)}){inner[(inner.Length - trailing)..]}";
        }

        // ── link targets ──

        /// <summary>
        /// Decides what a href/src becomes. Returns the URL to emit, or null when the link/image is
        /// dropped (a warning has then been recorded, except for an empty href).
        /// </summary>
        private string? ResolveTarget(string raw, IElement el, bool isImage)
        {
            var href = ControlChars().Replace(raw, string.Empty).Trim();
            if (href.Length == 0) return null;
            if (href.StartsWith('#')) return href;

            var schemeMatch = UrlScheme().Match(Whitespace().Replace(href, string.Empty));
            if (schemeMatch.Success)
            {
                var scheme = schemeMatch.Groups[1].Value.ToLowerInvariant();
                switch (scheme)
                {
                    case "http" or "https":
                        if (!TryMakeServerRelative(href, out href)) return href;   // external: kept verbatim
                        break;
                    case "mailto" or "tel" when !isImage:
                        return href;
                    default:
                        if (isImage)
                            Warn(NormalizerWarningCodes.ImageUnresolved, $"image with '{scheme}:' source dropped; only site and http(s) images are carried over", el);
                        else
                            Warn(NormalizerWarningCodes.LinkScriptRemoved, $"'{scheme}:' link removed; its text was kept", el);
                        return null;
                }
            }

            if (!href.StartsWith('/')) href = ResolveRelative(href);

            var fragmentAt = href.IndexOf('#');
            var fragment = fragmentAt >= 0 ? href[fragmentAt..] : string.Empty;
            var path = fragmentAt >= 0 ? href[..fragmentAt] : href;
            var queryAt = path.IndexOf('?');
            if (queryAt >= 0) path = path[..queryAt];

            if (path.Contains("/_layouts/", StringComparison.OrdinalIgnoreCase))
            {
                Warn(isImage ? NormalizerWarningCodes.ImageUnresolved : NormalizerWarningCodes.LinkLayoutsDropped,
                    $"{(isImage ? "image" : "link")} to SharePoint system path '{Shorten(path)}' dropped" + (isImage ? string.Empty : "; its text was kept"), el);
                return null;
            }

            if (!isImage && path.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            {
                var page = links.ResolvePage(path);
                if (page is null)
                {
                    Warn(NormalizerWarningCodes.LinkUnresolved, $"link to page '{Shorten(path)}' could not be mapped to a CMS page; its text was kept", el);
                    return null;
                }
                return page + fragment;
            }

            var media = links.ResolveDocument(path);
            if (media is null)
            {
                if (isImage)
                    Warn(NormalizerWarningCodes.ImageUnresolved, $"image '{Shorten(path)}' could not be mapped to a media item and was dropped", el);
                else
                    Warn(NormalizerWarningCodes.LinkUnresolved, $"link to '{Shorten(path)}' could not be mapped to a media item; its text was kept", el);
                return null;
            }
            return media;
        }

        private bool TryMakeServerRelative(string absolute, out string serverRelative)
        {
            serverRelative = absolute;
            if (links.SourceWebUrl is null) return false;
            if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) || !Uri.TryCreate(links.SourceWebUrl, UriKind.Absolute, out var web)) return false;
            if (!string.Equals(uri.Host, web.Host, StringComparison.OrdinalIgnoreCase)) return false;
            serverRelative = uri.PathAndQuery + uri.Fragment;
            return true;
        }

        private string ResolveRelative(string href)
        {
            if (pageUrl is null || !pageUrl.StartsWith('/')) return "/" + href;
            var baseUri = new Uri("https://sharepoint.invalid" + pageUrl, UriKind.Absolute);
            return Uri.TryCreate(baseUri, href, out var resolved) ? resolved.PathAndQuery + resolved.Fragment : "/" + href;
        }

        // ── helpers ──

        private static bool IsWebPart(IElement el) =>
            el.LocalName.StartsWith("webpartpages:", StringComparison.Ordinal)
            || el.ClassList.Any(c => WebPartClasses.Contains(c, StringComparer.OrdinalIgnoreCase));

        private static bool ContainsBlock(IElement el) => el.Descendants<IElement>().Any(d => BlockTags.Contains(d.LocalName));

        private void Warn(string code, string message, IElement el) =>
            _warnings.Add(new NormalizerWarning(code, message, pageId, Describe(el)));

        private static string Describe(IElement el) => Shorten(Whitespace().Replace(el.OuterHtml, " ").Trim(), 160);

        private static string Shorten(string s, int max = 80) => s.Length <= max ? s : s[..(max - 1)] + "…";

        private static int LongestRun(string s, char c)
        {
            int longest = 0, run = 0;
            foreach (var ch in s)
            {
                run = ch == c ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
            return longest;
        }
    }

    // ── text ──

    /// <summary>HTML whitespace (plus nbsp and zero-width residue) collapses to one space, as a browser renders it.</summary>
    private static string CollapseWhitespace(string s) =>
        Whitespace().Replace(ZeroWidth().Replace(s, string.Empty), " ");

    /// <summary>Backslash-escapes every character that could start Markdown syntax inside prose.</summary>
    private static string EscapeText(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '~' or '^':
                    sb.Append('\\').Append(c);
                    break;
                case '&' when EntityLike().Match(s, i).Success:
                    sb.Append("\\&");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Finalises accumulated inline text as a paragraph: trims, collapses, turns hard breaks into backslash breaks, escapes line starts.</summary>
    private static string FinishInlineBlock(string inline)
    {
        var lines = inline.Split(HardBreak)
            .Select(l => MultiSpace().Replace(l, " ").Trim())
            .Where(l => l.Length > 0)
            .Select(EscapeLineStart)
            .ToList();
        return string.Join("\\\n", lines);
    }

    /// <summary>Finalises inline text that must stay on one line (headings, table cells).</summary>
    private static string FinishInlineLine(string inline) =>
        EscapeLineStart(MultiSpace().Replace(inline.Replace(HardBreak, ' '), " ").Trim());

    /// <summary>A line of prose must not be mistaken for a heading, quote, list item, rule or setext underline.</summary>
    private static string EscapeLineStart(string line)
    {
        var ordered = OrderedMarker().Match(line);
        if (ordered.Success) return line[..ordered.Length] + "\\" + line[ordered.Length..];   // "1. x" → "1\. x"
        if (LineMarker().IsMatch(line) || RuleOrUnderline().IsMatch(line)) return "\\" + line;
        return line;
    }

    private static string EscapeUrl(string url) =>
        url.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29").Replace("<", "%3C").Replace(">", "%3E");

    [GeneratedRegex(@"<%.*?%>", RegexOptions.Singleline)]
    private static partial Regex ServerDirective();

    [GeneratedRegex(@"[\s   ]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[​﻿]")]
    private static partial Regex ZeroWidth();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlChars();

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):")]
    private static partial Regex UrlScheme();

    [GeneratedRegex(@"\G&(?:#\d+|#[xX][0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);")]
    private static partial Regex EntityLike();

    /// <summary>Heading, quote, bullet or pipe-table marker at the start of a line.</summary>
    [GeneratedRegex(@"^[#>+\-|](?:\s|$)")]
    private static partial Regex LineMarker();

    /// <summary>The digits of an ordered-list marker at the start of a line ("1." / "1)"); the escape goes before the delimiter.</summary>
    [GeneratedRegex(@"^\d{1,9}(?=[.)](?:\s|$))")]
    private static partial Regex OrderedMarker();

    /// <summary>A line of only dashes/equals: a thematic break or, after a paragraph line, a setext heading.</summary>
    [GeneratedRegex(@"^[-=]+\s*$")]
    private static partial Regex RuleOrUnderline();

    [GeneratedRegex(@"level(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WordListLevel();

    /// <summary>Word list glyphs that denote an ordered list: "1.", "a)", "iv." …</summary>
    [GeneratedRegex(@"^(?:\d+|[a-zA-Z]|[ivxlcIVXLC]+)[.)]$")]
    private static partial Regex OrderedGlyph();
}
