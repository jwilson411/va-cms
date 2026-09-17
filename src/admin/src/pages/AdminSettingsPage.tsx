/**
 * AdminSettingsPage — Admin Settings root page.
 *
 * Sections:
 *   - Site Settings (issue #143, epic #141): runtime configuration and feature flags,
 *     stored in the database and applied without a build or restart.
 *   - AD Group Mappings (story #67).
 *
 * Route: /admin/settings (added to main.tsx)
 * Access: SystemAdmin only (API enforces; UI can also gate via role claim).
 */

import { SiteSettingsSection } from '../features/siteSettings';
import { AdGroupMappingsSection } from '../features/adminSettings/AdGroupMappingsSection';

export function AdminSettingsPage(): JSX.Element {
  return (
    <main id="main-content" className="grid-container">
      <h1>Admin Settings</h1>
      <SiteSettingsSection />
      <AdGroupMappingsSection />
    </main>
  );
}
