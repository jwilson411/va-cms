/**
 * DbHealthPage — Database health dashboard (BRD NFR-OPS-01).
 *
 * Route: /admin/settings/health
 * Access: Developer or SystemAdmin (API policy CanDevelop enforces; a 403 is
 *         shown as an access message rather than hidden).
 *
 * Moved here from the public Next.js site in #172: the old page shipped an
 * `/admin/…` route on the public host and called the API with a hard-coded
 * DevBypass header, so it could never work outside local development.
 *
 * Accessibility: USWDS 3.x tables with captions and scoped headers, headings
 * wired to their sections with aria-labelledby, no inline styles.
 */

import React from 'react';
import { Link } from 'react-router-dom';
import { useDbHealth, type DbHealthResponse } from './useDbHealth';

/** Fragmentation guidance from the SQL Server maintenance docs (rebuild ≥ 30 %, reorganise 10–30 %). */
function fragmentationClass(pct: number): string | undefined {
  if (pct >= 30) return 'text-secondary-dark text-bold';
  if (pct >= 10) return 'text-accent-warm-darker';
  return undefined;
}

function Placeholder({ colSpan, children }: { colSpan: number; children: React.ReactNode }): JSX.Element {
  return (
    <tr>
      <td colSpan={colSpan} className="text-base-dark">
        {children}
      </td>
    </tr>
  );
}

function HealthTables({ data }: { data: DbHealthResponse }): JSX.Element {
  return (
    <>
      <p className="text-base-dark font-body-sm margin-bottom-4">
        Collected at{' '}
        <time dateTime={data.collectedAt}>{new Date(data.collectedAt).toLocaleString()}</time>
      </p>

      {/* ── Index fragmentation ─────────────────────────────────────────────── */}
      <section aria-labelledby="heading-fragmentation" className="margin-bottom-5">
        <h2 id="heading-fragmentation">Index fragmentation</h2>
        <p className="usa-hint margin-bottom-2">
          Indexes with more than 50 pages. Above 30 % should be rebuilt; 10–30 % can be reorganised.
        </p>
        <table className="usa-table usa-table--borderless usa-table--compact width-full">
          <caption>Index fragmentation by table and index</caption>
          <thead>
            <tr>
              <th scope="col">Table</th>
              <th scope="col">Index</th>
              <th scope="col">Fragmentation %</th>
              <th scope="col">Pages</th>
            </tr>
          </thead>
          <tbody>
            {data.indexFragmentation.length === 0 ? (
              <Placeholder colSpan={4}>No indexes over the page threshold.</Placeholder>
            ) : (
              data.indexFragmentation.map((r) => (
                <tr key={`${r.tableName}.${r.indexName}`}>
                  <th scope="row">{r.tableName}</th>
                  <td>{r.indexName}</td>
                  <td className={fragmentationClass(r.fragmentationPct)}>{r.fragmentationPct.toFixed(1)} %</td>
                  <td>{r.pageCount.toLocaleString()}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </section>

      {/* ── Table sizes ─────────────────────────────────────────────────────── */}
      <section aria-labelledby="heading-tablesizes" className="margin-bottom-5">
        <h2 id="heading-tablesizes">Table sizes</h2>
        <table className="usa-table usa-table--borderless usa-table--compact width-full">
          <caption>Row counts and storage usage per table</caption>
          <thead>
            <tr>
              <th scope="col">Table</th>
              <th scope="col">Rows</th>
              <th scope="col">Total MB</th>
              <th scope="col">Used MB</th>
            </tr>
          </thead>
          <tbody>
            {data.tableSizes.length === 0 ? (
              <Placeholder colSpan={4}>No tables reported.</Placeholder>
            ) : (
              data.tableSizes.map((r) => (
                <tr key={r.tableName}>
                  <th scope="row">{r.tableName}</th>
                  <td>{r.rowCount.toLocaleString()}</td>
                  <td>{r.totalSizeMB.toLocaleString()}</td>
                  <td>{r.usedSizeMB.toLocaleString()}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </section>

      {/* ── Long-running queries ────────────────────────────────────────────── */}
      <section aria-labelledby="heading-longqueries" className="margin-bottom-6">
        <h2 id="heading-longqueries">Long-running queries</h2>
        <p className="usa-hint margin-bottom-2">Queries running longer than 5 seconds at the time of collection.</p>
        <table className="usa-table usa-table--borderless usa-table--compact width-full">
          <caption>Queries running longer than 5 seconds</caption>
          <thead>
            <tr>
              <th scope="col">Session</th>
              <th scope="col">Status</th>
              <th scope="col">Duration (s)</th>
              <th scope="col">Command</th>
              <th scope="col">Wait type</th>
              <th scope="col">Blocking</th>
              <th scope="col">Query</th>
            </tr>
          </thead>
          <tbody>
            {data.longRunningQueries.length === 0 ? (
              <Placeholder colSpan={7}>No long-running queries.</Placeholder>
            ) : (
              data.longRunningQueries.map((r) => (
                <tr key={r.sessionId}>
                  <th scope="row">{r.sessionId}</th>
                  <td>{r.status}</td>
                  <td>{r.durationSec}</td>
                  <td>{r.command}</td>
                  <td>{r.waitType ?? '—'}</td>
                  <td>{r.blockingSessionId ?? '—'}</td>
                  <td>{r.queryText ? <code className="font-mono-xs">{r.queryText.slice(0, 120)}</code> : '—'}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </section>
    </>
  );
}

export function DbHealthPage(): JSX.Element {
  const { data, isLoading, isError, error, refetch, isFetching } = useDbHealth();

  return (
    <main id="main-content">
      <nav aria-label="Breadcrumb" className="usa-breadcrumb margin-bottom-2">
        <ol className="usa-breadcrumb__list">
          <li className="usa-breadcrumb__list-item">
            <Link to="/admin/settings" className="usa-breadcrumb__link">
              Admin Settings
            </Link>
          </li>
          <li className="usa-breadcrumb__list-item usa-current" aria-current="page">
            Database health
          </li>
        </ol>
      </nav>

      <h1>Database health</h1>
      <p className="usa-prose">
        Index fragmentation, table sizes and long-running queries from the CMS database. Available to
        Developer and SystemAdmin roles.
      </p>

      <button
        type="button"
        className="usa-button usa-button--outline margin-bottom-3"
        onClick={() => void refetch()}
        disabled={isFetching}
      >
        {isFetching ? 'Refreshing…' : 'Refresh'}
      </button>

      {isLoading && (
        <p role="status" aria-live="polite">
          Loading database health…
        </p>
      )}

      {isError && (
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <h2 className="usa-alert__heading">
              {error.status === 403 ? 'Access denied' : 'Unable to load health data'}
            </h2>
            <p className="usa-alert__text">
              {error.status === 403
                ? 'Database health requires the Developer or SystemAdmin role.'
                : `Could not read /api/v1/admin/health/db: ${error.message}`}
            </p>
          </div>
        </div>
      )}

      {data && <HealthTables data={data} />}
    </main>
  );
}
