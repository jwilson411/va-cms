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
import { clientSettingKeys, useClientSettings, type ClientSettingKey } from '../features/siteSettings/useClientSettings';

interface NavItem {
  label: string;
  to: string;
  /** aria-label override when the visible label alone is ambiguous. */
  ariaLabel?: string;
  /** Site setting (bool) that must be on for the item to render. */
  feature?: ClientSettingKey;
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', to: '/admin' },
  { label: 'Content', to: '/admin/content', ariaLabel: 'Content entries' },
  { label: 'Media', to: '/admin/media', ariaLabel: 'Media library' },
  { label: 'Content Types', to: '/admin/content-types' },
  { label: 'Users', to: '/admin/users', ariaLabel: 'User directory' },
  { label: 'Audit Log', to: '/admin/audit' },
  { label: 'Search Analytics', to: '/admin/search/analytics', feature: clientSettingKeys.featureSearchAnalytics },
  { label: 'Navigation', to: '/admin/navigation', ariaLabel: 'Navigation menu editor' },
  { label: 'Redirects', to: '/admin/redirects', ariaLabel: 'Redirect management' },
  { label: 'Search Pins', to: '/admin/search/pins' },
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
  // Feature-flagged items (site settings) disappear from the nav while the flag is off.
  const settings = useClientSettings();
  const visibleItems = NAV_ITEMS.filter((item) => !item.feature || settings.getBool(item.feature));

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
