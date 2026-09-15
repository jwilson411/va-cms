import { useAuth } from '../context/AuthContext';
import { SearchAnalyticsWidget } from '../features/searchAnalytics';

/**
 * DashboardPage — main admin landing page.
 *
 * Issue #51: Adds SearchAnalyticsWidget showing top 10 queries
 * and top 10 zero-result queries (last 30 days).
 */
export function DashboardPage(): JSX.Element {
  const { logout } = useAuth();

  return (
    <main id="main-content" className="grid-container">
      <h1>VA CMS Admin</h1>
      <p>You are signed in.</p>

      {/* ── Issue #51: Search Analytics Dashboard Widget ─────────────────── */}
      <div className="usa-card margin-top-3">
        <div className="usa-card__header">
          <h2 className="usa-card__heading">Search Analytics</h2>
        </div>
        <div className="usa-card__body">
          <SearchAnalyticsWidget />
        </div>
      </div>

      <div className="margin-top-4">
        <button
          type="button"
          className="usa-button usa-button--secondary"
          onClick={() => void logout()}
        >
          Sign out
        </button>
      </div>
    </main>
  );
}
