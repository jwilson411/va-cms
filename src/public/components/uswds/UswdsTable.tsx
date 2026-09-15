/**
 * UswdsTable — USWDS 3.x accessible data table wrapper.
 *
 * Renders a scrollable table with USWDS class names.
 * Column headers get scope="col" for screen-reader compatibility (NFR-A11Y-02).
 *
 * Usage:
 *   <UswdsTable
 *     caption="Index Fragmentation"
 *     headers={['Table', 'Index', 'Fragmentation %', 'Pages']}
 *     rows={data.map(r => [r.tableName, r.indexName, r.fragmentationPct.toFixed(1), r.pageCount])}
 *   />
 */

'use client';

import React from 'react';

export interface UswdsTableProps {
  /** Accessible caption for the table (required for a11y). */
  caption: string;
  /** Column header labels. */
  headers: string[];
  /**
   * Table rows. Each row is an array of cell values (string | number | React.ReactNode).
   * Must be the same length as headers.
   */
  rows: (string | number | React.ReactNode)[][];
  /** Show a scrollable container when table overflows (default: true). */
  scrollable?: boolean;
}

export function UswdsTable({
  caption,
  headers,
  rows,
  scrollable = true,
}: UswdsTableProps): React.ReactElement {
  const table = (
    <table className="usa-table usa-table--borderless width-full">
      <caption className="usa-sr-only">{caption}</caption>
      <thead>
        <tr>
          {headers.map((h) => (
            <th key={h} scope="col">
              {h}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.length === 0 ? (
          <tr>
            <td
              colSpan={headers.length}
              className="text-base-dark font-body-sm padding-2"
              aria-live="polite"
            >
              No data.
            </td>
          </tr>
        ) : (
          rows.map((row, ri) => (
            // eslint-disable-next-line react/no-array-index-key
            <tr key={ri}>
              {row.map((cell, ci) => (
                // eslint-disable-next-line react/no-array-index-key
                <td key={ci}>{cell}</td>
              ))}
            </tr>
          ))
        )}
      </tbody>
    </table>
  );

  if (!scrollable) return table;

  return (
    <div className="usa-table-container--scrollable" tabIndex={0} role="region" aria-label={caption}>
      {table}
    </div>
  );
}
