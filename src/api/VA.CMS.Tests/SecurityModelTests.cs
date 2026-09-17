using Microsoft.Data.SqlClient;

namespace VA.CMS.Tests;

/// <summary>
/// Integration tests for issue #73 — V003 security model — extended by #157.
///
/// The fixture provisions the logins with infra/sql/provision-logins.sql (test
/// passwords, CHECK_POLICY = ON) and then runs the migration set; V003 maps the
/// logins to database users and applies the EXECUTE-only / SELECT-only grants.
///
/// 1. vacms_app: direct SELECT on a table is denied; EXECUTE on procedures works.
/// 2. vacms_app: cannot read the DbUp journal directly but can call
///    usp_Migrations_ListApplied, which the API's startup check relies on.
/// 3. vacms_readonly: SELECT works, INSERT is denied.
/// 4. No migration script carries login or password material.
/// </summary>
[Collection("Database")]
public class SecurityModelTests(DatabaseFixture fixture)
{
    private string AppConnectionString() => fixture.AppConnectionString();

    /// <summary>
    /// AC: calling SELECT * FROM ContentEntry with the app connection string
    /// returns a permissions error (SqlException with error number 229).
    /// </summary>
    [Fact]
    public async Task AppLogin_DirectSelect_ContentEntry_ThrowsPermissionsError()
    {
        await using var conn = new SqlConnection(AppConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ContentEntry";

        var ex = await Assert.ThrowsAsync<SqlException>(
            () => cmd.ExecuteReaderAsync());

        // SQL Server error number 229 = SELECT permission denied on object.
        Assert.Equal(229, ex.Number);
    }

    /// <summary>
    /// AC: EXEC usp_ContentEntry_GetById @Id=1 succeeds with the app connection
    /// string (no permissions error; zero or more rows returned is acceptable).
    /// </summary>
    [Fact]
    public async Task AppLogin_ExecStoredProcedure_GetById_Succeeds()
    {
        await using var conn = new SqlConnection(AppConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentEntry_GetById @Id";
        cmd.Parameters.AddWithValue("@Id", 1L);

        // Must not throw — vacms_app has EXECUTE on the dbo schema.
        // The SP returns 0 rows when Id=1 doesn't exist, which is fine.
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            // Drain any result set
            while (await reader.ReadAsync()) { }
        });

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("SELECT TOP 1 * FROM [User]")]
    [InlineData("SELECT TOP 1 * FROM AuditLog")]
    [InlineData("SELECT TOP 1 * FROM SchemaVersions")]
    [InlineData("SELECT TOP 1 * FROM SiteSetting")]
    public async Task AppLogin_Cannot_Select_Any_Table_Directly(string sql)
    {
        await using var conn = new SqlConnection(AppConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var ex = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteReaderAsync());

        Assert.Equal(229, ex.Number);
    }

    [Fact]
    public async Task AppLogin_Cannot_Update_Or_Delete_AuditLog()
    {
        await using var conn = new SqlConnection(AppConnectionString());
        await conn.OpenAsync();

        foreach (var sql in new[] { "DELETE FROM AuditLog WHERE Id = -1", "UPDATE AuditLog SET Action = 'x' WHERE Id = -1" })
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var ex = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(229, ex.Number);
        }
    }

    [Fact]
    public async Task AppLogin_Can_Read_Migration_Status_Through_Procedure()
    {
        await using var conn = new SqlConnection(AppConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Migrations_ListApplied";

        var applied = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            applied.Add(reader.GetString(0));

        Assert.Contains("V003__security_model.sql", applied);
        Assert.Contains("V042__migration_status_sp.sql", applied);
    }

    [Fact]
    public async Task ReadonlyLogin_Can_Select_But_Not_Write()
    {
        await using var conn = new SqlConnection(fixture.ReadonlyConnectionString());
        await conn.OpenAsync();

        await using (var select = conn.CreateCommand())
        {
            select.CommandText = "SELECT COUNT(*) FROM ContentEntry";
            Assert.NotNull(await select.ExecuteScalarAsync());
        }

        await using var delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM SiteSetting WHERE Id = -1";
        var ex = await Assert.ThrowsAsync<SqlException>(() => delete.ExecuteNonQueryAsync());
        Assert.Equal(229, ex.Number);
    }

    [Fact]
    public async Task Logins_Are_Policy_Checked()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, is_policy_checked FROM sys.sql_logins WHERE name IN ('vacms_app', 'vacms_readonly')";

        var rows = new Dictionary<string, bool>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows[reader.GetString(0)] = reader.GetBoolean(1);

        Assert.True(rows["vacms_app"]);
        Assert.True(rows["vacms_readonly"]);
    }

    [Fact]
    public void No_Migration_Script_Contains_Login_Or_Password_Material()
    {
        foreach (var file in Directory.GetFiles(fixture.MigrationsPath, "*.sql"))
        {
            // Strip comment lines so prose about the policy does not trip the check.
            var code = string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
            Assert.DoesNotContain("CREATE LOGIN", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ALTER LOGIN",  code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PASSWORD",     code, StringComparison.OrdinalIgnoreCase);
        }
    }
}
