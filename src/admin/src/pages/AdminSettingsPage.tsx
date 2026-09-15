/**
 * AdminSettingsPage — Admin Settings root page.
 * Story #67: exposes the AD Group Mappings section.
 *
 * Route: /settings (added to main.tsx)
 * Access: SystemAdmin only (API enforces; UI can also gate via role claim).
 */

import { AdGroupMappingsSection } from '../features/adminSettings/AdGroupMappingsSection';

export function AdminSettingsPage(): JSX.Element {
  return (
    <main id="main-content" className="grid-container">
      <h1>Admin Settings</h1>
      <AdGroupMappingsSection />
    </main>
  );
}
