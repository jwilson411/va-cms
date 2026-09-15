/**
 * SearchFilters — USWDS-compliant filter sidebar for the /search page.
 *
 * Renders a filter form with:
 *   - Content type dropdown
 *   - Date range (from / to) date inputs
 *   - Tag select (taxonomy term ID)
 *
 * This is a client component — the form submits GET to /search so it
 * works without JavaScript (progressive enhancement).
 *
 * BRD FR-SEARCH-01 / Issue #50.
 *
 * Accessibility:
 *   - Each input has a <label> with htmlFor.
 *   - Error messages (if any) would use aria-describedby.
 *   - No inline styles; no USWDS focus style overrides.
 */

'use client';

import React from 'react';

export interface ContentTypeOption {
  id: number;
  name: string;
}

export interface TagOption {
  id: number;
  name: string;
}

export interface SearchFiltersProps {
  /** Current search query — preserved in hidden input on submit */
  query: string;
  /** Available content types for the dropdown */
  contentTypes: ContentTypeOption[];
  /** Available taxonomy terms for the tags dropdown */
  tags: TagOption[];
  /** Currently selected content type ID (string form for input value) */
  selectedType?: string;
  /** Currently selected from-date (ISO date string YYYY-MM-DD) */
  selectedFrom?: string;
  /** Currently selected to-date (ISO date string YYYY-MM-DD) */
  selectedTo?: string;
  /** Currently selected tag term ID (string form for input value) */
  selectedTag?: string;
}

export function SearchFilters({
  query,
  contentTypes,
  tags,
  selectedType,
  selectedFrom,
  selectedTo,
  selectedTag,
}: SearchFiltersProps): React.ReactElement {
  return (
    <aside
      className="usa-prose"
      aria-label="Search filters"
    >
      <form action="/search" method="get">
        {/* Preserve the current search query */}
        <input type="hidden" name="q" value={query} />

        <fieldset className="usa-fieldset">
          <legend className="usa-legend usa-legend--large">Filter results</legend>

          {/* Content type filter */}
          {contentTypes.length > 0 && (
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-type">
                Content type
              </label>
              <select
                className="usa-select"
                id="filter-type"
                name="type"
                defaultValue={selectedType ?? ''}
              >
                <option value="">All types</option>
                {contentTypes.map((ct) => (
                  <option key={ct.id} value={String(ct.id)}>
                    {ct.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Date range: from */}
          <div className="usa-form-group">
            <label className="usa-label" htmlFor="filter-from">
              Published on or after
            </label>
            <div className="usa-date-picker usa-date-picker--initialized" id="filter-from-wrapper">
              <input
                className="usa-input"
                id="filter-from"
                name="from"
                type="date"
                defaultValue={selectedFrom ?? ''}
                aria-label="Published on or after date"
              />
            </div>
          </div>

          {/* Date range: to */}
          <div className="usa-form-group">
            <label className="usa-label" htmlFor="filter-to">
              Published on or before
            </label>
            <div className="usa-date-picker usa-date-picker--initialized" id="filter-to-wrapper">
              <input
                className="usa-input"
                id="filter-to"
                name="to"
                type="date"
                defaultValue={selectedTo ?? ''}
                aria-label="Published on or before date"
              />
            </div>
          </div>

          {/* Tag filter */}
          {tags.length > 0 && (
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-tag">
                Topic / tag
              </label>
              <select
                className="usa-select"
                id="filter-tag"
                name="tag"
                defaultValue={selectedTag ?? ''}
              >
                <option value="">All topics</option>
                {tags.map((t) => (
                  <option key={t.id} value={String(t.id)}>
                    {t.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="usa-form-group">
            <button type="submit" className="usa-button margin-top-2">
              Apply filters
            </button>
            <a
              href={`/search?q=${encodeURIComponent(query)}`}
              className="usa-button usa-button--unstyled margin-left-2"
            >
              Clear filters
            </a>
          </div>
        </fieldset>
      </form>
    </aside>
  );
}
