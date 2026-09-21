/**
 * AdminSettingsPage — Admin Settings root page.
 *
 * Sections:
 *   - Site Settings (issue #143, epic #141): runtime configuration and feature flags,
 *     stored in the database and applied without a build or restart.
 *   - AD Group Mappings (story #67).
 *   - Link to the Database Health dashboard at /admin/settings/health (#172).
 *
 * Route: /admin/settings (added to main.tsx)
 * Access: SystemAdmin only (API enforces; UI can also gate via role claim).
 */

import { Link } from 'react-router-dom';
import { SiteSettingsSection } from '../features/siteSettings';
import { AdGroupMappingsSection } from '../features/adminSettings/AdGroupMappingsSection';

export function AdminSettingsPage(): JSX.Element {
  return (
    <main id="main-content">
      <h1>Admin Settings</h1>
      <SiteSettingsSection />
      <AdGroupMappingsSection />
      <section aria-labelledby="db-health-heading" className="margin-top-5">
        <h2 id="db-health-heading">Database Health</h2>
        <p className="usa-prose">
          Index fragmentation, table sizes and long-running queries (Developer or SystemAdmin).
        </p>
        <Link to="/admin/settings/health" className="usa-button usa-button--outline">
          Open database health
        </Link>
      </section>
    </main>
  );
}
