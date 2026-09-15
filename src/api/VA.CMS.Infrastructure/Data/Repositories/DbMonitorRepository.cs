using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Calls usp_Monitor_* stored procedures and maps results.
/// Uses raw ADO.NET — these SPs query DMVs so result columns are fixed
/// and do not match any application table; PetaPoco mapping is not appropriate.
/// </summary>
public class DbMonitorRepository : IDbMonitorRepository
{
    private readonly CmsDatabase _db;

    public DbMonitorRepository(CmsDatabase db) => _db = db;

    public async Task<IEnumerable<IndexFragmentationRow>> GetIndexFragmentationAsync()
    {
        var results = new List<IndexFragmentationRow>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Monitor_IndexFragmentation";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new IndexFragmentationRow
            {
                TableName      = reader.GetString(reader.GetOrdinal("TableName")),
                IndexName      = reader.IsDBNull(reader.GetOrdinal("IndexName"))
                                     ? string.Empty
                                     : reader.GetString(reader.GetOrdinal("IndexName")),
                FragmentationPct = Convert.ToDouble(reader.GetValue(reader.GetOrdinal("FragmentationPct"))),
                PageCount      = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("PageCount"))),
            });
        }
        return results;
    }

    public async Task<IEnumerable<TableSizeRow>> GetTableSizesAsync()
    {
        var results = new List<TableSizeRow>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Monitor_TableSizes";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TableSizeRow
            {
                TableName    = reader.GetString(reader.GetOrdinal("TableName")),
                RowCount     = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("RowCount"))),
                TotalSizeMB  = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("TotalSizeMB"))),
                UsedSizeMB   = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("UsedSizeMB"))),
            });
        }
        return results;
    }

    public async Task<IEnumerable<LongRunningQueryRow>> GetLongRunningQueriesAsync()
    {
        var results = new List<LongRunningQueryRow>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Monitor_LongRunningQueries";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new LongRunningQueryRow
            {
                SessionId        = reader.GetInt32(reader.GetOrdinal("session_id")),
                Status           = reader.GetString(reader.GetOrdinal("status")),
                StartTime        = reader.GetDateTime(reader.GetOrdinal("start_time")),
                DurationSec      = reader.GetInt32(reader.GetOrdinal("DurationSec")),
                Command          = reader.GetString(reader.GetOrdinal("command")),
                QueryText        = reader.IsDBNull(reader.GetOrdinal("QueryText"))
                                       ? null
                                       : reader.GetString(reader.GetOrdinal("QueryText")),
                WaitType         = reader.IsDBNull(reader.GetOrdinal("wait_type"))
                                       ? null
                                       : reader.GetString(reader.GetOrdinal("wait_type")),
                BlockingSessionId = reader.IsDBNull(reader.GetOrdinal("blocking_session_id"))
                                       ? null
                                       : reader.GetInt32(reader.GetOrdinal("blocking_session_id")),
            });
        }
        return results;
    }
}
