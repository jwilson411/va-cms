/**
 * Admin Health Dashboard — /admin/settings/health
 *
 * Server Component: fetches GET /api/v1/admin/health/db at render time.
 * Displays results using USWDS Table components.
 *
 * BRD NFR-OPS-01: database health (fragmentation, table sizes, long-running
 * queries) is surfaced to administrators via this page.
 *
 * In production this page is protected by SystemAdmin role auth middleware.
 * In dev (Auth:Mode=DevBypass) it is accessible with any X-Dev-User header.
 */

import React from 'react';
import { UswdsBanner, UswdsHeader, UswdsTable } from '@/components/uswds';

// ── Types matching DbHealthResponse from the API ───────────────────────────

interface IndexFragmentationRow {
  tableName: string;
  indexName: string;
  fragmentationPct: number;
  pageCount: number;
}

interface TableSizeRow {
  tableName: string;
  rowCount: number;
  totalSizeMB: number;
  usedSizeMB: number;
}

interface LongRunningQueryRow {
  sessionId: number;
  status: string;
  startTime: string;
  durationSec: number;
  command: string;
  queryText: string | null;
  waitType: string | null;
  blockingSessionId: number | null;
}

interface DbHealthResponse {
  indexFragmentation: IndexFragmentationRow[];
  tableSizes: TableSizeRow[];
  longRunningQueries: LongRunningQueryRow[];
  collectedAt: string;
}

// ── Data fetching ──────────────────────────────────────────────────────────

async function fetchDbHealth(): Promise<DbHealthResponse | null> {
  const apiBase = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5100';
  try {
    const res = await fetch(`${apiBase}/api/v1/admin/health/db`, {
      headers: {
        // DevBypass mode: identify as a system admin for local development.
        'X-Dev-User': 'admin@va.gov',
      },
      // Do not cache — health data should be fresh on every request.
      cache: 'no-store',
    });
    if (!res.ok) return null;
    return (await res.json()) as DbHealthResponse;
  } catch {
    return null;
  }
}

// ── Page ───────────────────────────────────────────────────────────────────

const NAV = [
  { label: 'Dashboard', href: '/admin' },
  { label: 'Settings', href: '/admin/settings' },
  { label: 'Health', href: '/admin/settings/health' },
];

export default async function AdminHealthPage(): Promise<React.ReactElement> {
  const data = await fetchDbHealth();

  return (
    <>
      <UswdsBanner />
      <UswdsHeader siteTitle="VA CMS — Admin" navigation={NAV} />

      <main id="main-content">
        <div className="grid-container margin-top-4">
          {/* Page heading */}
          <nav aria-label="Breadcrumb" className="usa-breadcrumb margin-bottom-2">
            <ol className="usa-breadcrumb__list">
              <li className="usa-breadcrumb__list-item">
                <a href="/admin" className="usa-breadcrumb__link">Admin</a>
              </li>
              <li className="usa-breadcrumb__list-item">
                <a href="/admin/settings" className="usa-breadcrumb__link">Settings</a>
              </li>
              <li className="usa-breadcrumb__list-item usa-current" aria-current="page">
                Database Health
              </li>
            </ol>
          </nav>

          <h1 className="margin-bottom-4">Database Health</h1>

          {data === null ? (
            <div
              className="usa-alert usa-alert--error"
              role="alert"
              aria-live="assertive"
            >
              <div className="usa-alert__body">
                <h4 className="usa-alert__heading">Unable to load health data</h4>
                <p className="usa-alert__text">
                  Could not reach{' '}
                  <code>/api/v1/admin/health/db</code>. Verify the API is running
                  and you have SystemAdmin access.
                </p>
              </div>
            </div>
          ) : (
            <>
              <p className="text-base-dark font-body-sm margin-bottom-4">
                Collected at{' '}
                <time dateTime={data.collectedAt}>
                  {new Date(data.collectedAt).toLocaleString()}
                </time>
              </p>

              {/* ── Index Fragmentation ─────────────────────────────────── */}
              <section aria-labelledby="heading-fragmentation" className="margin-bottom-5">
                <h2 id="heading-fragmentation" className="font-heading-lg margin-bottom-2">
                  Index Fragmentation
                </h2>
                <p className="usa-hint margin-bottom-2">
                  Indexes with page count &gt; 50. Fragmentation above 30% should be rebuilt;
                  10–30% can be reorganised.
                </p>
                <UswdsTable
                  caption="Index fragmentation by table and index"
                  headers={['Table', 'Index', 'Fragmentation %', 'Pages']}
                  rows={data.indexFragmentation.map((r) => [
                    r.tableName,
                    r.indexName,
                    r.fragmentationPct.toFixed(1) + ' %',
                    r.pageCount.toLocaleString(),
                  ])}
                />
              </section>

              {/* ── Table Sizes ─────────────────────────────────────────── */}
              <section aria-labelledby="heading-tablesizes" className="margin-bottom-5">
                <h2 id="heading-tablesizes" className="font-heading-lg margin-bottom-2">
                  Table Sizes
                </h2>
                <UswdsTable
                  caption="Row counts and storage usage per table"
                  headers={['Table', 'Rows', 'Total MB', 'Used MB']}
                  rows={data.tableSizes.map((r) => [
                    r.tableName,
                    r.rowCount.toLocaleString(),
                    r.totalSizeMB.toLocaleString(),
                    r.usedSizeMB.toLocaleString(),
                  ])}
                />
              </section>

              {/* ── Long-Running Queries ─────────────────────────────────── */}
              <section aria-labelledby="heading-longqueries" className="margin-bottom-6">
                <h2 id="heading-longqueries" className="font-heading-lg margin-bottom-2">
                  Long-Running Queries
                </h2>
                <p className="usa-hint margin-bottom-2">
                  Queries running longer than 5 seconds at the time of collection.
                </p>
                <UswdsTable
                  caption="Queries running longer than 5 seconds"
                  headers={[
                    'Session',
                    'Status',
                    'Duration (s)',
                    'Command',
                    'Wait Type',
                    'Blocking',
                    'Query',
                  ]}
                  rows={data.longRunningQueries.map((r) => [
                    r.sessionId,
                    r.status,
                    r.durationSec,
                    r.command,
                    r.waitType ?? '—',
                    r.blockingSessionId != null ? r.blockingSessionId : '—',
                    r.queryText != null ? (
                      <code className="font-mono-xs">{r.queryText.slice(0, 120)}</code>
                    ) : (
                      '—'
                    ),
                  ])}
                />
              </section>
            </>
          )}
        </div>
      </main>
    </>
  );
}
