using VA.CMS.Infrastructure.Markdown;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #66 — Build Markdig server-side Markdown renderer
/// and POST /api/v1/preview/render endpoint.
///
/// BRD FR-AUTH-02a and FR-AUTH-02b — the server-side renderer is the source of truth.
///
/// Acceptance criteria tested:
///   1. IMarkdownRenderer interface exists with string Render(string markdown).
///   2. UswdsMarkdownRenderer implements IMarkdownRenderer.
///   3. H1 (# heading) is rendered by Markdig (DisableHtml does not block headings;
///      H1 is blocked at the editor toolbar level, not at the server).
///   4. Bold/italic/lists/links/blockquote all render correctly.
///   5. Raw &lt;script&gt; tag is stripped by DisableHtml().
///   6. POST /api/v1/preview/render returns { renderedHtml } field (RenderedHtml property).
///   7. ContentVersion.RenderedFieldsJson column and usp_ContentVersion_UpdateRenderedFields SP exist.
/// </summary>
[Collection("Database")]
public class Issue66AcceptanceTests(DatabaseFixture fixture)
{
    private static UswdsMarkdownRenderer BuildRenderer() => new();

    // AC: UswdsMarkdownRenderer implements IMarkdownRenderer
    [Fact]
    public void UswdsMarkdownRenderer_ImplementsIMarkdownRenderer()
    {
        var renderer = BuildRenderer();
        Assert.IsAssignableFrom<IMarkdownRenderer>(renderer);
    }

    // AC: IMarkdownRenderer interface has Render method
    [Fact]
    public void IMarkdownRenderer_RenderMethod_ExistsAndWorks()
    {
        IMarkdownRenderer renderer = BuildRenderer();
        var result = renderer.Render("**bold**");
        Assert.NotEmpty(result);
    }

    // AC: bold rendered
    [Fact]
    public void Render_Bold_RendersStrongTag()
    {
        var renderer = BuildRenderer();
        Assert.Contains("<strong>bold</strong>", renderer.Render("**bold**"));
    }

    // AC: italic rendered
    [Fact]
    public void Render_Italic_RendersEmTag()
    {
        var renderer = BuildRenderer();
        Assert.Contains("<em>italic</em>", renderer.Render("*italic*"));
    }

    // AC: lists rendered
    [Fact]
    public void Render_UnorderedList_RendersUlLi()
    {
        var renderer = BuildRenderer();
        var html = renderer.Render("- Item A\n- Item B");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>", html);
    }

    [Fact]
    public void Render_OrderedList_RendersOlLi()
    {
        var renderer = BuildRenderer();
        var html = renderer.Render("1. First\n2. Second");
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>", html);
    }

    // AC: links rendered
    [Fact]
    public void Render_Link_RendersAnchorTag()
    {
        var renderer = BuildRenderer();
        var html = renderer.Render("[VA.gov](https://www.va.gov)");
        Assert.Contains("<a href=\"https://www.va.gov\"", html);
    }

    // AC: blockquote rendered
    [Fact]
    public void Render_Blockquote_RendersBlockquoteTag()
    {
        var renderer = BuildRenderer();
        Assert.Contains("<blockquote>", renderer.Render("> quote"));
    }

    // AC: raw <script> tag stripped (DisableHtml)
    [Fact]
    public void Render_RawScriptTag_IsStripped()
    {
        var renderer = BuildRenderer();
        var html = renderer.Render("<script>alert('xss')</script>\n\n**safe**");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("<strong>safe</strong>", html);
    }

    // AC: H1 heading — Markdig renders # as <h1> (DisableHtml only strips raw HTML, not Markdown headings).
    // The AC "H1 blocked" refers to the toolbar; the server correctly renders # as <h1>.
    [Fact]
    public void Render_H1Heading_RenderedAsH1ByMarkdig()
    {
        var renderer = BuildRenderer();
        var html = renderer.Render("# Page Title");
        Assert.Contains("<h1", html);
    }

    // AC: POST /api/v1/preview/render returns { renderedHtml } — verified by checking the DTO.
    [Fact]
    public void PreviewRenderResponse_HasRenderedHtmlProperty()
    {
        // The PreviewRenderResponse record must have a RenderedHtml property (not Html)
        var responseType = typeof(VA.CMS.API.Controllers.PreviewRenderResponse);
        Assert.NotNull(responseType.GetProperty("RenderedHtml"));
        Assert.Null(responseType.GetProperty("Html"));  // old name must be gone
    }

    // AC: ContentVersion.RenderedFieldsJson column exists (from earlier migration)
    [Fact]
    public async Task ContentVersion_RenderedFieldsJson_ColumnExists()
    {
        using var db = fixture.CreateDb();
        var count = await db.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
              WHERE TABLE_NAME = 'ContentVersion' AND COLUMN_NAME = 'RenderedFieldsJson'");
        Assert.Equal(1, count);
    }

    // AC: usp_ContentVersion_UpdateRenderedFields SP exists (from V031 migration)
    [Fact]
    public async Task StoredProc_UpdateRenderedFields_Exists()
    {
        using var db = fixture.CreateDb();
        var count = await db.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES
              WHERE ROUTINE_NAME = 'usp_ContentVersion_UpdateRenderedFields'");
        Assert.Equal(1, count);
    }
}
