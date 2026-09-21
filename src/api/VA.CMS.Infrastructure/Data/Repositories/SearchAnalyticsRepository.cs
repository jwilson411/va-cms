using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Search analytics repository. All DB access via EXEC usp_Search_* stored procedures.
/// Issue #51 — BRD FR-SEARCH-06.
/// </summary>
public class SearchAnalyticsRepository : ISearchAnalyticsRepository
{
    private readonly CmsDatabase _db;

    public SearchAnalyticsRepository(CmsDatabase db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchQueryStat>> GetTopQueriesAsync(int topN = 10, int daysBack = 30)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Search_GetTopQueries @TopN, @DaysBack";
        cmd.Parameters.AddWithValue("@TopN", topN);
        cmd.Parameters.AddWithValue("@DaysBack", daysBack);

        var results = new List<SearchQueryStat>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SearchQueryStat
            {
                Query           = reader.GetString(reader.GetOrdinal("Query")),
                SearchCount     = reader.GetInt32(reader.GetOrdinal("SearchCount")),
                ZeroResultCount = reader.GetInt32(reader.GetOrdinal("ZeroResultCount")),
                AvgResultCount  = reader.GetDecimal(reader.GetOrdinal("AvgResultCount")),
                LastSearchedAt  = reader.GetDateTime(reader.GetOrdinal("LastSearchedAt")),
            });
        }
        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchZeroResultStat>> GetZeroResultQueriesAsync(int topN = 10, int daysBack = 30)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Search_GetZeroResultQueries @TopN, @DaysBack";
        cmd.Parameters.AddWithValue("@TopN", topN);
        cmd.Parameters.AddWithValue("@DaysBack", daysBack);

        var results = new List<SearchZeroResultStat>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SearchZeroResultStat
            {
                Query           = reader.GetString(reader.GetOrdinal("Query")),
                ZeroResultCount = reader.GetInt32(reader.GetOrdinal("ZeroResultCount")),
                LastSearchedAt  = reader.GetDateTime(reader.GetOrdinal("LastSearchedAt")),
            });
        }
        return results;
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<SearchAnalyticsRow> Items, int TotalRows)> GetFullAnalyticsAsync(
        int daysBack = 30, int page = 1, int pageSize = 50,
        string sortBy = "SearchCount", string sortDir = "DESC", string? queryFilter = null)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_Search_GetAnalyticsFull @DaysBack, @Page, @PageSize, @SortBy, @SortDir, @QueryFilter, @TotalRows OUTPUT";
        cmd.Parameters.AddWithValue("@DaysBack", daysBack);
        cmd.Parameters.AddWithValue("@Page", page);
        cmd.Parameters.AddWithValue("@PageSize", pageSize);
        cmd.Parameters.AddWithValue("@SortBy", sortBy);
        cmd.Parameters.AddWithValue("@SortDir", sortDir);
        cmd.Parameters.AddWithValue("@QueryFilter", (object?)queryFilter ?? DBNull.Value);

        var totalRowsParam = cmd.Parameters.Add("@TotalRows", System.Data.SqlDbType.Int);
        totalRowsParam.Direction = System.Data.ParameterDirection.Output;

        var results = new List<SearchAnalyticsRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SearchAnalyticsRow
            {
                Query              = reader.GetString(reader.GetOrdinal("Query")),
                SearchCount        = reader.GetInt32(reader.GetOrdinal("SearchCount")),
                ZeroResultCount    = reader.GetInt32(reader.GetOrdinal("ZeroResultCount")),
                AvgResultCount     = reader.GetDecimal(reader.GetOrdinal("AvgResultCount")),
                LastSearchedAt     = reader.GetDateTime(reader.GetOrdinal("LastSearchedAt")),
                ClickCount         = reader.GetInt32(reader.GetOrdinal("ClickCount")),
                ClickThroughRate   = reader.GetDecimal(reader.GetOrdinal("ClickThroughRate")),
            });
        }
        await reader.CloseAsync();

        var totalRows = totalRowsParam.Value == DBNull.Value ? 0 : (int)totalRowsParam.Value;
        return (results, totalRows);
    }

    /// <inheritdoc />
    public async Task LogClickAsync(string query, string clickedSlug, int resultRank = 0, long? userId = null)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_Search_LogClick @Query, @ClickedSlug, @ResultRank, @UserId";
        cmd.Parameters.AddWithValue("@Query", query);
        cmd.Parameters.AddWithValue("@ClickedSlug", clickedSlug);
        cmd.Parameters.AddWithValue("@ResultRank", resultRank);
        cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
