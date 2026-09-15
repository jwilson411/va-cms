using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the three usp_Monitor_* stored procedures.
/// Called by the admin health dashboard API endpoint.
/// </summary>
public interface IDbMonitorRepository
{
    /// <summary>Returns index fragmentation data (page_count &gt; 50).</summary>
    Task<IEnumerable<IndexFragmentationRow>> GetIndexFragmentationAsync();

    /// <summary>Returns row count and storage MB for every user table.</summary>
    Task<IEnumerable<TableSizeRow>> GetTableSizesAsync();

    /// <summary>Returns queries that have been running for more than 5 seconds.</summary>
    Task<IEnumerable<LongRunningQueryRow>> GetLongRunningQueriesAsync();
}
