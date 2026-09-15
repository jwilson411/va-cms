/**
 * SearchPagination — USWDS Pagination component for the /search page.
 *
 * Renders USWDS-compliant pagination nav (usa-pagination).
 * Links are plain <a> tags that preserve the current query string parameters
 * so filters are maintained when navigating pages.
 *
 * BRD FR-SEARCH-01 / Issue #50.
 *
 * Accessibility:
 *   - <nav> with aria-label="Pagination"
 *   - Current page has aria-current="page"
 *   - Previous/Next have accessible text via span.usa-sr-only
 *   - No inline styles; no USWDS focus style overrides.
 */

import React from 'react';

export interface SearchPaginationProps {
  /** Current 1-based page number */
  currentPage: number;
  /** Total number of pages */
  totalPages: number;
  /** Build the href for a given page number (caller provides params) */
  buildPageHref: (page: number) => string;
}

/**
 * Maximum page buttons shown in the pagination bar (excluding prev/next).
 * USWDS guideline: show up to 7 page slots.
 */
const MAX_PAGE_SLOTS = 7;

/**
 * Compute the window of page numbers to display.
 * Returns an array of page numbers, with -1 as a placeholder for ellipsis gaps.
 */
function buildPageWindow(current: number, total: number): number[] {
  if (total <= MAX_PAGE_SLOTS) {
    return Array.from({ length: total }, (_, i) => i + 1);
  }

  // Always show first, last, current ±2, with ellipsis (-1) for gaps
  const pages = new Set<number>([1, total]);
  for (let p = Math.max(2, current - 2); p <= Math.min(total - 1, current + 2); p++) {
    pages.add(p);
  }

  const sorted = Array.from(pages).sort((a, b) => a - b);
  const result: number[] = [];
  for (let i = 0; i < sorted.length; i++) {
    if (i > 0 && sorted[i] - sorted[i - 1] > 1) {
      result.push(-1); // ellipsis gap
    }
    result.push(sorted[i]);
  }
  return result;
}

export function SearchPagination({
  currentPage,
  totalPages,
  buildPageHref,
}: SearchPaginationProps): React.ReactElement | null {
  if (totalPages <= 1) return null;

  const pageWindow = buildPageWindow(currentPage, totalPages);
  const hasPrev = currentPage > 1;
  const hasNext = currentPage < totalPages;

  return (
    <nav aria-label="Pagination" className="usa-pagination">
      <ul className="usa-pagination__list">
        {/* Previous page */}
        {hasPrev ? (
          <li className="usa-pagination__item usa-pagination__arrow">
            <a
              href={buildPageHref(currentPage - 1)}
              className="usa-pagination__link usa-pagination__previous-page"
              aria-label="Previous page"
            >
              <svg
                className="usa-icon"
                aria-hidden="true"
                role="img"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
              >
                <path d="M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z" />
              </svg>
              <span className="usa-pagination__link-text">Previous</span>
            </a>
          </li>
        ) : (
          <li
            className="usa-pagination__item usa-pagination__arrow"
            aria-hidden="true"
          >
            <span
              className="usa-pagination__link usa-pagination__previous-page usa-pagination__link--disabled"
              aria-disabled="true"
            >
              <svg
                className="usa-icon"
                aria-hidden="true"
                role="img"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
              >
                <path d="M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z" />
              </svg>
              <span className="usa-pagination__link-text">Previous</span>
            </span>
          </li>
        )}

        {/* Page numbers */}
        {pageWindow.map((page, idx) => {
          if (page === -1) {
            return (
              <li
                key={`ellipsis-${idx}`}
                className="usa-pagination__item usa-pagination__overflow"
                aria-label="ellipsis indicating non-visible pages"
                role="presentation"
              >
                <span>…</span>
              </li>
            );
          }

          const isCurrent = page === currentPage;
          return (
            <li key={page} className="usa-pagination__item usa-pagination__page-no">
              {isCurrent ? (
                <span
                  className="usa-pagination__button usa-current"
                  aria-current="page"
                  aria-label={`Page ${page}`}
                >
                  {page}
                </span>
              ) : (
                <a
                  href={buildPageHref(page)}
                  className="usa-pagination__button"
                  aria-label={`Page ${page}`}
                >
                  {page}
                </a>
              )}
            </li>
          );
        })}

        {/* Next page */}
        {hasNext ? (
          <li className="usa-pagination__item usa-pagination__arrow">
            <a
              href={buildPageHref(currentPage + 1)}
              className="usa-pagination__link usa-pagination__next-page"
              aria-label="Next page"
            >
              <span className="usa-pagination__link-text">Next</span>
              <svg
                className="usa-icon"
                aria-hidden="true"
                role="img"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
              >
                <path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z" />
              </svg>
            </a>
          </li>
        ) : (
          <li
            className="usa-pagination__item usa-pagination__arrow"
            aria-hidden="true"
          >
            <span
              className="usa-pagination__link usa-pagination__next-page usa-pagination__link--disabled"
              aria-disabled="true"
            >
              <span className="usa-pagination__link-text">Next</span>
              <svg
                className="usa-icon"
                aria-hidden="true"
                role="img"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
              >
                <path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z" />
              </svg>
            </span>
          </li>
        )}
      </ul>
    </nav>
  );
}
