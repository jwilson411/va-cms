using VA.CMS.Infrastructure.Markdown;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #115 — TipTap RichText editor with tiptap-markdown export.
///
/// AC: "On save, serialized to Markdown via tiptap-markdown; Markdig (#66) renders it
/// correctly on public site."
///
/// The fixture below is the verbatim output of the admin RichTextEditor
/// (src/admin/src/features/contentEntries/RichTextEditor.tsx) for a document that
/// exercises every toolbar control: block image, H2/H3, bold, italic, link with title,
/// tight bullet list ("-" marker), ordered list, block quote, and author-typed raw HTML
/// (which tiptap-markdown entity-escapes because it runs with html: false).
/// These tests pin the contract between that serializer and the Markdig pipeline.
/// </summary>
public class Issue115AcceptanceTests
{
    private const string EditorOutput =
        "![Placeholder hero image](/media/placeholder-hero.jpg)\n" +
        "\n" +
        "## Benefits overview\n" +
        "\n" +
        "Some **bold**, *italic* and a [link](https://www.va.gov/health \"VA Health\").\n" +
        "\n" +
        "- one\n" +
        "- two\n" +
        "\n" +
        "1. first\n" +
        "2. second\n" +
        "\n" +
        "> Quoted text\n" +
        "\n" +
        "### Sub heading\n" +
        "\n" +
        "Text with &lt;b&gt;raw&lt;/b&gt; html & ampersand &lt; less-than.";

    private static string Render(string markdown) => new UswdsMarkdownRenderer().Render(markdown);

    [Fact]
    public void EditorOutput_BlockImage_RendersImgWithAltOnItsOwn()
    {
        var html = Render(EditorOutput);
        Assert.Contains("<img src=\"/media/placeholder-hero.jpg\" alt=\"Placeholder hero image\"", html);
        // The image must not swallow the following heading into its paragraph.
        Assert.Contains("<h2", html);
        Assert.Contains(">Benefits overview</h2>", html);
        Assert.DoesNotContain("## Benefits", html);
    }

    [Fact]
    public void EditorOutput_Headings_RenderH2AndH3()
    {
        var html = Render(EditorOutput);
        Assert.Contains(">Benefits overview</h2>", html);
        Assert.Contains(">Sub heading</h3>", html);
    }

    [Fact]
    public void EditorOutput_InlineMarks_RenderStrongEmAndLinkWithTitle()
    {
        var html = Render(EditorOutput);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<a href=\"https://www.va.gov/health\" title=\"VA Health\">link</a>", html);
    }

    [Fact]
    public void EditorOutput_TightLists_RenderWithoutParagraphsInsideItems()
    {
        var html = Render(EditorOutput);
        Assert.Contains("<ul>\n<li>one</li>\n<li>two</li>\n</ul>", html);
        Assert.Contains("<ol>\n<li>first</li>\n<li>second</li>\n</ol>", html);
    }

    [Fact]
    public void EditorOutput_Blockquote_RendersBlockquote()
    {
        var html = Render(EditorOutput);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("Quoted text", html);
    }

    [Fact]
    public void EditorOutput_EntityEscapedHtml_StaysLiteralText()
    {
        var html = Render(EditorOutput);
        // The author typed <b>raw</b>; the editor escaped it, and Markdig keeps it as text.
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;raw&lt;/b&gt;", html);
        Assert.Contains("&amp; ampersand &lt; less-than", html);
    }

    [Fact]
    public void EditorOutput_BareUrlLink_RendersAutolink()
    {
        // Inserting a link with nothing selected emits a CommonMark autolink.
        var html = Render("<https://www.va.gov/>");
        Assert.Contains("<a href=\"https://www.va.gov/\">https://www.va.gov/</a>", html);
    }

    [Fact]
    public void EditorOutput_ContainsNoRawHtmlPassthrough()
    {
        // Sanity: nothing in the fixture is raw HTML that DisableHtml() would have to strip,
        // so the rendered output is exactly what the author saw in the WYSIWYG editor.
        Assert.DoesNotContain("<img", EditorOutput);
        Assert.DoesNotContain("<a ", EditorOutput);
        Assert.DoesNotContain("<b>", EditorOutput);
    }
}
