using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Search analytics repository interface — issue #51.
/// All methods call EXEC usp_Search_* stored procedures.
/// </summary>
public interface ISearchAnalyticsRepository
{
    /// <summary>
    /// Returns the top N search queries by volume over the last <paramref name="daysBack"/> days.
    /// Calls usp_Search_GetTopQueries.
    /// </summary>
    Task<IReadOnlyList<SearchQueryStat>> GetTopQueriesAsync(int topN = 10, int daysBack = 30);

    /// <summary>
    /// Returns the top N queries that returned zero results over the last <paramref name="daysBack"/> days.
    /// Calls usp_Search_GetZeroResultQueries.
    /// </summary>
    Task<IReadOnlyList<SearchZeroResultStat>> GetZeroResultQueriesAsync(int topN = 10, int daysBack = 30);

    /// <summary>
    /// Returns a paginated full analytics table (queries + CTR) over the last <paramref name="daysBack"/> days.
    /// Calls usp_Search_GetAnalyticsFull.
    /// </summary>
    Task<(IReadOnlyList<SearchAnalyticsRow> Items, int TotalRows)> GetFullAnalyticsAsync(
        int daysBack = 30, int page = 1, int pageSize = 50);

    /// <summary>
    /// Records a user click on a search result for CTR tracking.
    /// Calls usp_Search_LogClick.
    /// </summary>
    Task LogClickAsync(string query, string clickedSlug, int resultRank = 0, long? userId = null);
}
