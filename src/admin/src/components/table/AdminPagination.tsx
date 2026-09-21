import React from 'react';
import { Icon } from '../Icon';

export interface AdminPaginationProps {
  page: number;
  totalPages: number;
  onPage: (page: number) => void;
  /** Accessible name for the <nav>; defaults to "Pagination". */
  ariaLabel?: string;
  /** When provided, renders a "Page X of Y (N items)" status line. */
  totalRows?: number;
  /** Noun used in the status line, e.g. "entries". */
  itemLabel?: string;
}

function buildPageNumbers(current: number, total: number): (number | '…')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | '…')[] = [];
  const DELTA = 2;
  const left = current - DELTA;
  const right = current + DELTA;

  pages.push(1);
  if (left > 2) pages.push('…');
  for (let p = Math.max(2, left); p <= Math.min(total - 1, right); p++) pages.push(p);
  if (right < total - 1) pages.push('…');
  pages.push(total);
  return pages;
}

/**
 * Shared USWDS pagination for admin list tables.
 *
 * Uses the design system's `.usa-pagination` markup so every list pages the
 * same way. Renders nothing for a single page of results.
 */
export function AdminPagination({
  page,
  totalPages,
  onPage,
  ariaLabel = 'Pagination',
  totalRows,
  itemLabel = 'items',
}: AdminPaginationProps): JSX.Element | null {
  if (totalPages <= 1) return null;

  return (
    <nav aria-label={ariaLabel} className="usa-pagination margin-top-3">
      <ul className="usa-pagination__list">
        <li className="usa-pagination__item usa-pagination__arrow">
          <button
            type="button"
            className="usa-pagination__link usa-pagination__previous-page"
            aria-label="Previous page"
            disabled={page <= 1}
            onClick={() => onPage(Math.max(1, page - 1))}
          >
            <Icon name="navigate_before" size={3} />
            <span className="usa-pagination__link-text">Previous</span>
          </button>
        </li>

        {buildPageNumbers(page, totalPages).map((p, i) =>
          p === '…' ? (
            <li
              key={`ellipsis-${i}`}
              className="usa-pagination__item usa-pagination__overflow"
              aria-hidden="true"
            >
              <span>…</span>
            </li>
          ) : (
            <li key={p} className="usa-pagination__item usa-pagination__page-no">
              <button
                type="button"
                className={`usa-pagination__button${p === page ? ' usa-current' : ''}`}
                aria-label={`Page ${p}`}
                aria-current={p === page ? 'page' : undefined}
                onClick={() => onPage(p)}
              >
                {p}
              </button>
            </li>
          ),
        )}

        <li className="usa-pagination__item usa-pagination__arrow">
          <button
            type="button"
            className="usa-pagination__link usa-pagination__next-page"
            aria-label="Next page"
            disabled={page >= totalPages}
            onClick={() => onPage(Math.min(totalPages, page + 1))}
          >
            <span className="usa-pagination__link-text">Next</span>
            <Icon name="navigate_next" size={3} />
          </button>
        </li>
      </ul>

      {totalRows != null && (
        <p className="font-body-3xs text-base-dark margin-top-1">
          Page {page} of {totalPages} ({totalRows.toLocaleString()} {itemLabel})
        </p>
      )}
    </nav>
  );
}
