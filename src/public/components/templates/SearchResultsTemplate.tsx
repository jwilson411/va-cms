/**
 * SearchResultsTemplate — USWDS-compliant search results page template.
 *
 * Issue #63 — Wire axe-core Playwright a11y tests for all public templates
 * Epic #14 — Section 508 & Accessibility Hardening
 *
 * Wraps the search results UI in the mandatory USWDS page chrome
 * (Banner, Header, Footer, Identifier) to form a complete, testable
 * page template. The Next.js page at app/search/page.tsx renders its
 * output inside this layout via the root layout.tsx, but this
 * component exists to allow component-level axe-core accessibility
 * testing without running a live Next.js server.
 *
 * This is a Server Component (no 'use client' directive).
 *
 * USWDS classes used:
 *  - usa-search           — search form
 *  - usa-card-group       — result cards
 *  - usa-pagination       — pagination nav
 *  - grid-container       — standard centered max-width container
 */

import React from 'react';
import { UswdsBanner } from '@/components/uswds/UswdsBanner';
import { UswdsHeader, NavItem } from '@/components/uswds/UswdsHeader';
import { UswdsFooter } from '@/components/uswds/UswdsFooter';
import { UswdsIdentifier } from '@/components/uswds/UswdsIdentifier';
import { SearchResultCard } from '@/components/search/SearchResultCard';
import { SearchFilters } from '@/components/search/SearchFilters';
import { SearchPagination } from '@/components/search/SearchPagination';
import type { SearchResultItem } from '@/lib/cms/search';
import type { ContentTypeOption, TagOption } from '@/components/search/SearchFilters';

export interface SearchResultsTemplateProps {
  /** Current search query string (may be empty) */
  query: string;
  /** Search result items to display */
  items: SearchResultItem[];
  /** Total result count across all pages */
  totalItems: number;
  /** Current 1-based page number */
  currentPage: number;
  /** Total pages (for pagination) */
  totalPages: number;
  /** Function to build a page URL given a page number */
  buildPageHref: (page: number) => string;
  /** Available content-type filter options */
  contentTypes: ContentTypeOption[];
  /** Available tag filter options */
  tags: TagOption[];
  /** CMS-managed primary navigation items for the header */
  navigation: NavItem[];
  /** Currently active filter values (for pre-filling the filter form) */
  selectedType?: string;
  selectedFrom?: string;
  selectedTo?: string;
  selectedTag?: string;
}

const PAGE_SIZE = 10;

/**
 * Full-page Search Results layout.
 *
 * Layout structure:
 *   <UswdsBanner />             — top-of-page official gov banner (mandatory)
 *   <UswdsHeader />             — primary nav (mandatory)
 *   <main #main-content>
 *     <grid-container>
 *       <h1>                    — "Search results for…" or "Search"
 *       <form role=search>      — refine/start search
 *       [query present:]
 *         <aside>               — filter sidebar (SearchFilters)
 *         <section>             — result cards + pagination
 *       [no query:]
 *         <p>                   — empty state prompt
 *     </grid-container>
 *   </main>
 *   <UswdsFooter />             — footer (mandatory)
 *   <UswdsIdentifier />         — identifier (mandatory)
 */
export function SearchResultsTemplate({
  query,
  items,
  totalItems,
  currentPage,
  totalPages,
  buildPageHref,
  contentTypes,
  tags,
  navigation,
  selectedType,
  selectedFrom,
  selectedTo,
  selectedTag,
}: SearchResultsTemplateProps): React.ReactElement {
  const startItem = (currentPage - 1) * PAGE_SIZE + 1;
  const endItem = Math.min(currentPage * PAGE_SIZE, totalItems);

  return (
    <>
      {/* Mandatory: Official government banner — top of every public page */}
      <UswdsBanner />

      {/* Mandatory: USWDS extended header with CMS-managed navigation */}
      <UswdsHeader
        siteTitle="Department of Veterans Affairs"
        navigation={navigation}
      />

      <main id="main-content" tabIndex={-1}>
        <div className="grid-container">
          {/* Skip link target for keyboard users */}
          <a className="usa-skipnav" href="#search-results-section">
            Skip to search results
          </a>

          {/* Page heading */}
          <h1 className="usa-heading margin-top-4">
            {query ? `Search results for "${query}"` : 'Search'}
          </h1>

          {/* Search bar — allows refining/starting a search from this page */}
          <form
            action="/search"
            method="get"
            role="search"
            aria-label="Site search"
            className="margin-bottom-3"
          >
            <div className="usa-search usa-search--big">
              <label className="usa-sr-only" htmlFor="search-page-field">
                Search
              </label>
              <input
                className="usa-input"
                id="search-page-field"
                type="search"
                name="q"
                defaultValue={query}
                aria-label="Search the site"
                autoComplete="off"
              />
              <button className="usa-button" type="submit">
                <span className="usa-search__submit-text">Search</span>
              </button>
            </div>
          </form>

          {query ? (
            /* Two-column layout: filter sidebar + results */
            <div className="grid-row grid-gap" id="search-results-section">
              {/* Filter sidebar */}
              <aside
                className="tablet:grid-col-3"
                aria-label="Filter search results"
              >
                <SearchFilters
                  query={query}
                  contentTypes={contentTypes}
                  tags={tags}
                  selectedType={selectedType}
                  selectedFrom={selectedFrom}
                  selectedTo={selectedTo}
                  selectedTag={selectedTag}
                />
              </aside>

              {/* Results column */}
              <section
                className="tablet:grid-col-9"
                aria-label="Search results"
                id="search-results-list"
              >
                {/* Result count — live region */}
                <p
                  className="usa-prose"
                  role="status"
                  aria-live="polite"
                  aria-atomic="true"
                >
                  {totalItems === 0 ? (
                    <>
                      No results found for <strong>&ldquo;{query}&rdquo;</strong>.
                    </>
                  ) : (
                    <>
                      Showing {startItem}–{endItem} of{' '}
                      <strong>{totalItems.toLocaleString()}</strong> results for{' '}
                      <strong>&ldquo;{query}&rdquo;</strong>
                    </>
                  )}
                </p>

                {/* Result cards */}
                {items.length > 0 ? (
                  <ul className="usa-card-group" aria-label="Search results">
                    {items.map((item) => (
                      <li key={item.id} className="usa-card__container">
                        <SearchResultCard result={item} />
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div className="usa-alert usa-alert--info usa-alert--slim margin-top-3">
                    <div className="usa-alert__body">
                      <p className="usa-alert__text">
                        Try different keywords, or{' '}
                        <a href="/" className="usa-link">
                          browse the site
                        </a>{' '}
                        to find what you&rsquo;re looking for.
                      </p>
                    </div>
                  </div>
                )}

                {/* Pagination */}
                {totalPages > 1 && (
                  <div className="margin-top-4">
                    <SearchPagination
                      currentPage={currentPage}
                      totalPages={totalPages}
                      buildPageHref={buildPageHref}
                    />
                  </div>
                )}
              </section>
            </div>
          ) : (
            /* Empty state: no query entered */
            <div className="grid-row" id="search-results-section">
              <div className="tablet:grid-col-12">
                <p className="usa-prose">
                  Enter a search term above to find VA content.
                </p>
              </div>
            </div>
          )}
        </div>
      </main>

      {/* Mandatory: USWDS big footer */}
      <UswdsFooter
        agencyName="Department of Veterans Affairs"
        agencyHref="https://www.va.gov"
      />

      {/* Mandatory: USWDS Identifier — required by 21st Century IDEA Act */}
      <UswdsIdentifier
        agencyName="Department of Veterans Affairs"
        agencyShortName="VA"
        agencyHref="https://www.va.gov"
      />
    </>
  );
}
