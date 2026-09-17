using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VA.CMS.Infrastructure.Data.Migrations;

namespace VA.CMS.Tests;

/// <summary>
/// #157: the shared migration runner behind `vacms db migrate` and the API's
/// startup check, plus the SQLCMD-style script runner used for login provisioning.
/// </summary>
public class SqlCmdScriptTests
{
    [Fact]
    public void Substitute_Replaces_Every_Variable()
    {
        var result = SqlCmdScript.Substitute("CREATE LOGIN [a] WITH PASSWORD = N'$(Pw)'; USE [$(Db)]; -- $(Pw)",
            new Dictionary<string, string> { ["Pw"] = "s3cret!", ["Db"] = "VACMS" });

        Assert.Equal("CREATE LOGIN [a] WITH PASSWORD = N's3cret!'; USE [VACMS]; -- s3cret!", result);
    }

    [Fact]
    public void Substitute_Throws_For_Missing_Variable()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SqlCmdScript.Substitute("N'$(Missing)'", new Dictionary<string, string>()));

        Assert.Contains("$(Missing)", ex.Message);
    }

    [Fact]
    public void SplitBatches_Splits_On_Go_And_Drops_Comment_Only_Batches()
    {
        const string script = "-- header comment\nGO\nSELECT 1;\nGO\n\nGO -- trailing\nSELECT 2\ngo\n";

        var batches = SqlCmdScript.SplitBatches(script);

        Assert.Equal(["SELECT 1;", "SELECT 2"], batches);
    }

    [Fact]
    public void Provision_Script_Only_Uses_Declared_Variables()
    {
        var root = new DirectoryInfo(MigrationRunner.FindMigrationsPath()).Parent!.FullName;
        var text = File.ReadAllText(IoPath.Combine(root, "infra", "sql", "provision-logins.sql"));

        // Substituting with exactly the documented variables must not throw.
        SqlCmdScript.Substitute(text, new Dictionary<string, string>
        {
            ["VacmsAppPassword"] = "x", ["VacmsReadonlyPassword"] = "y", ["DatabaseName"] = "z",
        });
        Assert.Contains("CHECK_POLICY = ON", text);
    }
}

[Collection("Database")]
public class MigrationRunnerTests(DatabaseFixture fixture)
{
    [Fact]
    public void ListScripts_Is_Ordered_And_Includes_The_Status_Procedure_Script()
    {
        var scripts = MigrationRunner.ListScripts(fixture.MigrationsPath);

        Assert.Equal(scripts.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), scripts);
        Assert.Contains("V042__migration_status_sp.sql", scripts);
    }

    [Fact]
    public async Task Check_As_App_Login_Reports_Up_To_Date_After_Migrate()
    {
        var status = await MigrationRunner.CheckAsync(fixture.AppConnectionString(), fixture.MigrationsPath);

        Assert.Null(status.Error);
        Assert.Empty(status.Pending);
        Assert.True(status.IsUpToDate);
        Assert.Equal(MigrationRunner.ListScripts(fixture.MigrationsPath).Count, status.Applied.Count);
    }

    [Fact]
    public async Task Check_Reports_Scripts_On_Disk_That_Are_Not_Applied()
    {
        var temp = Directory.CreateTempSubdirectory("vacms-migrations-");
        try
        {
            foreach (var file in Directory.GetFiles(fixture.MigrationsPath, "*.sql"))
                File.Copy(file, IoPath.Combine(temp.FullName, IoPath.GetFileName(file)));
            await File.WriteAllTextAsync(IoPath.Combine(temp.FullName, "V999__not_applied.sql"), "SELECT 1;");

            var status = await MigrationRunner.CheckAsync(fixture.AppConnectionString(), temp.FullName);

            Assert.False(status.IsUpToDate);
            Assert.Equal(["V999__not_applied.sql"], status.Pending);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The API, connected as the EXECUTE-only login with Database:MigrateOnStartup=false,
    /// performs the pending-migrations check at startup and serves traffic when current.
    /// </summary>
    [Fact]
    public async Task Api_Starts_As_App_Login_When_Schema_Is_Current()
    {
        await using var factory = new StartupCheckFactory(fixture.AppConnectionString());
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private sealed class StartupCheckFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // No SKIP_MIGRATIONS here — the point is to exercise the startup check.
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Jwt:SigningKey",  "migration-check-signing-key-32ch!");
            builder.UseSetting("Jwt:Issuer",      "va-cms-api");
            builder.UseSetting("Jwt:Audience",    "va-cms-spa");
            builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
            builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        }
    }

    [Fact]
    public void Journal_Dry_Run_Reports_Nothing_Pending()
    {
        var pending = MigrationRunner.GetPendingViaJournal(fixture.ConnectionString, fixture.MigrationsPath);

        Assert.Empty(pending);
    }
}
