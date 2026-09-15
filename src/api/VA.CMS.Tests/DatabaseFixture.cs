using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using VA.CMS.Infrastructure.Data;
using DbUp;
using DbUp.ScriptProviders;

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

    public string ConnectionString { get; private set; } = string.Empty;

    public CmsDatabase CreateDb() => new CmsDatabase(ConnectionString);

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("VaCms_Test!2026")
            .Build();

        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        // Run all migrations from the repo migrations/ folder
        var migrationsPath = FindMigrationsPath();

        EnsureDatabase.For.SqlDatabase(ConnectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(ConnectionString)
            .WithScriptsFromFileSystem(migrationsPath,
                new FileSystemScriptOptions { IncludeSubDirectories = false })
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"Test DB migration failed: {result.Error}");
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
