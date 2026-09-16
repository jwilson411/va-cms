import { SearchAnalyticsWidget } from '../features/searchAnalytics';

/**
 * DashboardPage — main admin landing page.
 *
 * Issue #51: Adds SearchAnalyticsWidget showing top 10 queries
 * and top 10 zero-result queries (last 30 days).
 */
export function DashboardPage(): JSX.Element {
  return (
    <main id="main-content">
      <h1>Dashboard</h1>

      {/* ── Issue #51: Search Analytics Dashboard Widget ─────────────────── */}
      <div className="usa-card margin-top-3">
        <div className="usa-card__header">
          <h2 className="usa-card__heading">Search Analytics</h2>
        </div>
        <div className="usa-card__body">
          <SearchAnalyticsWidget />
        </div>
      </div>
    </main>
  );
}
