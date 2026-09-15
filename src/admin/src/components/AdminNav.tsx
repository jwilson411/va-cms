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

interface NavItem {
  label: string;
  to: string;
  /** aria-label override when the visible label alone is ambiguous. */
  ariaLabel?: string;
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', to: '/admin' },
  { label: 'Content', to: '/admin/content', ariaLabel: 'Content entries' },
  { label: 'Media', to: '/admin/media', ariaLabel: 'Media library' },
  { label: 'Content Types', to: '/admin/content-types' },
  { label: 'Users', to: '/admin/users', ariaLabel: 'User directory' },
  { label: 'Audit Log', to: '/admin/audit' },
  { label: 'Search Analytics', to: '/admin/search/analytics' },
  { label: 'Navigation', to: '/admin/navigation', ariaLabel: 'Navigation menu editor' },
];

/**
 * Admin sidebar navigation.
 *
 * Keyboard users can Tab to the skip-link at the top (visible on focus)
 * then activate it to jump directly to `#main-content`, bypassing repeated
 * navigation on every page.
 */
export function AdminNav(): JSX.Element {
  return (
    <>
      {/*
       * Skip navigation link — visually hidden until focused.
       * Targets `id="main-content"` which every admin page sets on its <main>.
       * This is the first focusable element in the SPA shell.
       */}
      <a
        href="#main-content"
        className="usa-skipnav"
        data-testid="skip-nav-link"
      >
        Skip to main content
      </a>

      <nav
        aria-label="Admin navigation"
        className="usa-sidenav"
        data-testid="admin-nav"
      >
        <ul className="usa-sidenav__list">
          {NAV_ITEMS.map(({ label, to, ariaLabel }) => (
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
