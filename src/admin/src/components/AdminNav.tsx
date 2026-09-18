/**
 * AdminNav — keyboard-accessible admin navigation component.
 *
 * Issue #64: Complete keyboard navigation audit across admin.
 *
 * Implements:
 *   - "Skip to main content" skip link (WCAG 2.4.1, Section 508)
 *   - Landmark navigation using <nav> with aria-label
 *   - All links are keyboard-reachable and have descriptive labels
 *   - Active route indicated via aria-current="page"
 *
 * USWDS sidenav pattern:
 *   https://designsystem.digital.gov/components/sidenav/
 */

import React from 'react';
import { NavLink } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { clientSettingKeys, useClientSettings, type ClientSettingKey } from '../features/siteSettings/useClientSettings';
import { SEARCH_ANALYTICS_ROLES } from '../features/searchAnalytics/access';

interface NavItem {
  label: string;
  to: string;
  /** aria-label override when the visible label alone is ambiguous. */
  ariaLabel?: string;
  /** Site setting (bool) that must be on for the item to render. */
  feature?: ClientSettingKey;
  /** Role names (any one of) the user must hold for the item to render; omitted = every role. */
  roles?: readonly string[];
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', to: '/admin' },
  { label: 'Content', to: '/admin/content', ariaLabel: 'Content entries' },
  { label: 'Media', to: '/admin/media', ariaLabel: 'Media library' },
  { label: 'Content Types', to: '/admin/content-types' },
  { label: 'Users', to: '/admin/users', ariaLabel: 'User directory' },
  { label: 'Audit Log', to: '/admin/audit' },
  // #175: the API answers 403 below SiteAdmin, so the link is not offered either.
  { label: 'Search Analytics', to: '/admin/search/analytics', feature: clientSettingKeys.featureSearchAnalytics, roles: SEARCH_ANALYTICS_ROLES },
  { label: 'Navigation', to: '/admin/navigation', ariaLabel: 'Navigation menu editor' },
  { label: 'Redirects', to: '/admin/redirects', ariaLabel: 'Redirect management' },
  { label: 'Search Pins', to: '/admin/search/pins' },
  { label: 'Webhooks', to: '/admin/webhooks', ariaLabel: 'Webhook management' },
  { label: 'Settings', to: '/admin/settings', ariaLabel: 'Admin settings' },
];

/**
 * Admin sidebar navigation.
 *
 * Keyboard users can Tab to the skip-link at the top (visible on focus)
 * then activate it to jump directly to `#main-content`, bypassing repeated
 * navigation on every page.
 */
/**
 * Skip navigation link — visually hidden until focused. Targets `id="main-content"`
 * which every admin page sets on its <main>. Rendered by AdminLayout as the very
 * first element in the shell: .usa-skipnav is absolutely positioned, so it must
 * sit outside the USWDS grid columns (which are position: relative) or it shows
 * inside the sidebar instead of off-screen.
 */
export function SkipNav(): JSX.Element {
  return (
    <a
      href="#main-content"
      className="usa-skipnav"
      data-testid="skip-nav-link"
    >
      Skip to main content
    </a>
  );
}

export function AdminNav(): JSX.Element {
  // Feature-flagged items (site settings) disappear from the nav while the flag is off;
  // role-gated items disappear for users the API would refuse anyway.
  const settings = useClientSettings();
  const { roles } = useAuth();
  const visibleItems = NAV_ITEMS.filter(
    (item) =>
      (!item.feature || settings.getBool(item.feature)) &&
      (!item.roles || item.roles.some((r) => roles.includes(r))),
  );

  return (
    <>
      <nav
        aria-label="Admin navigation"
        data-testid="admin-nav"
      >
        {/* USWDS 3 sidenav: the class goes on the <ul>, items on <li> */}
        <ul className="usa-sidenav">
          {visibleItems.map(({ label, to, ariaLabel }) => (
            <li key={to} className="usa-sidenav__item">
              <NavLink
                to={to}
                aria-label={ariaLabel}
                aria-current={undefined /* NavLink adds aria-current="page" automatically */}
                className={({ isActive }) =>
                  isActive ? 'usa-current' : undefined
                }
                end={to === '/admin'}
              >
                {label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
    </>
  );
}
