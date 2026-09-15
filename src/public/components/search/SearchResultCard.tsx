/**
 * SearchResultCard — USWDS-compliant result card for the /search page.
 *
 * Renders a single search result as a USWDS card component.
 * BRD FR-SEARCH-01 / Issue #50.
 *
 * Accessibility:
 *   - Card heading is an <h2> linked to the result URL.
 *   - Excerpt is rendered as a paragraph.
 *   - Content type and published date are labelled with aria-label on <span>.
 *   - No inline styles; no USWDS focus style overrides.
 */

import React from 'react';
import { searchResultHref, formatPublishedAt } from '@/lib/cms/search';
import type { SearchResultItem } from '@/lib/cms/search';

export interface SearchResultCardProps {
  result: SearchResultItem;
}

export function SearchResultCard({ result }: SearchResultCardProps): React.ReactElement {
  const href = searchResultHref(result.slug);
  const publishedDate = formatPublishedAt(result.publishedAt);

  return (
    <div className="usa-card usa-card--flag">
      <div className="usa-card__container">
        <div className="usa-card__header">
          <h2 className="usa-card__heading">
            <a
              href={href}
              className="usa-link"
            >
              {result.title ?? result.slug}
            </a>
          </h2>
        </div>
        {result.excerpt && (
          <div className="usa-card__body">
            <p className="usa-card__text">{result.excerpt}</p>
          </div>
        )}
        <div className="usa-card__footer">
          {result.contentTypeName && (
            <span
              className="usa-tag"
              aria-label={`Content type: ${result.contentTypeName}`}
            >
              {result.contentTypeName}
            </span>
          )}
          {publishedDate && (
            <span
              className="usa-hint margin-left-1"
              aria-label={`Published: ${publishedDate}`}
            >
              {publishedDate}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}
