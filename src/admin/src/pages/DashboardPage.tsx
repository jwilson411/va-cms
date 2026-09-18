import { useAuth } from '../context/AuthContext';
import { SearchAnalyticsWidget, canReadSearchAnalytics } from '../features/searchAnalytics';

/**
 * DashboardPage — main admin landing page.
 *
 * Issue #51: Adds SearchAnalyticsWidget showing top 10 queries
 * and top 10 zero-result queries (last 30 days).
 * Issue #175: the widget is only offered to roles the API lets read it
 * (SiteAdmin / SystemAdmin); everyone else gets a dashboard without the card
 * instead of a card that fails to load.
 */
export function DashboardPage(): JSX.Element {
  const { roles } = useAuth();
  const showSearchAnalytics = canReadSearchAnalytics(roles);

  return (
    <main id="main-content">
      <h1>Dashboard</h1>

      {/* ── Issue #51: Search Analytics Dashboard Widget ─────────────────── */}
      {showSearchAnalytics && (
        <div className="usa-card margin-top-3">
          <div className="usa-card__header">
            <h2 className="usa-card__heading">Search Analytics</h2>
          </div>
          <div className="usa-card__body">
            <SearchAnalyticsWidget />
          </div>
        </div>
      )}
    </main>
  );
}
