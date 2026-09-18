using System.Text.Json;
using System.Text.Json.Nodes;
using VA.CMS.Infrastructure.Migration.SharePoint;
using Path = System.IO.Path;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #191 (epic #13, BRD MIG-01): the SharePoint export
/// package reader and the <c>vacms migrate sharepoint --dry-run</c> inventory.
///
///   - The sample package under Fixtures/SharePoint/sample-export parses, is
///     importable, and every manifest field lands on the model.
///   - Each validation rule rejects a mutated copy of the sample with the documented
///     problem code and the offending entity id.
///   - No manifest path can reach outside the package root.
///   - The inventory text is deterministic and states the go/no-go.
///
/// No database: the reader is pure file I/O.
/// </summary>
public class Issue191AcceptanceTests : IDisposable
{
    private static readonly string SamplePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SharePoint", "sample-export");

    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    // ── sample package ────────────────────────────────────────────────────────

    [Fact]
    public void Sample_Package_Is_Importable_And_Fully_Mapped()
    {
        var result = SharePointExportReader.Read(SamplePath);

        Assert.True(result.IsImportable, string.Join("\n", result.Errors.Select(e => e.Message)));
        var pkg = Assert.IsType<SharePointExportPackage>(result.Package);

        Assert.Equal(SharePointExportPackage.FormatV1, pkg.Format);
        Assert.Equal("Export-VacmsSharePoint.ps1/1.0", pkg.ExporterVersion);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 13, 5, 0, TimeSpan.Zero), pkg.ExportedAt);
        Assert.Equal("https://intranet.example.va.gov/sites/vba-example", pkg.SourceWeb.Url);
        Assert.Equal("VBA Example Regional Office", pkg.SourceWeb.Title);

        Assert.Equal(8, pkg.Pages.Count);
        Assert.Equal(5, pkg.Documents.Count);
        Assert.Equal(4, pkg.Users.Count);

        var home = pkg.Pages.Single(p => p.Url.EndsWith("/Pages/default.aspx", StringComparison.Ordinal));
        Assert.Equal("0a1b2c3d-0001-4000-8000-000000000001", home.Id);
        Assert.Equal("Welcome to the VBA Example Regional Office", home.Title);
        Assert.Equal(SharePointPageLayout.Publishing, home.Layout);
        Assert.Equal(SharePointPageStatus.Published, home.Status);
        Assert.Equal("Home page for the regional office intranet.", home.Description);
        Assert.Equal(@"i:0#.w|VA\jsmith", home.Author);
        Assert.Equal(@"i:0#.w|VA\mjones", home.Editor);
        Assert.Equal(new DateTimeOffset(2019, 3, 4, 14, 22, 10, TimeSpan.Zero), home.Created);
        Assert.True(File.Exists(pkg.ResolvePath(home.ContentFile)));
        Assert.Contains("ms-rtestate-field", File.ReadAllText(pkg.ResolvePath(home.ContentFile)));

        Assert.Equal(5, pkg.Pages.Count(p => p.Layout == SharePointPageLayout.Publishing));
        Assert.Equal(2, pkg.Pages.Count(p => p.Layout == SharePointPageLayout.Wiki));
        Assert.Equal(1, pkg.Pages.Count(p => p.Layout == SharePointPageLayout.WebPartPage));
        Assert.Equal(6, pkg.Pages.Count(p => p.Status == SharePointPageStatus.Published));
        Assert.Equal(1, pkg.Pages.Count(p => p.Status == SharePointPageStatus.Draft));
        Assert.Equal(1, pkg.Pages.Count(p => p.Status == SharePointPageStatus.CheckedOut));

        var checklist = pkg.Documents.Single(d => d.Url.EndsWith("Claims-Checklist.pdf", StringComparison.Ordinal));
        Assert.Equal("Documents", checklist.Library);
        Assert.Equal("application/pdf", checklist.ContentType);
        Assert.Equal("Disability Claims Checklist", checklist.Title);
        Assert.Equal(new FileInfo(pkg.ResolvePath(checklist.File)).Length, checklist.SizeBytes);
        Assert.Equal(2, pkg.Documents.Count(d => d.Library == "Site Assets"));
        Assert.Equal("Front entrance of the regional office building",
            pkg.Documents.Single(d => d.File.EndsWith("office-front.png", StringComparison.Ordinal)).AltText);

        var rosa = pkg.Users.Single(u => u.Login == @"i:0#.w|VA\rgarcia");
        Assert.Null(rosa.Upn);
        Assert.Equal("rosa.garcia@va.gov", rosa.PrincipalName);   // email stands in for the UPN
        Assert.Equal("jane.smith@va.gov", pkg.Users.Single(u => u.Login == @"i:0#.w|VA\jsmith").PrincipalName);
        Assert.Null(pkg.Users.Single(u => u.Login == @"i:0#.w|VA\legacyuser").PrincipalName);

        // The only expected finding on the sample: legacyuser has no UPN/email (#195 handles it).
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(PackageProblemCodes.UserPrincipalMissing, warning.Code);
        Assert.Equal(@"i:0#.w|VA\legacyuser", warning.EntityId);
    }

    // ── rejections ────────────────────────────────────────────────────────────

    [Fact]
    public void Missing_Directory_And_Missing_Manifest_Are_Errors()
    {
        var missing = SharePointExportReader.Read(Path.Combine(Path.GetTempPath(), "vacms-" + Guid.NewGuid()));
        Assert.False(missing.IsImportable);
        Assert.Null(missing.Package);
        Assert.Equal(PackageProblemCodes.PackageMissing, Assert.Single(missing.Errors).Code);

        var empty = NewTempDir();
        var noManifest = SharePointExportReader.Read(empty);
        Assert.False(noManifest.IsImportable);
        Assert.Equal(PackageProblemCodes.ManifestMissing, Assert.Single(noManifest.Errors).Code);
    }

    [Fact]
    public void Malformed_Manifest_Is_An_Error_Not_An_Exception()
    {
        var dir = CopySample();
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "{ \"format\": \"vacms-sharepoint-export/1\", \"pages\": [ ");

        var result = SharePointExportReader.Read(dir);

        Assert.False(result.IsImportable);
        Assert.Null(result.Package);
        Assert.Equal(PackageProblemCodes.ManifestInvalid, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vacms-sharepoint-export/2")]
    [InlineData("something-else")]
    public void Unknown_Or_Missing_Format_Is_Rejected(string? format)
    {
        var result = ReadMutated(m =>
        {
            if (format is null) m.Remove("format"); else m["format"] = format;
        });

        Assert.False(result.IsImportable);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PackageProblemCodes.FormatUnsupported, error.Code);
        Assert.Contains(SharePointExportPackage.FormatV1, error.Message);
    }

    [Fact]
    public void Missing_Source_Web_Is_An_Error()
    {
        var result = ReadMutated(m => m.Remove("sourceWeb"));
        Assert.False(result.IsImportable);
        Assert.Contains(result.Errors, e => e.Code == PackageProblemCodes.SourceWebMissing);
    }

    [Fact]
    public void Duplicate_Page_Id_Is_Rejected_With_The_Id()
    {
        var result = ReadMutated(m => Pages(m)[1]!["id"] = Pages(m)[0]!["id"]!.GetValue<string>());

        Assert.False(result.IsImportable);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PackageProblemCodes.PageIdDuplicate, error.Code);
        Assert.Equal("0a1b2c3d-0001-4000-8000-000000000001", error.EntityId);
        Assert.Equal(7, result.Package!.Pages.Count);   // the duplicate is dropped, the rest survive
    }

    [Fact]
    public void Page_Without_Id_Or_Url_Is_Rejected()
    {
        var noId  = ReadMutated(m => Pages(m)[0]!.AsObject().Remove("id"));
        Assert.Equal(PackageProblemCodes.PageIdMissing, Assert.Single(noId.Errors).Code);

        var noUrl = ReadMutated(m => Pages(m)[0]!.AsObject().Remove("url"));
        var error = Assert.Single(noUrl.Errors);
        Assert.Equal(PackageProblemCodes.PageUrlMissing, error.Code);
        Assert.Equal("0a1b2c3d-0001-4000-8000-000000000001", error.EntityId);
    }

    [Fact]
    public void Page_Body_File_That_Does_Not_Exist_Is_Rejected()
    {
        var result = ReadMutated(m => Pages(m)[2]!["contentFile"] = "pages/does-not-exist.html");

        Assert.False(result.IsImportable);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PackageProblemCodes.PageContentMissing, error.Code);
        Assert.Equal("0a1b2c3d-0001-4000-8000-000000000003", error.EntityId);
        Assert.Contains("pages/does-not-exist.html", error.Message);
    }

    [Theory]
    [InlineData("../manifest.json")]
    [InlineData("pages/../../outside.html")]
    [InlineData("..\\..\\outside.html")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("\\\\server\\share\\x.html")]
    public void Page_Path_Escaping_The_Package_Is_Rejected(string contentFile)
    {
        var result = ReadMutated(m => Pages(m)[0]!["contentFile"] = contentFile);

        Assert.False(result.IsImportable);
        var error = Assert.Single(result.Errors);
        Assert.Equal(PackageProblemCodes.PathEscapesPackage, error.Code);
        Assert.Equal("0a1b2c3d-0001-4000-8000-000000000001", error.EntityId);
    }

    [Fact]
    public void Traversal_Is_Rejected_Even_When_The_Target_Exists()
    {
        // A sibling file outside the package that a hostile manifest points at.
        var dir     = CopySample();
        var sibling = Path.Combine(Path.GetDirectoryName(dir)!, Path.GetFileName(dir) + "-outside.html");
        File.WriteAllText(sibling, "<p>outside</p>");
        try
        {
            var result = ReadMutated(dir, m => Pages(m)[0]!["contentFile"] = "../" + Path.GetFileName(sibling));
            Assert.Equal(PackageProblemCodes.PathEscapesPackage, Assert.Single(result.Errors).Code);
        }
        finally { File.Delete(sibling); }
    }

    [Fact]
    public void Document_Path_Escaping_The_Package_Is_Rejected()
    {
        var result = ReadMutated(m => Documents(m)[0]!["file"] = "../../secrets.pdf");
        var error  = Assert.Single(result.Errors);
        Assert.Equal(PackageProblemCodes.PathEscapesPackage, error.Code);
        Assert.Equal("5d6e7f80-0001-4000-8000-000000000001", error.EntityId);
    }

    [Fact]
    public void Document_File_Missing_And_Duplicate_Id_Are_Rejected()
    {
        var missing = ReadMutated(m => Documents(m)[0]!["file"] = "documents/Documents/gone.pdf");
        var error   = Assert.Single(missing.Errors);
        Assert.Equal(PackageProblemCodes.DocumentFileMissing, error.Code);
        Assert.Equal("5d6e7f80-0001-4000-8000-000000000001", error.EntityId);

        var dup = ReadMutated(m => Documents(m)[1]!["id"] = Documents(m)[0]!["id"]!.GetValue<string>());
        Assert.Equal(PackageProblemCodes.DocumentIdDuplicate, Assert.Single(dup.Errors).Code);
        Assert.Equal(4, dup.Package!.Documents.Count);
    }

    [Fact]
    public void Duplicate_User_Login_Is_Rejected()
    {
        var result = ReadMutated(m => Users(m)[1]!["login"] = Users(m)[0]!["login"]!.GetValue<string>());
        var error  = Assert.Single(result.Errors);
        Assert.Equal(PackageProblemCodes.UserLoginDuplicate, error.Code);
        Assert.Equal(@"i:0#.w|VA\jsmith", error.EntityId);
    }

    // ── warnings (do not block) ───────────────────────────────────────────────

    [Fact]
    public void Soft_Problems_Are_Warnings_And_Keep_The_Package_Importable()
    {
        var result = ReadMutated(m =>
        {
            Pages(m)[0]!.AsObject().Remove("title");
            Pages(m)[1]!["layout"] = "mystery";
            Pages(m)[2]!["status"] = "weird";
            Pages(m)[3]!["author"] = @"i:0#.w|VA\nobody";
            Documents(m)[0]!["sizeBytes"] = 1;
        });

        Assert.True(result.IsImportable);
        var codes = result.Warnings.Select(w => w.Code).ToList();
        Assert.Contains(PackageProblemCodes.PageTitleMissing,     codes);
        Assert.Contains(PackageProblemCodes.PageLayoutUnknown,    codes);
        Assert.Contains(PackageProblemCodes.PageStatusUnknown,    codes);
        Assert.Contains(PackageProblemCodes.PageAuthorUnknown,    codes);
        Assert.Contains(PackageProblemCodes.DocumentSizeMismatch, codes);
        Assert.Contains(PackageProblemCodes.UserPrincipalMissing, codes);

        var pkg = result.Package!;
        Assert.Equal("default", pkg.Pages[0].Title);                              // falls back to the file name
        Assert.Equal(SharePointPageLayout.WebPartPage, pkg.Pages[1].Layout);       // unknown layout → best-effort
        Assert.Equal(SharePointPageStatus.Draft, pkg.Pages[2].Status);             // unknown status → draft
    }

    [Fact]
    public void Layout_And_Status_Are_Case_Insensitive()
    {
        var result = ReadMutated(m =>
        {
            Pages(m)[0]!["layout"] = "Publishing";
            Pages(m)[0]!["status"] = "PUBLISHED";
        });
        Assert.DoesNotContain(result.Warnings, w => w.Code is PackageProblemCodes.PageLayoutUnknown or PackageProblemCodes.PageStatusUnknown);
        Assert.Equal(SharePointPageLayout.Publishing, result.Package!.Pages[0].Layout);
        Assert.Equal(SharePointPageStatus.Published, result.Package!.Pages[0].Status);
    }

    // ── inventory text ────────────────────────────────────────────────────────

    [Fact]
    public void Inventory_Summarises_The_Sample_Package()
    {
        var text = MigrationInventory.Render(SharePointExportReader.Read(SamplePath));

        Assert.Contains("SharePoint export package: " + Path.GetFullPath(SamplePath), text);
        Assert.Contains("Format:      vacms-sharepoint-export/1   exported 2026-09-18 13:05 UTC   exporter Export-VacmsSharePoint.ps1/1.0", text);
        Assert.Contains("Source web:  https://intranet.example.va.gov/sites/vba-example   \"VBA Example Regional Office\"", text);
        Assert.Contains("Pages: 8", text);
        Assert.Contains("by layout   publishing 5, wiki 2, webpartpage 1", text);
        Assert.Contains("by status   published 6, checkedout 1, draft 1", text);
        Assert.Contains("Documents: 5 (1 KB)", text);
        Assert.Contains("by extension  .pdf 2 (701 B), .png 2 (140 B), .csv 1 (194 B)", text);
        Assert.Contains("by library    Documents 3, Site Assets 2", text);
        Assert.Contains("Users: 4   with UPN/email 3, without 1", text);
        Assert.Contains("Problems: 0 error(s), 1 warning(s)", text);
        Assert.Contains("WARN   user-principal-missing  user 'i:0#.w|VA\\legacyuser' has no UPN or email", text);
        Assert.EndsWith("Result: package is importable." + Environment.NewLine, text);
    }

    [Fact]
    public void Inventory_States_Not_Importable_And_Lists_Errors_First()
    {
        var result = ReadMutated(m =>
        {
            Pages(m)[2]!["contentFile"] = "pages/nope.html";
            Pages(m)[0]!.AsObject().Remove("title");
        });
        var text = MigrationInventory.Render(result);

        Assert.Contains("Problems: 1 error(s), 2 warning(s)", text);
        var errorLine   = text.Split('\n').Single(l => l.Contains("ERROR", StringComparison.Ordinal));
        var warningLine = text.Split('\n').First(l => l.Contains("WARN", StringComparison.Ordinal));
        Assert.Contains("page-content-missing", errorLine);
        Assert.True(text.IndexOf(errorLine, StringComparison.Ordinal) < text.IndexOf(warningLine, StringComparison.Ordinal));
        Assert.EndsWith("Result: package is NOT importable — fix the errors above and re-run." + Environment.NewLine, text);
    }

    [Fact]
    public void Inventory_For_An_Unreadable_Package_Has_No_Counts()
    {
        var text = MigrationInventory.Render(SharePointExportReader.Read(NewTempDir()));
        Assert.DoesNotContain("Pages:", text);
        Assert.Contains("ERROR  manifest-missing", text);
        Assert.Contains("Result: package is NOT importable", text);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(4_404_019, "4.2 MB")]
    [InlineData(2_500_000_000, "2.33 GB")]
    public void Byte_Formatting_Is_Human_And_Invariant(long n, string expected) =>
        Assert.Equal(expected, MigrationInventory.Bytes(n));

    // ── helpers ───────────────────────────────────────────────────────────────

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vacms-191-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private string CopySample()
    {
        var dir = NewTempDir();
        foreach (var file in Directory.EnumerateFiles(SamplePath, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir, Path.GetRelativePath(SamplePath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return dir;
    }

    private SharePointExportReadResult ReadMutated(Action<JsonObject> mutate) => ReadMutated(CopySample(), mutate);

    private static SharePointExportReadResult ReadMutated(string dir, Action<JsonObject> mutate)
    {
        var manifestPath = Path.Combine(dir, "manifest.json");
        var manifest     = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        mutate(manifest);
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return SharePointExportReader.Read(dir);
    }

    private static JsonArray Pages(JsonObject m)     => m["pages"]!.AsArray();
    private static JsonArray Documents(JsonObject m) => m["documents"]!.AsArray();
    private static JsonArray Users(JsonObject m)     => m["users"]!.AsArray();
}
