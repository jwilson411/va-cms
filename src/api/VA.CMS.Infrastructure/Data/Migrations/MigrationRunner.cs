using DbUp;
using DbUp.Engine;
using DbUp.ScriptProviders;
using Microsoft.Data.SqlClient;

namespace VA.CMS.Infrastructure.Data.Migrations;

/// <summary>
/// Outcome of a pending-migrations check.
/// </summary>
public sealed record MigrationStatus(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending,
    string? Error)
{
    public bool IsUpToDate => Error is null && Pending.Count == 0;
}

/// <summary>
/// Single home for DbUp script discovery and execution (#157). Used by
/// <c>vacms db migrate</c> (the deployment path, run with an elevated connection)
/// and by API startup, which either migrates (Development opt-in) or verifies
/// nothing is pending through <c>usp_Migrations_ListApplied</c> — the runtime
/// login is EXECUTE-only and may not read dbo.SchemaVersions directly.
/// </summary>
public static class MigrationRunner
{
    /// <summary>
    /// Locates the migrations folder: beside the binaries in a deployment, or the
    /// repo-root <c>migrations/</c> folder when running from a source checkout.
    /// </summary>
    public static string FindMigrationsPath(string? baseDirectory = null)
    {
        var start = baseDirectory ?? AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "migrations");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate a migrations/ directory at or above {start}.");
    }

    /// <summary>Names of every script in the migrations folder, in execution order.</summary>
    public static IReadOnlyList<string> ListScripts(string migrationsPath)
        => Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Applies every pending script (creating the database first when the login may).
    /// Requires DDL rights — this is the deployment account's job, never the API's.
    /// </summary>
    public static DatabaseUpgradeResult Upgrade(string connectionString, string migrationsPath, bool ensureDatabase = true)
    {
        if (ensureDatabase)
            EnsureDatabase.For.SqlDatabase(connectionString);

        return Build(connectionString, migrationsPath).PerformUpgrade();
    }

    /// <summary>
    /// Scripts DbUp would run now, without running them (needs SELECT on the journal).
    /// A database that does not exist yet reports every script.
    /// </summary>
    public static IReadOnlyList<string> GetPendingViaJournal(string connectionString, string migrationsPath)
    {
        try
        {
            return Build(connectionString, migrationsPath).GetScriptsToExecute().Select(s => s.Name).ToList();
        }
        catch (SqlException ex) when (ex.Number == 4060) // cannot open database
        {
            return ListScripts(migrationsPath);
        }
    }

    /// <summary>
    /// Compares the scripts on disk with <c>usp_Migrations_ListApplied</c>. Works for
    /// an EXECUTE-only login. A database that predates V042 (no procedure) reports
    /// every script as pending with an explanatory error.
    /// </summary>
    public static async Task<MigrationStatus> CheckAsync(string connectionString, string migrationsPath, CancellationToken ct = default)
    {
        var onDisk = ListScripts(migrationsPath);
        var applied = new List<string>();

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "EXEC dbo.usp_Migrations_ListApplied";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                applied.Add(reader.GetString(0));
        }
        catch (SqlException ex) when (ex.Number is 2812 or 229)
        {
            // 2812: procedure missing (database is behind V042); 229: EXECUTE denied.
            return new MigrationStatus(applied, onDisk,
                $"Cannot read migration status ({ex.Message.Trim()}). Run `vacms db migrate` with the deployment account.");
        }

        var appliedSet = new HashSet<string>(applied, StringComparer.OrdinalIgnoreCase);
        var pending = onDisk.Where(s => !appliedSet.Contains(s)).ToList();
        return new MigrationStatus(applied, pending, null);
    }

    private static UpgradeEngine Build(string connectionString, string migrationsPath)
        => DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsFromFileSystem(migrationsPath, new FileSystemScriptOptions { IncludeSubDirectories = false })
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();
}
