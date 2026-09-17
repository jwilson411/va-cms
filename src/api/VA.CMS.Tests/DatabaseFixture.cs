using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Migrations;

namespace VA.CMS.Tests;

/// <summary>
/// Shared xUnit collection fixture that starts a real SQL Server container via TestContainers
/// and runs all DbUp migrations before tests execute. Shared across the test collection
/// to avoid container-per-test overhead.
/// </summary>
[CollectionDefinition("Database")]
public class DatabaseCollectionDefinition : ICollectionFixture<DatabaseFixture> { }

public class DatabaseFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    /// <summary>Test-only secrets handed to infra/sql/provision-logins.sql (#157); never used outside the container.</summary>
    public const string AppPassword      = "Fixture!Exec#2026aZ";
    public const string ReadonlyPassword = "Fixture!Read#2026bY";

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Where the migration scripts were loaded from.</summary>
    public string MigrationsPath { get; private set; } = string.Empty;

    public CmsDatabase CreateDb() => new CmsDatabase(ConnectionString);

    /// <summary>Connection string for the EXECUTE-only application login.</summary>
    public string AppConnectionString() => AsLogin("vacms_app", AppPassword);

    /// <summary>Connection string for the SELECT-only reporting login.</summary>
    public string ReadonlyConnectionString() => AsLogin("vacms_readonly", ReadonlyPassword);

    private string AsLogin(string user, string password)
        => new SqlConnectionStringBuilder(ConnectionString)
        {
            UserID = user, Password = password, IntegratedSecurity = false,
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("VaCms_Test!2026")
            .Build();

        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        // Provision the logins exactly as a deployment would (#157), then migrate
        // with the SA connection — the same two steps DEPLOYMENT.md prescribes.
        MigrationsPath = FindMigrationsPath();
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var dbName = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        await SqlCmdScript.RunAsync(master, await File.ReadAllTextAsync(FindProvisionScript()), new Dictionary<string, string>
        {
            ["VacmsAppPassword"]      = AppPassword,
            ["VacmsReadonlyPassword"] = ReadonlyPassword,
            ["DatabaseName"]          = dbName,
        });

        var result = MigrationRunner.Upgrade(ConnectionString, MigrationsPath);
        if (!result.Successful)
            throw new InvalidOperationException($"Test DB migration failed: {result.Error}");
    }

    private static string FindProvisionScript()
    {
        var root = new DirectoryInfo(FindMigrationsPath()).Parent!.FullName;
        var path = IoPath.Combine(root, "infra", "sql", "provision-logins.sql");
        return File.Exists(path) ? path : throw new FileNotFoundException(path);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static string FindMigrationsPath()
    {
        // AppContext.BaseDirectory = …/src/api/VA.CMS.Tests/bin/Release/net8.0/
        // migrations/ is at the worktree root, 6 levels up from BaseDirectory.
        // Try relative paths from deepest to shallowest.
        var candidates = new[]
        {
            // 6 levels up: net8.0 → Release → bin → VA.CMS.Tests → api → src → worktree-root
            IoPath.GetFullPath(IoPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "migrations")),
            // 5 levels up (in case of flat layout)
            IoPath.GetFullPath(IoPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "migrations")),
            // Adjacent to solution root fallback
            IoPath.GetFullPath(IoPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "migrations")),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        // Last resort: walk up from BaseDirectory until we find a migrations/ sibling
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var migrationsDir = IoPath.Combine(dir.FullName, "migrations");
            if (Directory.Exists(migrationsDir))
                return migrationsDir;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate migrations/ directory. BaseDirectory: {AppContext.BaseDirectory}");
    }
}
