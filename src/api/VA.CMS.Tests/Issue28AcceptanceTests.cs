using VA.CMS.Infrastructure.ContentTypes;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #28:
/// Build `vacms content-type scaffold &lt;Name&gt;` CLI command (FR-DEV-06).
///
/// AC:
/// - Running the command creates a correctly structured C# type definition file.
/// - Scaffolded file compiles without modification.
/// - CLI shows usage help with `vacms content-type --help`.
/// </summary>
public class Issue28AcceptanceTests : IDisposable
{
    private readonly string _tempDir;

    public Issue28AcceptanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vacms-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── ScaffoldService.Scaffold() — file creation ────────────────────────────

    [Fact]
    public void Scaffold_Creates_File_In_ContentTypes_Subdirectory()
    {
        var path = ScaffoldService.Scaffold("NewsArticle", _tempDir);

        Assert.True(File.Exists(path), $"Expected file at {path}");
        Assert.Contains("ContentTypes", path);
        Assert.EndsWith("NewsArticleTypeDefinition.cs", path);
    }

    [Fact]
    public void Scaffold_Creates_ContentTypes_Directory_When_Missing()
    {
        var subDir = Path.Combine(_tempDir, "new-project");
        // Do not pre-create subDir/ContentTypes — Scaffold should create it.

        var path = ScaffoldService.Scaffold("EventListing", subDir);

        Assert.True(File.Exists(path));
        Assert.True(Directory.Exists(Path.Combine(subDir, "ContentTypes")));
    }

    // ── Generated file structure ──────────────────────────────────────────────

    [Fact]
    public void Scaffold_File_Contains_Correct_ClassName()
    {
        var path = ScaffoldService.Scaffold("BenefitsPage", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("public sealed class BenefitsPageTypeDefinition", content);
    }

    [Fact]
    public void Scaffold_File_Derives_From_ContentTypeDefinitionBase()
    {
        var path = ScaffoldService.Scaffold("StaffDirectory", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains(": ContentTypeDefinitionBase", content);
    }

    [Fact]
    public void Scaffold_File_Contains_Snake_Case_Name()
    {
        var path = ScaffoldService.Scaffold("NewsArticle", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("\"news_article\"", content);
    }

    [Fact]
    public void Scaffold_File_Contains_Display_Name_With_Spaces()
    {
        var path = ScaffoldService.Scaffold("NewsArticle", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("\"News Article\"", content);
    }

    [Fact]
    public void Scaffold_File_Contains_Template_Id()
    {
        var path = ScaffoldService.Scaffold("NewsArticle", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("\"NewsArticleTemplate\"", content);
    }

    [Fact]
    public void Scaffold_File_Overrides_All_Required_Abstract_Properties()
    {
        var path = ScaffoldService.Scaffold("PressRelease", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("override string  Name", content);
        Assert.Contains("override string  DisplayName", content);
        Assert.Contains("override string? TemplateId", content);
        Assert.Contains("override IReadOnlyList<FieldDefinition> Fields", content);
    }

    [Fact]
    public void Scaffold_File_Contains_Starter_Fields()
    {
        var path = ScaffoldService.Scaffold("LandingPage", _tempDir);
        var content = File.ReadAllText(path);

        // Starter kit: title (ShortText), summary (LongText), body (RichText)
        Assert.Contains("FieldType.ShortText", content);
        Assert.Contains("FieldType.LongText",  content);
        Assert.Contains("FieldType.RichText",  content);
    }

    [Fact]
    public void Scaffold_File_Has_Correct_Namespace()
    {
        var path = ScaffoldService.Scaffold("ServiceDetail", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("namespace VA.CMS.ContentTypes;", content);
    }

    [Fact]
    public void Scaffold_File_Imports_Infrastructure_ContentTypes()
    {
        var path = ScaffoldService.Scaffold("Alert", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("using VA.CMS.Infrastructure.ContentTypes;", content);
    }

    // ── Single-word name (no underscores expected) ─────────────────────────────

    [Fact]
    public void Scaffold_Single_Word_Name_No_Underscore_In_Snake_Case()
    {
        var path = ScaffoldService.Scaffold("Page", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("\"page\"", content);
        Assert.DoesNotContain("_page", content);
    }

    // ── Overwrite: calling scaffold twice for same name overwrites the file ────

    [Fact]
    public void Scaffold_Same_Name_Twice_Overwrites_File()
    {
        ScaffoldService.Scaffold("Widget", _tempDir);
        var path = ScaffoldService.Scaffold("Widget", _tempDir);

        Assert.True(File.Exists(path));
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Scaffold_Blank_Name_Throws_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ScaffoldService.Scaffold("", _tempDir));
        Assert.Throws<ArgumentException>(() => ScaffoldService.Scaffold("   ", _tempDir));
    }

    [Fact]
    public void Scaffold_Null_Name_Throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ScaffoldService.Scaffold(null!, _tempDir));
    }

    [Fact]
    public void Scaffold_Name_Starting_With_Digit_Throws()
    {
        Assert.Throws<ArgumentException>(() => ScaffoldService.Scaffold("123News", _tempDir));
    }

    [Fact]
    public void Scaffold_Name_With_Spaces_Throws()
    {
        Assert.Throws<ArgumentException>(() => ScaffoldService.Scaffold("News Article", _tempDir));
    }

    [Fact]
    public void Scaffold_Name_With_Hyphens_Throws()
    {
        Assert.Throws<ArgumentException>(() => ScaffoldService.Scaffold("news-article", _tempDir));
    }

    // ── IsValidIdentifier unit tests ──────────────────────────────────────────

    [Theory]
    [InlineData("NewsArticle",    true)]
    [InlineData("Page",           true)]
    [InlineData("StandardPage",   true)]
    [InlineData("ABC",            true)]
    [InlineData("A1B2",           true)]
    [InlineData("",               false)]
    [InlineData("1Name",          false)]
    [InlineData("news-article",   false)]
    [InlineData("news article",   false)]
    [InlineData("news_article",   false)]
    public void IsValidIdentifier_Returns_Expected(string input, bool expected)
    {
        Assert.Equal(expected, ScaffoldService.IsValidIdentifier(input));
    }

    // ── ToSnakeCase unit tests ────────────────────────────────────────────────

    [Theory]
    [InlineData("NewsArticle",     "news_article")]
    [InlineData("Page",            "page")]
    [InlineData("StandardPage",    "standard_page")]
    [InlineData("BenefitsPage",    "benefits_page")]
    [InlineData("EventListing",    "event_listing")]
    [InlineData("ABC",             "a_b_c")]
    public void ToSnakeCase_Returns_Expected(string input, string expected)
    {
        Assert.Equal(expected, ScaffoldService.ToSnakeCase(input));
    }

    // ── ToDisplayName unit tests ──────────────────────────────────────────────

    [Theory]
    [InlineData("NewsArticle",   "News Article")]
    [InlineData("Page",          "Page")]
    [InlineData("StandardPage",  "Standard Page")]
    [InlineData("BenefitsPage",  "Benefits Page")]
    public void ToDisplayName_Returns_Expected(string input, string expected)
    {
        Assert.Equal(expected, ScaffoldService.ToDisplayName(input));
    }

    // ── GenerateSource produces compilable C# structure ───────────────────────

    [Fact]
    public void GenerateSource_Contains_All_Required_Structural_Elements()
    {
        var src = ScaffoldService.GenerateSource(
            "NewsArticleTypeDefinition",
            "news_article",
            "News Article",
            "NewsArticleTemplate");

        // using directive
        Assert.Contains("using VA.CMS.Infrastructure.ContentTypes;", src);
        // namespace
        Assert.Contains("namespace VA.CMS.ContentTypes;", src);
        // class declaration
        Assert.Contains("public sealed class NewsArticleTypeDefinition : ContentTypeDefinitionBase", src);
        // all four required overrides
        Assert.Contains("override string  Name", src);
        Assert.Contains("override string  DisplayName", src);
        Assert.Contains("override string? TemplateId", src);
        Assert.Contains("override IReadOnlyList<FieldDefinition> Fields", src);
        // field types
        Assert.Contains("FieldType.ShortText", src);
        Assert.Contains("FieldType.LongText",  src);
        Assert.Contains("FieldType.RichText",  src);
        // values
        Assert.Contains("\"news_article\"", src);
        Assert.Contains("\"News Article\"", src);
        Assert.Contains("\"NewsArticleTemplate\"", src);
    }

    // ── Scaffolded file is logically correct against the domain model ─────────

    [Fact]
    public void Scaffold_Produced_File_References_Known_FieldTypes()
    {
        // All FieldType enum values used in the scaffold are defined in the enum.
        var knownTypes = Enum.GetNames<FieldType>();

        var path = ScaffoldService.Scaffold("ContentCheck", _tempDir);
        var content = File.ReadAllText(path);

        // Parse out FieldType.XXX references
        var matches = System.Text.RegularExpressions.Regex.Matches(
            content, @"FieldType\.(\w+)");

        Assert.NotEmpty(matches);
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var typeName = m.Groups[1].Value;
            Assert.Contains(typeName, knownTypes);
        }
    }
}
