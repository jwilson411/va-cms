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

import React, { useState } from 'react';
import { useSearchAnalyticsFull } from './useSearchAnalytics';

const DAYS_OPTIONS = [
  { value: 7,  label: 'Last 7 days' },
  { value: 30, label: 'Last 30 days' },
  { value: 90, label: 'Last 90 days' },
];

const PAGE_SIZE = 50;

export function SearchAnalyticsPage(): JSX.Element {
  const [daysBack, setDaysBack] = useState<number>(30);
  const [page, setPage] = useState<number>(1);

  const { data, isLoading, isError } = useSearchAnalyticsFull(daysBack, page, PAGE_SIZE);

  function handleDaysChange(e: React.ChangeEvent<HTMLSelectElement>) {
    setDaysBack(Number(e.target.value));
    setPage(1); // reset to first page on filter change
  }

  return (
    <main id="main-content" className="grid-container">
      <h1>Search Analytics</h1>
      <p className="usa-prose">
        Query volume, zero-result rate, and click-through rate for all searches.
      </p>

      {/* Filter controls */}
      <div className="usa-form-group">
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
          <p className="usa-prose font-body-xs">
            Showing{' '}
            <strong>
              {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, data.totalRows)}
            </strong>{' '}
            of <strong>{data.totalRows.toLocaleString()}</strong> queries
          </p>

          {data.items.length === 0 ? (
            <p className="usa-prose">No search activity in the selected time range.</p>
          ) : (
            <table
              className="usa-table usa-table--striped usa-table--compact usa-table--scrollable"
              aria-label="Search analytics table"
            >
              <thead>
                <tr>
                  <th scope="col">Query</th>
                  <th scope="col">Searches</th>
                  <th scope="col">Zero Results</th>
                  <th scope="col">Avg Results</th>
                  <th scope="col">Clicks</th>
                  <th scope="col">CTR (%)</th>
                  <th scope="col">Last Searched</th>
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
          )}

          {/* Pagination */}
          {data.totalPages > 1 && (
            <nav aria-label="Search analytics pagination" className="usa-pagination margin-top-2">
              <ul className="usa-pagination__list">
                <li className="usa-pagination__item usa-pagination__arrow">
                  <button
                    type="button"
                    className="usa-pagination__link usa-pagination__previous-page"
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                    disabled={page === 1}
                    aria-label="Previous page"
                  >
                    <span aria-hidden="true">‹</span> Previous
                  </button>
                </li>
                <li className="usa-pagination__item usa-pagination__page-no">
                  <span aria-current="page">
                    Page {page} of {data.totalPages}
                  </span>
                </li>
                <li className="usa-pagination__item usa-pagination__arrow">
                  <button
                    type="button"
                    className="usa-pagination__link usa-pagination__next-page"
                    onClick={() => setPage((p) => Math.min(data.totalPages, p + 1))}
                    disabled={page === data.totalPages}
                    aria-label="Next page"
                  >
                    Next <span aria-hidden="true">›</span>
                  </button>
                </li>
              </ul>
            </nav>
          )}
        </>
      )}
    </main>
  );
}
