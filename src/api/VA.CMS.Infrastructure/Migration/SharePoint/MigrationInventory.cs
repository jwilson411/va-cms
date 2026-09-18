using System.Globalization;
using System.Text;

namespace VA.CMS.Infrastructure.Migration.SharePoint;

/// <summary>
/// The go/no-go summary printed by <c>vacms migrate sharepoint --dry-run</c> (#191):
/// what is in the package, what is wrong with it, and whether an import could start.
/// Plain text, deterministic (sorted groups), so it can be pasted into a ticket and
/// asserted on in tests.
/// </summary>
public static class MigrationInventory
{
    public static string Render(SharePointExportReadResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"SharePoint export package: {result.PackagePath}");

        var pkg = result.Package;
        if (pkg is not null)
        {
            var exported = pkg.ExportedAt is { } at ? at.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "(unknown)";
            var exporter = pkg.ExporterVersion is null ? string.Empty : $"   exporter {pkg.ExporterVersion}";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Format:      {pkg.Format}   exported {exported}{exporter}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Source web:  {pkg.SourceWeb.Url}{(pkg.SourceWeb.Title is null ? string.Empty : $"   \"{pkg.SourceWeb.Title}\"")}");
            sb.AppendLine();

            RenderPages(sb, pkg);
            RenderDocuments(sb, pkg);
            RenderUsers(sb, pkg);
        }

        RenderProblems(sb, result);

        sb.AppendLine();
        sb.AppendLine(result.IsImportable
            ? "Result: package is importable."
            : "Result: package is NOT importable — fix the errors above and re-run.");
        return sb.ToString();
    }

    private static void RenderPages(StringBuilder sb, SharePointExportPackage pkg)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"Pages: {pkg.Pages.Count}");
        if (pkg.Pages.Count == 0) return;
        sb.AppendLine(CultureInfo.InvariantCulture, $"  by layout   {Counts(pkg.Pages.Select(p => p.Layout))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  by status   {Counts(pkg.Pages.Select(p => p.Status))}");
    }

    private static void RenderDocuments(StringBuilder sb, SharePointExportPackage pkg)
    {
        var total = pkg.Documents.Sum(d => ActualSize(pkg, d));
        sb.AppendLine(CultureInfo.InvariantCulture, $"Documents: {pkg.Documents.Count} ({Bytes(total)})");
        if (pkg.Documents.Count == 0) return;

        var byExt = pkg.Documents
            .GroupBy(d => Path.GetExtension(d.File).ToLowerInvariant() is { Length: > 0 } e ? e : "(none)")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()} ({Bytes(g.Sum(d => ActualSize(pkg, d)))})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  by extension  {string.Join(", ", byExt)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  by library    {Counts(pkg.Documents.Select(d => d.Library))}");
    }

    private static void RenderUsers(StringBuilder sb, SharePointExportPackage pkg)
    {
        var with = pkg.Users.Count(u => u.PrincipalName is not null);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Users: {pkg.Users.Count}   with UPN/email {with}, without {pkg.Users.Count - with}");
    }

    private static void RenderProblems(StringBuilder sb, SharePointExportReadResult result)
    {
        var errors   = result.Errors.ToList();
        var warnings = result.Warnings.ToList();
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Problems: {errors.Count} error(s), {warnings.Count} warning(s)");

        var width = result.Problems.Select(p => p.Code.Length).DefaultIfEmpty(0).Max();
        foreach (var p in errors.Concat(warnings))
        {
            var tag = p.Severity == PackageProblemSeverity.Error ? "ERROR" : "WARN ";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {tag}  {p.Code.PadRight(width)}  {p.Message}");
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Counts(IEnumerable<string> keys) =>
        string.Join(", ", keys
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} {g.Count()}"));

    private static long ActualSize(SharePointExportPackage pkg, SharePointDocument d)
    {
        var path = pkg.ResolvePath(d.File);
        return File.Exists(path) ? new FileInfo(path).Length : d.SizeBytes ?? 0;
    }

    /// <summary>Human-readable size (invariant culture): 1 KB, 4.2 MB, 2.33 GB.</summary>
    public static string Bytes(long n)
    {
        var inv = CultureInfo.InvariantCulture;
        return n switch
        {
            < 1024L               => $"{n} B",
            < 1024L * 1024        => (n / 1024.0).ToString("0.#", inv) + " KB",
            < 1024L * 1024 * 1024 => (n / (1024.0 * 1024)).ToString("0.#", inv) + " MB",
            _                     => (n / (1024.0 * 1024 * 1024)).ToString("0.##", inv) + " GB",
        };
    }
}
