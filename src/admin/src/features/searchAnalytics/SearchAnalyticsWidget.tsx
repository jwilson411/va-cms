/**
 * SearchAnalyticsWidget — Admin dashboard widget for issue #51 (FR-SEARCH-06).
 *
 * Displays:
 *   - Top 10 search queries (last 30 days)
 *   - Top 10 zero-result queries (last 30 days)
 *
 * USWDS 3.x only — no Tailwind, no MUI, no Bootstrap.
 * No inline styles. No focus style overrides.
 * All interactive elements have accessible labels.
 */

import React from 'react';
import { useSearchAnalyticsSummary } from './useSearchAnalytics';

export function SearchAnalyticsWidget(): JSX.Element {
  const { data, isLoading, isError } = useSearchAnalyticsSummary();

  if (isLoading) {
    return (
      <div className="usa-card__body">
        <p className="usa-prose" aria-live="polite" aria-busy="true">
          Loading search analytics…
        </p>
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="usa-card__body">
        <p className="usa-prose text-error" role="alert">
          Unable to load search analytics. Please try again later.
        </p>
      </div>
    );
  }

  return (
    <section aria-labelledby="search-analytics-heading">
      {/* Top Queries */}
      <h3 className="font-heading-sm margin-top-0" id="top-queries-heading">
        Top 10 Queries — Last 30 Days
      </h3>
      {data.topQueries.length === 0 ? (
        <p className="usa-prose font-body-xs">No search data in the last 30 days.</p>
      ) : (
        <table
          className="usa-table usa-table--borderless usa-table--compact width-full"
          aria-labelledby="top-queries-heading"
        >
          <thead>
            <tr>
              <th scope="col">Query</th>
              <th scope="col" aria-label="Search count">#</th>
              <th scope="col" aria-label="Zero-result count">Zero</th>
            </tr>
          </thead>
          <tbody>
            {data.topQueries.map((row) => (
              <tr key={row.query}>
                <td>{row.query}</td>
                <td>{row.searchCount.toLocaleString()}</td>
                <td>{row.zeroResultCount.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* Zero-Result Queries */}
      <h3 className="font-heading-sm margin-top-3" id="zero-result-heading">
        Top 10 Zero-Result Queries — Last 30 Days
      </h3>
      {data.zeroResultQueries.length === 0 ? (
        <p className="usa-prose font-body-xs">No zero-result queries in the last 30 days.</p>
      ) : (
        <table
          className="usa-table usa-table--borderless usa-table--compact width-full"
          aria-labelledby="zero-result-heading"
        >
          <thead>
            <tr>
              <th scope="col">Query</th>
              <th scope="col" aria-label="Zero-result count">Zero</th>
            </tr>
          </thead>
          <tbody>
            {data.zeroResultQueries.map((row) => (
              <tr key={row.query}>
                <td>{row.query}</td>
                <td>{row.zeroResultCount.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="usa-prose font-body-2xs margin-top-2">
        <a href="/admin/search/analytics" className="usa-link">
          View full search analytics →
        </a>
      </p>
    </section>
  );
}
