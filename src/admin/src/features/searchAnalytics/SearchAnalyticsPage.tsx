/**
 * SearchAnalyticsPage — Full search analytics admin page (issue #51, FR-SEARCH-06).
 *
 * Route: /admin/search/analytics
 *
 * Features:
 *   - Full paginated analytics table: query, searches, zero-results, CTR
 *   - Days-back selector (7 / 30 / 90 days)
 *   - Sortable by search count (server sorts by count DESC already)
 *
 * Accessibility:
 *   - All form inputs have <label> elements.
 *   - aria-describedby on error messages.
 *   - No inline styles, no Tailwind, no MUI.
 *   - USWDS 3.x components exclusively.
 */

import React, { useCallback, useState } from 'react';
import {
  useSearchAnalyticsFull,
  type SearchAnalyticsSortBy,
  type SearchAnalyticsSortDir,
} from './useSearchAnalytics';
import { clientSettingKeys, useClientSettings } from '../siteSettings/useClientSettings';
import { AdminPagination, SortableHeader } from '../../components/table';

const DAYS_OPTIONS = [
  { value: 7,  label: 'Last 7 days' },
  { value: 30, label: 'Last 30 days' },
  { value: 90, label: 'Last 90 days' },
];

export function SearchAnalyticsPage(): JSX.Element {
  // Rows per page: admin.searchAnalyticsPageSize (site setting, default 50)
  const clientSettings = useClientSettings();
  const PAGE_SIZE = Math.max(1, clientSettings.getInt(clientSettingKeys.adminSearchAnalyticsPageSize));
  const [daysBack, setDaysBack] = useState<number>(30);
  const [queryFilter, setQueryFilter] = useState<string>('');
  const [sortBy, setSortBy] = useState<SearchAnalyticsSortBy>('SearchCount');
  const [sortDir, setSortDir] = useState<SearchAnalyticsSortDir>('DESC');
  const [page, setPage] = useState<number>(1);

  const { data, isLoading, isError } = useSearchAnalyticsFull(
    daysBack, page, PAGE_SIZE, sortBy, sortDir, queryFilter,
  );

  function handleDaysChange(e: React.ChangeEvent<HTMLSelectElement>) {
    setDaysBack(Number(e.target.value));
    setPage(1); // reset to first page on filter change
  }

  function handleQueryFilterChange(e: React.ChangeEvent<HTMLInputElement>) {
    setQueryFilter(e.target.value);
    setPage(1);
  }

  // Toggle direction if already sorted on this field, else sort DESC on the new field.
  const handleSort = useCallback((field: SearchAnalyticsSortBy) => {
    if (sortBy === field) {
      setSortDir((d) => (d === 'ASC' ? 'DESC' : 'ASC'));
    } else {
      setSortBy(field);
      setSortDir('DESC');
    }
    setPage(1);
  }, [sortBy]);

  return (
    <main id="main-content">
      <h1>Search Analytics</h1>
      <p className="usa-prose">
        Query volume, zero-result rate, and click-through rate for all searches.
      </p>

      {/* Filter controls */}
      <div className="display-flex flex-wrap flex-align-end" style={{ gap: '1rem' }}>
        <div className="usa-form-group margin-top-0">
          <label className="usa-label" htmlFor="days-back-select">
            Date range
          </label>
          <select
            id="days-back-select"
            className="usa-select"
            value={daysBack}
            onChange={handleDaysChange}
            aria-label="Select date range for analytics"
          >
            {DAYS_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label className="usa-label" htmlFor="query-filter-input">
            Filter by query text
          </label>
          <form
            className="usa-search usa-search--small"
            role="search"
            onSubmit={(e) => e.preventDefault()}
          >
            <input
              id="query-filter-input"
              className="usa-input"
              type="search"
              value={queryFilter}
              onChange={handleQueryFilterChange}
              placeholder="e.g. benefits"
            />
            <button type="submit" className="usa-button">
              <img
                src="/uswds/img/usa-icons-bg/search--white.svg"
                className="usa-search__submit-icon"
                alt="Search"
              />
            </button>
          </form>
        </div>
      </div>

      {/* Loading state */}
      {isLoading && (
        <p className="usa-prose" aria-live="polite" aria-busy="true">
          Loading analytics…
        </p>
      )}

      {/* Error state */}
      {isError && (
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <p className="usa-alert__text">
              Failed to load analytics data. Please refresh the page.
            </p>
          </div>
        </div>
      )}

      {/* Analytics table */}
      {!isLoading && !isError && data && (
        <>
          {data.totalRows > 0 && (
            <p className="usa-prose font-body-xs">
              Showing{' '}
              <strong>
                {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, data.totalRows)}
              </strong>{' '}
              of <strong>{data.totalRows.toLocaleString()}</strong> queries
            </p>
          )}

          {data.items.length === 0 ? (
            <p className="usa-prose">
              {queryFilter
                ? 'No queries match that filter in the selected time range.'
                : 'No search activity in the selected time range.'}
            </p>
          ) : (
            <div className="usa-table-container--scrollable" tabIndex={0}>
              <table
                className="usa-table usa-table--borderless width-full"
                aria-label="Search analytics table"
              >
                <thead>
                  <tr>
                    <SortableHeader label="Query" field="Query" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                    <SortableHeader label="Searches" field="SearchCount" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                    <SortableHeader label="Zero Results" field="ZeroResultCount" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                    <SortableHeader label="Avg Results" field="AvgResultCount" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                    <SortableHeader label="Clicks" field="ClickCount" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                    <SortableHeader label="CTR (%)" field="ClickThroughRate" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                    <SortableHeader label="Last Searched" field="LastSearchedAt" currentSortBy={sortBy} currentSortDir={sortDir} onSort={handleSort} />
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((row) => (
                    <tr key={row.query}>
                      <td>{row.query}</td>
                      <td>{row.searchCount.toLocaleString()}</td>
                      <td>{row.zeroResultCount.toLocaleString()}</td>
                      <td>{row.avgResultCount.toFixed(1)}</td>
                      <td>{row.clickCount.toLocaleString()}</td>
                      <td>{row.clickThroughRate.toFixed(1)}</td>
                      <td>{new Date(row.lastSearchedAt).toLocaleDateString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination */}
          <AdminPagination
            page={page}
            totalPages={data.totalPages}
            onPage={setPage}
            ariaLabel="Search analytics pagination"
          />
        </>
      )}
    </main>
  );
}
