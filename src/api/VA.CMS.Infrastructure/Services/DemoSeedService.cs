using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data;

namespace VA.CMS.Infrastructure.Services;

/// <summary>
/// Implements demo data seeding for local development and staging environments.
/// All DB operations delegate to <c>usp_DemoSeed_Run</c> and <c>usp_DemoSeed_Reset</c>
/// stored procedures — no raw DML from this layer.
/// </summary>
public class DemoSeedService : ISeedService
{
    private readonly CmsDatabase _db;

    public DemoSeedService(CmsDatabase db) => _db = db;

    /// <inheritdoc/>
    public async Task SeedDemoAsync()
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC [dbo].[usp_DemoSeed_Run]";
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task ResetAndSeedDemoAsync()
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "EXEC [dbo].[usp_DemoSeed_Reset]";
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "EXEC [dbo].[usp_DemoSeed_Run]";
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
