using System.Text.RegularExpressions;
using VA.CMS.Infrastructure.Markdown;
using VA.CMS.Infrastructure.Migration.SharePoint;
using Path = System.IO.Path;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #192 (epic #13, BRD MIG-02): the SharePoint HTML → USWDS-safe
/// Markdown normalizer.
///
///   - Golden files: Fixtures/SharePoint/html/&lt;case&gt;.html normalizes to exactly &lt;case&gt;.md.
///     Set VACMS_UPDATE_GOLDENS=1 to rewrite the .md files from the current output, then review
///     the diff — the .md files are the specification.
///   - Every golden output and every sample-export page body renders through
///     UswdsMarkdownRenderer with no escaped HTML tags and no style/class attributes.
///   - Each dropped element or link is a warning carrying the page id and the source markup.
///
/// No database, no network: the normalizer is a pure function.
/// </summary>
public partial class Issue192AcceptanceTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SharePoint", "html");

    private static readonly string SamplePagesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SharePoint", "sample-export", "pages");

    private const string PageId = "0a1b2c3d-0001-4000-8000-000000000099";
    private const string PageUrl = "/sites/vba-example/Pages/default.aspx";

    private static readonly Dictionary<string, string> KnownPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/sites/vba-example/Pages/default.aspx"] = "/",
        ["/sites/vba-example/Pages/Claims-Process.aspx"] = "/claims-process",
        ["/sites/vba-example/Pages/Contact.aspx"] = "/contact",
        ["/sites/vba-example/Pages/Holiday-Schedule-2027.aspx"] = "/holiday-schedule-2027",
        ["/sites/vba-example/Pages/About.aspx"] = "/about",
    };

    private static readonly HashSet<string> KnownDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        "/sites/vba-example/Documents/Claims-Checklist.pdf",
        "/sites/vba-example/Documents/Claims Checklist (2027).pdf",
        "/sites/vba-example/Documents/Forms/VA-Form-21-526EZ-Instructions.pdf",
        "/sites/vba-example/Documents/Minutes-2022-12-20.docx",
        "/sites/vba-example/SiteAssets/office-front.png",
        "/sites/vba-example/SiteAssets/process-diagram.png",
        "/sites/vba-example/SiteAssets/spacer.gif",
    };

    /// <summary>What the page/document importers (#193/#194) will supply: the sample site's pages and files.</summary>
    private static readonly SharePointLinkResolver SampleSite = new()
    {
        SourceWebUrl = "https://intranet.example.va.gov/sites/vba-example",
        ResolvePage = path => KnownPages.TryGetValue(path, out var slug) ? slug : null,
        ResolveDocument = path => KnownDocuments.Contains(path) ? "/media/" + Path.GetFileName(path).ToLowerInvariant() : null,
    };

    public static IEnumerable<object[]> GoldenCases() =>
        Directory.GetFiles(FixtureDir, "*.html").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) }).OrderBy(c => (string)c[0]);

    // ── golden files ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Golden_Html_Normalizes_To_Expected_Markdown(string caseName)
    {
        var html = File.ReadAllText(Path.Combine(FixtureDir, caseName + ".html"));
        var expectedPath = Path.Combine(FixtureDir, caseName + ".md");

        var result = SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite, PageUrl);

        if (Environment.GetEnvironmentVariable("VACMS_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFixtureDir(), caseName + ".md"), result.Markdown);
            File.WriteAllText(expectedPath, result.Markdown);
        }

        Assert.True(File.Exists(expectedPath), $"missing golden file {caseName}.md (run with VACMS_UPDATE_GOLDENS=1 to create it)");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
        Assert.Equal(expected, result.Markdown);
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Golden_Markdown_Renders_Without_Raw_Html_Or_Styles(string caseName)
    {
        var html = File.ReadAllText(Path.Combine(FixtureDir, caseName + ".html"));
        var result = SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite, PageUrl);

        AssertUswdsSafe(new UswdsMarkdownRenderer().Render(result.Markdown), caseName);
    }

    [Fact]
    public void Normalizer_Is_Deterministic()
    {
        var html = File.ReadAllText(Path.Combine(FixtureDir, "structure.html"));
        var a = SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite, PageUrl);
        var b = SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite, PageUrl);

        Assert.Equal(a.Markdown, b.Markdown);
        Assert.Equal(a.Warnings, b.Warnings);
    }

    // ── sample export package ─────────────────────────────────────────────────

    [Fact]
    public void Every_Sample_Export_Page_Body_Normalizes_And_Renders_Clean()
    {
        var pages = Directory.GetFiles(SamplePagesDir, "*.html");
        Assert.Equal(8, pages.Length);

        foreach (var page in pages)
        {
            var id = Path.GetFileNameWithoutExtension(page);
            var result = SharePointHtmlNormalizer.Normalize(File.ReadAllText(page), id, SampleSite, PageUrl);

            Assert.All(result.Warnings, w => Assert.Equal(id, w.PageId));
            AssertUswdsSafe(new UswdsMarkdownRenderer().Render(result.Markdown), id);
            Assert.DoesNotContain("ms-rte", result.Markdown);
            Assert.DoesNotContain("<%", result.Markdown);
        }

        // The web part page (.aspx source) carries no rich text: nothing but a warning comes out.
        var webPartPage = SharePointHtmlNormalizer.Normalize(
            File.ReadAllText(Path.Combine(SamplePagesDir, "0a1b2c3d-0003-4000-8000-000000000001.html")), "wpp", SampleSite);
        Assert.Equal(string.Empty, webPartPage.Markdown);
        Assert.Equal(NormalizerWarningCodes.WebPartDropped, Assert.Single(webPartPage.Warnings).Code);
    }

    // ── warnings ──────────────────────────────────────────────────────────────

    [Fact]
    public void Links_Produce_One_Warning_Per_Dropped_Target_With_Page_Id_And_Element()
    {
        var html = File.ReadAllText(Path.Combine(FixtureDir, "links.html"));
        var result = SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite, PageUrl);

        Assert.All(result.Warnings, w => Assert.Equal(PageId, w.PageId));
        Assert.Equal(
        [
            NormalizerWarningCodes.LinkLayoutsDropped,   // _layouts/15/help.aspx
            NormalizerWarningCodes.LinkScriptRemoved,    // javascript:window.print()
            NormalizerWarningCodes.LinkScriptRemoved,    // "  JaVaScRiPt:alert(1)"
            NormalizerWarningCodes.LinkScriptRemoved,    // vbscript:
            NormalizerWarningCodes.LinkUnresolved,       // Retired-Page.aspx
            NormalizerWarningCodes.LinkUnresolved,       // Old-Memo.docx
            NormalizerWarningCodes.LinkUnresolved,       // /sites/other/Pages/default.aspx
        ], result.Warnings.Select(w => w.Code));

        var layouts = result.Warnings[0];
        Assert.Contains("_layouts/15/help.aspx", layouts.Element);
        Assert.StartsWith("<a href=", layouts.Element);
        Assert.Contains("SharePoint help page", layouts.Element);
        Assert.Contains("javascript:window.print()", result.Warnings[1].Element);
        Assert.Contains("Retired-Page.aspx", result.Warnings[4].Message);

        // The dropped links keep their text and nothing else.
        Assert.Contains("SharePoint help page.", result.Markdown);
        Assert.Contains("Print this page. Obfuscated. VBScript.", result.Markdown);
        Assert.DoesNotContain("javascript", result.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_layouts", result.Markdown);
    }

    [Fact]
    public void Images_Warn_On_Missing_Alt_And_Unresolved_Sources()
    {
        var html = File.ReadAllText(Path.Combine(FixtureDir, "images.html"));
        var result = SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite, PageUrl);

        Assert.Equal(
        [
            NormalizerWarningCodes.ImageAltMissing,      // process-diagram.png has no alt attribute
            NormalizerWarningCodes.ImageUnresolved,      // not-in-package.jpg
            NormalizerWarningCodes.ImageUnresolved,      // data: image
            NormalizerWarningCodes.ImageUnresolved,      // _layouts/15/images/blank.gif
        ], result.Warnings.Select(w => w.Code));

        Assert.Contains("process-diagram.png", result.Warnings[0].Element);
        Assert.Contains("![](/media/process-diagram.png)", result.Markdown);        // kept, empty alt for the CMS to fill
        Assert.Contains("![](/media/spacer.gif)", result.Markdown);                  // explicit alt="" is decorative: no warning
        Assert.Contains("Building rear entrance", result.Markdown);                  // dropped image leaves its alt text
        Assert.DoesNotContain("not-in-package", result.Markdown);
        Assert.DoesNotContain("data:", result.Markdown);
        Assert.Contains("![Same image, absolute URL](/media/office-front.png)", result.Markdown);
        Assert.Contains("![VA logo](https://www.va.gov/img/design/logo/va-logo.png)", result.Markdown);
    }

    [Fact]
    public void Layout_Tables_And_Web_Parts_And_Dropped_Elements_Warn()
    {
        var layout = SharePointHtmlNormalizer.Normalize(File.ReadAllText(Path.Combine(FixtureDir, "nested-layout-table.html")), PageId, SampleSite);
        Assert.Equal(3, layout.Warnings.Count(w => w.Code == NormalizerWarningCodes.LayoutTableUnwrapped));   // outer, inner, single-cell
        Assert.DoesNotContain("|", layout.Markdown);

        var data = SharePointHtmlNormalizer.Normalize(File.ReadAllText(Path.Combine(FixtureDir, "data-table.html")), PageId, SampleSite);
        var inferred = Assert.Single(data.Warnings);
        Assert.Equal(NormalizerWarningCodes.TableHeaderInferred, inferred.Code);
        Assert.Contains("New Year's Day", inferred.Element);

        var wiki = SharePointHtmlNormalizer.Normalize(File.ReadAllText(Path.Combine(FixtureDir, "wiki-page.html")), PageId, SampleSite, PageUrl);
        Assert.Equal(2, wiki.Warnings.Count(w => w.Code == NormalizerWarningCodes.WebPartDropped));
        Assert.DoesNotContain("Announcements list view", wiki.Markdown);
        Assert.DoesNotContain("g_wsaEnabled", wiki.Markdown);

        var structure = SharePointHtmlNormalizer.Normalize(File.ReadAllText(Path.Combine(FixtureDir, "structure.html")), PageId, SampleSite);
        var dropped = structure.Warnings.Where(w => w.Code == NormalizerWarningCodes.ElementDropped).ToList();
        Assert.Equal(3, dropped.Count);                                              // input, button, iframe
        Assert.Contains(dropped, w => w.Element.StartsWith("<iframe", StringComparison.Ordinal));
        Assert.Contains(dropped, w => w.Element.StartsWith("<input", StringComparison.Ordinal));
        Assert.Contains(dropped, w => w.Element.StartsWith("<button", StringComparison.Ordinal));
        Assert.Equal(dropped.Count, structure.Warnings.Count);                      // nothing else on that page is lossy
    }

    [Fact]
    public void Chrome_Is_Stripped_Silently_And_Empty_Input_Yields_Empty_Output()
    {
        var word = SharePointHtmlNormalizer.Normalize(File.ReadAllText(Path.Combine(FixtureDir, "word-paste.html")), PageId, SampleSite);
        var only = Assert.Single(word.Warnings);                                     // <o:p>, <xml>, <style>, VML, conditional comments: not content
        Assert.Equal(NormalizerWarningCodes.TableHeaderInferred, only.Code);         // Word tables have no <th>; the bold first row is promoted
        Assert.Contains("Anytown", word.Markdown);                                   // <st1:place> smart tags unwrap, not drop
        Assert.DoesNotContain("mso-", word.Markdown);
        Assert.DoesNotContain("·", word.Markdown);
        Assert.Contains("- Quarterly town halls\n  - January and April at the office", word.Markdown);
        Assert.Contains("1. Email the Public Contact Team\n2. Confirm the venue two weeks ahead", word.Markdown);
        Assert.Contains("| **Quarter** | **Venue** |", word.Markdown);

        Assert.Equal(string.Empty, SharePointHtmlNormalizer.Normalize(null, PageId, SharePointLinkResolver.None).Markdown);
        Assert.Equal(string.Empty, SharePointHtmlNormalizer.Normalize("  <div>&nbsp;</div><p></p> ", PageId, SharePointLinkResolver.None).Markdown);
        Assert.Empty(SharePointHtmlNormalizer.Normalize("<o:p></o:p><style>p{}</style>", PageId, SharePointLinkResolver.None).Warnings);
    }

    [Fact]
    public void Resolver_None_Drops_Every_Site_Link_But_Keeps_External_Ones()
    {
        var html = "<p><a href=\"/sites/x/Pages/a.aspx\">a</a> <a href=\"/sites/x/Documents/b.pdf\">b</a> <a href=\"https://www.va.gov/\">va.gov</a></p>";
        var result = SharePointHtmlNormalizer.Normalize(html, PageId, SharePointLinkResolver.None);

        Assert.Equal("a b [va.gov](https://www.va.gov/)\n", result.Markdown);
        Assert.Equal(2, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.Equal(NormalizerWarningCodes.LinkUnresolved, w.Code));
    }

    [Fact]
    public void Prose_That_Looks_Like_Markdown_Is_Escaped_So_It_Renders_As_Typed()
    {
        var html = File.ReadAllText(Path.Combine(FixtureDir, "structure.html"));
        var rendered = new UswdsMarkdownRenderer().Render(SharePointHtmlNormalizer.Normalize(html, PageId, SampleSite).Markdown);

        Assert.Contains("5 * 3 = 15, snake_case_name, [brackets], x &lt; y &gt; z, ~tilde~, back\\slash, a^b, AT&amp;T, &amp;copy; literal.", rendered);
        Assert.Contains("# not a heading<br />\n1. not a list<br />\n- not a bullet<br />\n+ nor this<br />\n&gt; not a quote<br />\n| not a table<br />\n---", rendered);
        Assert.Contains("<h2 id=\"trailing-hash\">Trailing hash #</h2>", rendered);
        Assert.Contains("<code>inline `code`</code>", rendered);
        Assert.Contains("<ol start=\"3\">", rendered);
        Assert.Contains("<pre><code class=\"language-csharp\">var x = &quot;```&quot;;\nConsole.WriteLine(x);   // keep   spacing\n</code></pre>", rendered);
        Assert.Contains("<del>struck</del>", rendered);
        Assert.Contains("<h2 id=\"page-title-becomes-h2\">Page title becomes h2</h2>", rendered);
        Assert.DoesNotContain("<h1", rendered);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>The acceptance bar: no escaped HTML tags (a raw-HTML leak) and no presentational attributes in the rendered output.</summary>
    private static void AssertUswdsSafe(string rendered, string caseName)
    {
        Assert.False(EscapedTag().IsMatch(rendered), $"{caseName}: rendered output contains an escaped HTML tag:\n{rendered}");
        Assert.DoesNotContain("style=", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"ms-", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<font", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<span", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>&amp;lt;</c> followed by a tag name or <c>/</c>: raw HTML that Markdig escaped to text.</summary>
    [GeneratedRegex(@"&lt;/?[a-zA-Z]")]
    private static partial Regex EscapedTag();

    /// <summary>The fixture folder in the source tree (for VACMS_UPDATE_GOLDENS), found by walking up from the test binaries.</summary>
    private static string SourceFixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VA.CMS.Tests.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("VA.CMS.Tests.csproj not found above " + AppContext.BaseDirectory),
            "Fixtures", "SharePoint", "html");
    }
}
