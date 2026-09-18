using System.Text.Json;
using System.Text.Json.Serialization;

namespace VA.CMS.Infrastructure.Migration.SharePoint;

/// <summary>
/// Parses and validates a SharePoint export package directory (#191, BRD MIG-01).
///
/// The reader never writes anything and never follows a manifest path outside the
/// package root: a manifest is untrusted input that was produced on another host,
/// so every <c>contentFile</c> / <c>file</c> value is normalised and checked to sit
/// under the root before it is looked at. Problems are collected rather than thrown
/// so the dry-run can show an office everything wrong with an export in one pass.
/// </summary>
public static class SharePointExportReader
{
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        NumberHandling              = JsonNumberHandling.AllowReadingFromString,
    };

    public static SharePointExportReadResult Read(string packageDir)
    {
        var problems = new List<PackageProblem>();

        if (string.IsNullOrWhiteSpace(packageDir) || !Directory.Exists(packageDir))
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.PackageMissing, $"Package directory not found: {packageDir}"));
            return new SharePointExportReadResult { PackagePath = packageDir ?? string.Empty, Problems = problems };
        }

        var root         = Path.GetFullPath(packageDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var manifestPath = Path.Combine(root, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.ManifestMissing, $"{ManifestFileName} not found in {root}"));
            return new SharePointExportReadResult { PackagePath = root, Problems = problems };
        }

        ManifestDto? dto;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            dto = JsonSerializer.Deserialize<ManifestDto>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.ManifestInvalid, $"{ManifestFileName} is not valid JSON: {ex.Message}"));
            return new SharePointExportReadResult { PackagePath = root, Problems = problems };
        }

        if (dto is null)
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.ManifestInvalid, $"{ManifestFileName} is empty"));
            return new SharePointExportReadResult { PackagePath = root, Problems = problems };
        }

        if (!string.Equals(dto.Format, SharePointExportPackage.FormatV1, StringComparison.Ordinal))
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.FormatUnsupported,
                $"Unsupported manifest format '{dto.Format ?? "(missing)"}'; this build reads '{SharePointExportPackage.FormatV1}'"));
            return new SharePointExportReadResult { PackagePath = root, Problems = problems };
        }

        if (dto.SourceWeb is null || string.IsNullOrWhiteSpace(dto.SourceWeb.Url))
            problems.Add(PackageProblem.Error(PackageProblemCodes.SourceWebMissing, "manifest.sourceWeb.url is required"));

        var users     = ReadUsers(dto.Users, problems);
        var logins    = new HashSet<string>(users.Select(u => u.Login), StringComparer.OrdinalIgnoreCase);
        var pages     = ReadPages(dto.Pages, root, logins, problems);
        var documents = ReadDocuments(dto.Documents, root, problems);

        var package = new SharePointExportPackage
        {
            RootPath        = root,
            Format          = dto.Format!,
            ExportedAt      = dto.ExportedAt,
            ExporterVersion = dto.ExporterVersion,
            SourceWeb       = new SharePointSourceWeb(dto.SourceWeb?.Url ?? string.Empty, dto.SourceWeb?.Title, dto.SourceWeb?.Id),
            Pages           = pages,
            Documents       = documents,
            Users           = users,
        };

        return new SharePointExportReadResult { PackagePath = root, Package = package, Problems = problems };
    }

    // ── sections ──────────────────────────────────────────────────────────────

    private static List<SharePointUser> ReadUsers(List<UserDto>? dtos, List<PackageProblem> problems)
    {
        var users = new List<SharePointUser>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (u, i) in (dtos ?? []).Select((u, i) => (u, i)))
        {
            if (string.IsNullOrWhiteSpace(u.Login))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.UserLoginMissing, $"users[{i}] has no login"));
                continue;
            }
            if (!seen.Add(u.Login))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.UserLoginDuplicate, $"users[{i}] login '{u.Login}' appears more than once", u.Login));
                continue;
            }

            var user = new SharePointUser(u.Login.Trim(), Blank(u.Upn), Blank(u.Email), Blank(u.DisplayName));
            if (user.PrincipalName is null)
                problems.Add(PackageProblem.Warning(PackageProblemCodes.UserPrincipalMissing,
                    $"user '{user.Login}' has no UPN or email; it will need a --user-map entry or --default-owner to import (#195)", user.Login));
            users.Add(user);
        }
        return users;
    }

    private static List<SharePointPage> ReadPages(List<PageDto>? dtos, string root, HashSet<string> logins, List<PackageProblem> problems)
    {
        var pages = new List<SharePointPage>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (p, i) in (dtos ?? []).Select((p, i) => (p, i)))
        {
            var label = $"pages[{i}]";
            if (string.IsNullOrWhiteSpace(p.Id))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.PageIdMissing, $"{label} has no id"));
                continue;
            }
            var id = p.Id.Trim();
            if (!seen.Add(id))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.PageIdDuplicate, $"page id '{id}' appears more than once", id));
                continue;
            }

            var ok = true;
            if (string.IsNullOrWhiteSpace(p.Url))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.PageUrlMissing, $"page '{id}' has no url", id));
                ok = false;
            }

            var layout = Blank(p.Layout)?.ToLowerInvariant();
            if (!SharePointPageLayout.IsKnown(layout))
            {
                problems.Add(PackageProblem.Warning(PackageProblemCodes.PageLayoutUnknown,
                    $"page '{id}' has layout '{layout ?? "(missing)"}'; treated as {SharePointPageLayout.WebPartPage}", id));
                layout = SharePointPageLayout.WebPartPage;
            }

            var status = Blank(p.Status)?.ToLowerInvariant();
            if (!SharePointPageStatus.IsKnown(status))
            {
                problems.Add(PackageProblem.Warning(PackageProblemCodes.PageStatusUnknown,
                    $"page '{id}' has status '{status ?? "(missing)"}'; treated as {SharePointPageStatus.Draft}", id));
                status = SharePointPageStatus.Draft;
            }

            var contentFile = ResolveInside(root, p.ContentFile, id, "contentFile", problems);
            if (contentFile is null)
                ok = false;
            else if (!File.Exists(contentFile))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.PageContentMissing, $"page '{id}' body file '{p.ContentFile}' does not exist", id));
                ok = false;
            }

            var title = Blank(p.Title);
            if (title is null)
                problems.Add(PackageProblem.Warning(PackageProblemCodes.PageTitleMissing, $"page '{id}' has no title; the file name will be used", id));

            var author = Blank(p.Author);
            if (author is not null && !logins.Contains(author))
                problems.Add(PackageProblem.Warning(PackageProblemCodes.PageAuthorUnknown, $"page '{id}' author '{author}' is not in users[]", id));

            if (!ok) continue;

            pages.Add(new SharePointPage(
                id, p.Url!.Trim(), title ?? Path.GetFileNameWithoutExtension(p.Url!.Trim()), layout!,
                p.ContentFile!.Trim(), status!, Blank(p.Description), author, Blank(p.Editor), p.Created, p.Modified));
        }
        return pages;
    }

    private static List<SharePointDocument> ReadDocuments(List<DocumentDto>? dtos, string root, List<PackageProblem> problems)
    {
        var docs = new List<SharePointDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (d, i) in (dtos ?? []).Select((d, i) => (d, i)))
        {
            var label = $"documents[{i}]";
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.DocumentIdMissing, $"{label} has no id"));
                continue;
            }
            var id = d.Id.Trim();
            if (!seen.Add(id))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.DocumentIdDuplicate, $"document id '{id}' appears more than once", id));
                continue;
            }

            var ok = true;
            if (string.IsNullOrWhiteSpace(d.Url))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.DocumentUrlMissing, $"document '{id}' has no url", id));
                ok = false;
            }

            var file = ResolveInside(root, d.File, id, "file", problems);
            if (file is null)
                ok = false;
            else if (!File.Exists(file))
            {
                problems.Add(PackageProblem.Error(PackageProblemCodes.DocumentFileMissing, $"document '{id}' file '{d.File}' does not exist", id));
                ok = false;
            }
            else if (d.SizeBytes is { } declared && declared != new FileInfo(file).Length)
            {
                problems.Add(PackageProblem.Warning(PackageProblemCodes.DocumentSizeMismatch,
                    $"document '{id}' declares {declared} bytes but the file is {new FileInfo(file).Length} bytes", id));
            }

            if (!ok) continue;

            docs.Add(new SharePointDocument(
                id, Blank(d.Library) ?? "Documents", d.Url!.Trim(), d.File!.Trim(), d.SizeBytes,
                Blank(d.ContentType), Blank(d.Title), Blank(d.AltText), Blank(d.Author), d.Modified));
        }
        return docs;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises a manifest-relative path and returns its absolute form, or null (with a
    /// problem recorded) when it is missing, rooted, or resolves outside <paramref name="root"/>.
    /// </summary>
    private static string? ResolveInside(string root, string? relative, string entityId, string field, List<PackageProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.PathEscapesPackage, $"'{entityId}' has no {field}", entityId));
            return null;
        }

        var trimmed   = relative.Trim();
        var candidate = trimmed.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        // Rooted paths (`/etc/passwd`, `C:\…`, `\\server\share`) are never package-relative. The
        // manifest is usually written on Windows and read on whatever runs the CLI, so Windows
        // drive and UNC prefixes are checked explicitly rather than trusting Path.IsPathRooted.
        var windowsRooted = trimmed.Length >= 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':';
        if (windowsRooted || Path.IsPathRooted(candidate) || candidate.StartsWith(Path.DirectorySeparatorChar))
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.PathEscapesPackage, $"'{entityId}' {field} '{relative}' is not relative to the package", entityId));
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(root, candidate));
        var rootWithSep = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            problems.Add(PackageProblem.Error(PackageProblemCodes.PathEscapesPackage, $"'{entityId}' {field} '{relative}' resolves outside the package", entityId));
            return null;
        }
        return full;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── manifest shape (kept private; the public model is SharePointExportPackage) ──

    private sealed class ManifestDto
    {
        public string? Format { get; set; }
        public DateTimeOffset? ExportedAt { get; set; }
        public string? ExporterVersion { get; set; }
        public SourceWebDto? SourceWeb { get; set; }
        public List<PageDto>? Pages { get; set; }
        public List<DocumentDto>? Documents { get; set; }
        public List<UserDto>? Users { get; set; }
    }

    private sealed class SourceWebDto
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? Id { get; set; }
    }

    private sealed class PageDto
    {
        public string? Id { get; set; }
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? Layout { get; set; }
        public string? ContentFile { get; set; }
        public string? Status { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? Editor { get; set; }
        public DateTimeOffset? Created { get; set; }
        public DateTimeOffset? Modified { get; set; }
    }

    private sealed class DocumentDto
    {
        public string? Id { get; set; }
        public string? Library { get; set; }
        public string? Url { get; set; }
        public string? File { get; set; }
        public long? SizeBytes { get; set; }
        public string? ContentType { get; set; }
        public string? Title { get; set; }
        public string? AltText { get; set; }
        public string? Author { get; set; }
        public DateTimeOffset? Modified { get; set; }
    }

    private sealed class UserDto
    {
        public string? Login { get; set; }
        public string? Upn { get; set; }
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
    }
}
