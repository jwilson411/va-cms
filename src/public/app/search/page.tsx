/**
 * app/search/page.tsx — Public search results page.
 *
 * Issue #50 — BRD FR-SEARCH-01
 *
 * Acceptance criteria:
 *   - USWDS Search component in public header (already present via UswdsHeader, issue #47)
 *   - Search results page at /search renders USWDS-compliant result cards
 *   - Filter sidebar: content type, date range, tags
 *   - Pagination on results (USWDS Pagination component)
 *
 * This is a Next.js 14 App Router Server Component. It:
 *   1. Reads searchParams from the URL (q, type, from, to, tag, page)
 *   2. Fetches results from the CMS search API (no-store, server-side)
 *   3. Renders result cards, filter sidebar, and pagination
 *
 * When q is absent, renders the search form with empty state.
 * Errors in the API call are handled gracefully (empty result set shown).
 */

import React from 'react';
import type { Metadata } from 'next';
import { fetchSearchResults, totalPages } from '@/lib/cms/search';
import { SearchResultCard } from '@/components/search/SearchResultCard';
import { SearchFilters } from '@/components/search/SearchFilters';
import { SearchPagination } from '@/components/search/SearchPagination';

export const metadata: Metadata = {
  title: 'Search — Department of Veterans Affairs',
  description: 'Search published VA content',
};

/** Force dynamic rendering — search is never static. */
export const dynamic = 'force-dynamic';

interface SearchPageProps {
  searchParams: {
    q?: string;
    type?: string;
    from?: string;
    to?: string;
    tag?: string;
    page?: string;
  };
}

export default async function SearchPage({
  searchParams,
}: SearchPageProps): Promise<React.ReactElement> {
  const query = (searchParams.q ?? '').trim();
  const typeParam = searchParams.type ? parseInt(searchParams.type, 10) : null;
  const fromParam = searchParams.from ?? null;
  const toParam = searchParams.to ?? null;
  const tagParam = searchParams.tag ? parseInt(searchParams.tag, 10) : null;
  const pageParam = searchParams.page ? Math.max(1, parseInt(searchParams.page, 10)) : 1;

  const PAGE_SIZE = 10;

  const response = query
    ? await fetchSearchResults(query, {
        type: Number.isNaN(typeParam) ? null : typeParam,
        from: fromParam || null,
        to: toParam || null,
        tag: Number.isNaN(tagParam) ? null : tagParam,
        page: pageParam,
        pageSize: PAGE_SIZE,
      })
    : null;

  const numPages = response ? totalPages(response.totalItems, PAGE_SIZE) : 1;

  /** Build href for a page number, preserving all current filters */
  function buildPageHref(page: number): string {
    const params = new URLSearchParams();
    if (query) params.set('q', query);
    if (typeParam != null && !Number.isNaN(typeParam)) params.set('type', String(typeParam));
    if (fromParam) params.set('from', fromParam);
    if (toParam) params.set('to', toParam);
    if (tagParam != null && !Number.isNaN(tagParam)) params.set('tag', String(tagParam));
    params.set('page', String(page));
    return `/search?${params.toString()}`;
  }

  return (
    <div className="grid-container">
      {/* Skip link target */}
      <a className="usa-skipnav" href="#main-search-results">
        Skip to search results
      </a>

      {/* Page heading */}
      <div className="grid-row margin-top-4 margin-bottom-2">
        <div className="tablet:grid-col-12">
          <h1 className="usa-heading">
            {query ? `Search results for "${query}"` : 'Search'}
          </h1>
        </div>
      </div>

      {/* Search bar — allows refining/starting a new search from this page */}
      <div className="grid-row margin-bottom-3">
        <div className="tablet:grid-col-8">
          <form
            action="/search"
            method="get"
            role="search"
            aria-label="Site search"
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
        </div>
      </div>

      {/* Main content area: filter sidebar + results */}
      {query && response ? (
        <div className="grid-row grid-gap" id="main-search-results">
          {/* Filter sidebar */}
          <aside className="tablet:grid-col-3 usa-prose" aria-label="Filter search results">
            <SearchFilters
              query={query}
              contentTypes={[]}
              tags={[]}
              selectedType={searchParams.type}
              selectedFrom={searchParams.from}
              selectedTo={searchParams.to}
              selectedTag={searchParams.tag}
            />
          </aside>

          {/* Results column */}
          <main className="tablet:grid-col-9" id="search-results-list">
            {/* Results count */}
            <p className="usa-prose" role="status" aria-live="polite">
              {response.totalItems === 0 ? (
                <>No results found for <strong>&ldquo;{query}&rdquo;</strong>.</>
              ) : (
                <>
                  Showing {(pageParam - 1) * PAGE_SIZE + 1}–
                  {Math.min(pageParam * PAGE_SIZE, response.totalItems)} of{' '}
                  <strong>{response.totalItems.toLocaleString()}</strong> results for{' '}
                  <strong>&ldquo;{query}&rdquo;</strong>
                </>
              )}
            </p>

            {/* Result cards */}
            {response.items.length > 0 ? (
              <ul className="usa-card-group" aria-label="Search results">
                {response.items.map((item) => (
                  <li key={item.id} className="usa-card__container">
                    <SearchResultCard result={item} />
                  </li>
                ))}
              </ul>
            ) : (
              response.totalItems === 0 && (
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
              )
            )}

            {/* Pagination */}
            {numPages > 1 && (
              <div className="margin-top-4">
                <SearchPagination
                  currentPage={pageParam}
                  totalPages={numPages}
                  buildPageHref={buildPageHref}
                />
              </div>
            )}
          </main>
        </div>
      ) : !query ? (
        /* Empty state: no query */
        <div className="grid-row" id="main-search-results">
          <div className="tablet:grid-col-12">
            <p className="usa-prose">
              Enter a search term above to find VA content.
            </p>
          </div>
        </div>
      ) : null}
    </div>
  );
}
