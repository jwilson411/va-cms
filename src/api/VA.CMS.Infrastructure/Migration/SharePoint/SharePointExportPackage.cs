namespace VA.CMS.Infrastructure.Migration.SharePoint;

/// <summary>
/// A SharePoint 2016 export package on disk (#191, BRD MIG-01) as written by
/// <c>infra/sharepoint/Export-VacmsSharePoint.ps1</c> and documented in
/// <c>docs/MIGRATION.md</c>:
///
/// <code>
/// &lt;package&gt;/
///   manifest.json                 format, source web, pages[], documents[], users[]
///   pages/&lt;page id&gt;.html         raw body HTML of each page
///   documents/&lt;library&gt;/&lt;path&gt;   document-library files, as exported
/// </code>
///
/// Every path in the manifest is relative to the package root and must resolve
/// inside it; <see cref="SharePointExportReader"/> enforces that.
/// </summary>
public sealed class SharePointExportPackage
{
    /// <summary>The only manifest format this build reads.</summary>
    public const string FormatV1 = "vacms-sharepoint-export/1";

    /// <summary>Absolute path of the package directory.</summary>
    public required string RootPath { get; init; }

    public required string Format { get; init; }
    public DateTimeOffset? ExportedAt { get; init; }
    public string? ExporterVersion { get; init; }
    public required SharePointSourceWeb SourceWeb { get; init; }
    public required IReadOnlyList<SharePointPage> Pages { get; init; }
    public required IReadOnlyList<SharePointDocument> Documents { get; init; }
    public required IReadOnlyList<SharePointUser> Users { get; init; }

    /// <summary>Absolute path of a file referenced by the manifest (already validated by the reader).</summary>
    public string ResolvePath(string relativePath) => Path.GetFullPath(Path.Combine(RootPath, relativePath));
}

/// <summary>The SharePoint web (site) the package was exported from.</summary>
public sealed record SharePointSourceWeb(string Url, string? Title, string? Id);

/// <summary>Page layouts the exporter emits. Anything else is reported as a warning and treated as <see cref="WebPartPage"/>.</summary>
public static class SharePointPageLayout
{
    /// <summary>Publishing page (the <c>Pages</c> library): body from <c>PublishingPageContent</c>.</summary>
    public const string Publishing = "publishing";
    /// <summary>Wiki page (<c>Site Pages</c>): body from <c>WikiField</c>.</summary>
    public const string Wiki = "wiki";
    /// <summary>Web part page: no rich-text field; the exporter stores the raw <c>.aspx</c> source. Best effort.</summary>
    public const string WebPartPage = "webpartpage";

    public static readonly IReadOnlyList<string> All = [Publishing, Wiki, WebPartPage];
    public static bool IsKnown(string? layout) => layout is not null && All.Contains(layout, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Publication state of a page in SharePoint (<c>SPFile.Level</c>).</summary>
public static class SharePointPageStatus
{
    public const string Published  = "published";
    public const string Draft      = "draft";
    public const string CheckedOut = "checkedout";

    public static readonly IReadOnlyList<string> All = [Published, Draft, CheckedOut];
    public static bool IsKnown(string? status) => status is not null && All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

/// <summary>One page in the manifest.</summary>
/// <param name="Id">List item <c>UniqueId</c> (GUID); stable across re-exports and used as the migration source id (#193).</param>
/// <param name="Url">Server-relative URL, e.g. <c>/sites/vba/Pages/About-Us.aspx</c>.</param>
/// <param name="ContentFile">Package-relative path of the raw body HTML.</param>
/// <param name="Author">SharePoint login of the creator (<c>Author</c> column), matched against <c>users[].login</c>.</param>
public sealed record SharePointPage(
    string Id,
    string Url,
    string Title,
    string Layout,
    string ContentFile,
    string Status,
    string? Description,
    string? Author,
    string? Editor,
    DateTimeOffset? Created,
    DateTimeOffset? Modified);

/// <summary>One document-library file in the manifest.</summary>
/// <param name="Url">Server-relative URL of the file in SharePoint.</param>
/// <param name="File">Package-relative path of the exported file.</param>
/// <param name="ContentType">MIME type as SharePoint reported it. Advisory only — the importer sniffs the content (#194).</param>
public sealed record SharePointDocument(
    string Id,
    string Library,
    string Url,
    string File,
    long? SizeBytes,
    string? ContentType,
    string? Title,
    string? AltText,
    string? Author,
    DateTimeOffset? Modified);

/// <summary>One site user in the manifest.</summary>
/// <param name="Login">Claims login, e.g. <c>i:0#.w|VA\jsmith</c>.</param>
/// <param name="Upn">User principal name when the exporter could resolve it from AD.</param>
public sealed record SharePointUser(
    string Login,
    string? Upn,
    string? Email,
    string? DisplayName)
{
    /// <summary>The identity the CMS matches on (#195): UPN, else email.</summary>
    public string? PrincipalName => !string.IsNullOrWhiteSpace(Upn) ? Upn : !string.IsNullOrWhiteSpace(Email) ? Email : null;
}

public enum PackageProblemSeverity { Warning, Error }

/// <summary>A validation finding about a package. Errors make the package non-importable.</summary>
public sealed record PackageProblem(PackageProblemSeverity Severity, string Code, string Message, string? EntityId = null)
{
    public static PackageProblem Error(string code, string message, string? entityId = null)   => new(PackageProblemSeverity.Error, code, message, entityId);
    public static PackageProblem Warning(string code, string message, string? entityId = null) => new(PackageProblemSeverity.Warning, code, message, entityId);
}

/// <summary>Problem codes emitted by <see cref="SharePointExportReader"/>; stable so reports (#196) can key on them.</summary>
public static class PackageProblemCodes
{
    public const string PackageMissing          = "package-missing";
    public const string ManifestMissing         = "manifest-missing";
    public const string ManifestInvalid         = "manifest-invalid";
    public const string FormatUnsupported       = "format-unsupported";
    public const string SourceWebMissing        = "source-web-missing";
    public const string PathEscapesPackage      = "path-escapes-package";
    public const string PageIdMissing           = "page-id-missing";
    public const string PageIdDuplicate         = "page-id-duplicate";
    public const string PageUrlMissing          = "page-url-missing";
    public const string PageTitleMissing        = "page-title-missing";
    public const string PageLayoutUnknown       = "page-layout-unknown";
    public const string PageStatusUnknown       = "page-status-unknown";
    public const string PageContentMissing      = "page-content-missing";
    public const string PageAuthorUnknown       = "page-author-unknown";
    public const string DocumentIdMissing       = "document-id-missing";
    public const string DocumentIdDuplicate     = "document-id-duplicate";
    public const string DocumentUrlMissing      = "document-url-missing";
    public const string DocumentFileMissing     = "document-file-missing";
    public const string DocumentSizeMismatch    = "document-size-mismatch";
    public const string UserLoginMissing        = "user-login-missing";
    public const string UserLoginDuplicate      = "user-login-duplicate";
    public const string UserPrincipalMissing    = "user-principal-missing";
}

/// <summary>Outcome of <see cref="SharePointExportReader.Read"/>.</summary>
public sealed class SharePointExportReadResult
{
    public required string PackagePath { get; init; }
    /// <summary>Null when the manifest could not be parsed at all.</summary>
    public SharePointExportPackage? Package { get; init; }
    public required IReadOnlyList<PackageProblem> Problems { get; init; }

    public IEnumerable<PackageProblem> Errors   => Problems.Where(p => p.Severity == PackageProblemSeverity.Error);
    public IEnumerable<PackageProblem> Warnings => Problems.Where(p => p.Severity == PackageProblemSeverity.Warning);

    /// <summary>True when the package parsed and has no errors. Warnings do not block an import.</summary>
    public bool IsImportable => Package is not null && !Errors.Any();
}
