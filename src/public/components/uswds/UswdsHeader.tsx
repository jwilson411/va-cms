/**
 * UswdsHeader — USWDS extended header for VA public pages.
 *
 * Renders the usa-header--extended variant with primary navigation.
 * Accepts a typed navigation prop to allow content-driven menus.
 *
 * BRD NFR-A11Y-02: must come from @uswds/uswds, not rewritten.
 */

'use client';

import React, { useState } from 'react';

export interface NavItem {
  label: string;
  href: string;
  children?: NavItem[];
}

export interface UswdsHeaderProps {
  /** Site title shown in the header masthead */
  siteTitle: string;
  /** Optional URL for the site title link. Defaults to '/'. */
  siteTitleHref?: string;
  /** Navigation items for the primary menu */
  navigation: NavItem[];
  /** Optional: override the search action URL. Defaults to '/search'. */
  searchAction?: string;
  /** Render the search form. Driven by the features.publicSearch site setting. Defaults to true. */
  showSearch?: boolean;
}

export function UswdsHeader({
  siteTitle,
  siteTitleHref = '/',
  navigation,
  searchAction = '/search',
  showSearch = true,
}: UswdsHeaderProps): React.ReactElement {
  const [menuOpen, setMenuOpen] = useState(false);
  const [openDropdown, setOpenDropdown] = useState<string | null>(null);

  const handleDropdownToggle = (label: string): void => {
    setOpenDropdown((prev) => (prev === label ? null : label));
  };

  return (
    <header className="usa-header usa-header--extended" role="banner">
      <div className="usa-navbar">
        <div className="usa-logo">
          <em className="usa-logo__text">
            <a href={siteTitleHref} title={`Home — ${siteTitle}`}>
              {siteTitle}
            </a>
          </em>
        </div>
        <button
          type="button"
          className="usa-menu-btn"
          aria-expanded={menuOpen}
          aria-controls="basic-mega-nav"
          onClick={() => setMenuOpen((prev) => !prev)}
        >
          Menu
        </button>
      </div>

      <nav
        aria-label="Primary navigation"
        className="usa-nav"
        id="basic-mega-nav"
        hidden={!menuOpen && typeof window !== 'undefined' && window.innerWidth < 1024}
      >
        <div className="usa-nav__inner">
          <button
            type="button"
            className="usa-nav__close"
            onClick={() => setMenuOpen(false)}
          >
            <span aria-hidden="true">✕</span>
            <span className="usa-sr-only">Close</span>
          </button>

          {showSearch && (
          <div className="usa-search usa-search--small" role="search">
            <form action={searchAction} method="get">
              <label className="usa-sr-only" htmlFor="extended-search-field-small">
                Search
              </label>
              <input
                className="usa-input"
                id="extended-search-field-small"
                type="search"
                name="q"
                aria-label="Search"
              />
              <button className="usa-button" type="submit">
                <span className="usa-sr-only">Search</span>
                <svg
                  className="usa-icon"
                  aria-hidden="true"
                  focusable="false"
                  role="img"
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 24 24"
                >
                  <path d="M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z" />
                </svg>
              </button>
            </form>
          </div>
          )}

          <ul className="usa-nav__primary usa-accordion">
            {navigation.map((item) => (
              <li
                key={item.label}
                className={`usa-nav__primary-item${item.children?.length ? ' usa-nav__submenu-list' : ''}`}
              >
                {item.children && item.children.length > 0 ? (
                  <>
                    <button
                      type="button"
                      className="usa-accordion__button usa-nav__link"
                      aria-expanded={openDropdown === item.label}
                      aria-controls={`nav-dropdown-${item.label.toLowerCase().replace(/\s+/g, '-')}`}
                      onClick={() => handleDropdownToggle(item.label)}
                    >
                      <span>{item.label}</span>
                    </button>
                    <ul
                      id={`nav-dropdown-${item.label.toLowerCase().replace(/\s+/g, '-')}`}
                      className="usa-nav__submenu"
                      hidden={openDropdown !== item.label}
                    >
                      {item.children.map((child) => (
                        <li key={child.label} className="usa-nav__submenu-item">
                          <a href={child.href} className="usa-nav__link">
                            {child.label}
                          </a>
                        </li>
                      ))}
                    </ul>
                  </>
                ) : (
                  <a href={item.href} className="usa-nav__link">
                    <span>{item.label}</span>
                  </a>
                )}
              </li>
            ))}
          </ul>
        </div>
      </nav>
    </header>
  );
}
