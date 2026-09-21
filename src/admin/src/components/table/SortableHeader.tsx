import React from 'react';
import type { SortDirection } from './useSortableRows';

export interface SortableHeaderProps<K extends string> {
  /** Visible column label, e.g. "Last Modified". */
  label: string;
  /** Column key this header sorts by. */
  field: K;
  currentSortBy: K;
  currentSortDir: SortDirection;
  onSort: (field: K) => void;
}

/**
 * USWDS sortable table header.
 *
 * Follows the markup the design system documents for sortable tables
 * (https://designsystem.digital.gov/components/table/#sortable-table-rows):
 * `th[data-sortable]` carries `aria-sort`, and the sort control is an
 * icon-only `.usa-table__header__button` that USWDS styles (the arrows are the
 * three `usa-icon` groups the framework toggles by `aria-sort`).
 *
 * We drive the sort from React rather than the USWDS JS so the same component
 * works for both server-sorted and client-sorted tables.
 */
export function SortableHeader<K extends string>({
  label,
  field,
  currentSortBy,
  currentSortDir,
  onSort,
}: SortableHeaderProps<K>): JSX.Element {
  const isActive = currentSortBy === field;
  const ariaSort = isActive
    ? currentSortDir === 'ASC'
      ? 'ascending'
      : 'descending'
    : undefined;
  const nextDirection = isActive && currentSortDir === 'ASC' ? 'descending' : 'ascending';

  return (
    <th data-sortable scope="col" role="columnheader" aria-sort={ariaSort}>
      {label}
      <button
        type="button"
        className="usa-table__header__button"
        title={`Sort by ${label} (${nextDirection}).`}
        onClick={() => onSort(field)}
      >
        <svg
          className="usa-icon"
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 24 24"
          aria-hidden="true"
          focusable="false"
        >
          <g className="descending" fill="transparent">
            <path d="M17 17L15.59 15.59L12.9999 18.17V2H10.9999V18.17L8.41 15.58L7 17L11.9999 22L17 17Z" />
          </g>
          <g className="ascending" fill="transparent">
            <path
              transform="rotate(180, 12, 12)"
              d="M17 17L15.59 15.59L12.9999 18.17V2H10.9999V18.17L8.41 15.58L7 17L11.9999 22L17 17Z"
            />
          </g>
          <g className="unsorted" fill="transparent">
            <polygon points="15.17 15 13 17.17 13 6.83 15.17 9 16.58 7.59 12 3 7.41 7.59 8.83 9 11 6.83 11 17.17 8.83 15 7.42 16.41 12 21 16.59 16.41 15.17 15" />
          </g>
        </svg>
      </button>
    </th>
  );
}
