using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Repository interface for pinned search results management.
/// Issue #52 — BRD FR-SEARCH-04.
/// All methods call EXEC usp_SearchPin_* stored procedures.
/// </summary>
public interface ISearchPinRepository
{
    /// <summary>
    /// Returns all pins. Pass <paramref name="queryString"/> to filter to a specific query.
    /// Calls usp_SearchPin_List.
    /// </summary>
    Task<IReadOnlyList<SearchPin>> ListAsync(string? queryString = null);

    /// <summary>
    /// Creates or updates a pin for the given <paramref name="queryString"/>.
    /// Returns the id of the created or updated pin.
    /// Calls usp_SearchPin_Create.
    /// </summary>
    Task<long> CreateAsync(string queryString, long contentEntryId, long? createdById = null);

    /// <summary>
    /// Deletes a pin by its <paramref name="id"/>.
    /// Calls usp_SearchPin_Delete.
    /// </summary>
    Task DeleteAsync(long id);
}
