using Microsoft.Data.SqlClient;

namespace VA.CMS.Tests;

/// <summary>
/// Integration tests for issue #73 — V003 security model migration.
///
/// These tests verify that:
/// 1. Connecting as the vacms_app service account and issuing a direct SELECT on
///    ContentEntry fails with a permissions error (NFR-DB-01, NFR-DB-04).
/// 2. Connecting as the vacms_app service account and calling the stored procedure
///    usp_ContentEntry_GetById succeeds (EXECUTE permission is granted).
///
/// The test fixture (DatabaseFixture) runs all migrations including V003 against a
/// fresh SQL Server TestContainers instance, so the logins, users, and permission
/// grants are already applied when these tests run.
/// </summary>
[Collection("Database")]
public class SecurityModelTests(DatabaseFixture fixture)
{
    // vacms_app dev password matches the literal in V003__security_model.sql.
    // This is intentionally a known dev-only credential — production uses a
    // deployment-managed secret set via ALTER LOGIN after migration.
    private const string AppPassword = "VaCms_App!Dev2026";

    /// <summary>
    /// Builds a connection string for the vacms_app login against the same
    /// SQL Server instance and database that the test fixture uses.
    /// </summary>
    private string AppConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            UserID = "vacms_app",
            Password = AppPassword,
            IntegratedSecurity = false,
        };
        return builder.ConnectionString;
    }

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
}
