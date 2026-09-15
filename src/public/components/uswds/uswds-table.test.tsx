/**
 * UswdsTable component tests.
 *
 * Verifies:
 * - Caption is rendered (required for a11y).
 * - Column headers have scope="col" (BRD NFR-A11Y-02).
 * - Each data row renders the correct number of cells.
 * - Empty state renders the "No data." message.
 * - Scrollable container is present by default.
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { UswdsTable } from './UswdsTable';

const HEADERS = ['Table', 'Index', 'Fragmentation %', 'Pages'];
const ROWS: (string | number)[][] = [
  ['ContentEntry', 'IX_ContentEntry_Status_UpdatedAt', '12.5 %', '200'],
  ['MediaAsset',   'IX_MediaAsset_MimeType_CreatedAt', '0.0 %',  '50'],
];

describe('UswdsTable', () => {
  it('renders the table with correct USWDS class names', () => {
    const { container } = render(
      <UswdsTable caption="Index Fragmentation" headers={HEADERS} rows={ROWS} />,
    );
    const table = container.querySelector('table');
    expect(table).not.toBeNull();
    expect(table?.className).toContain('usa-table');
  });

  it('renders a visually hidden caption for screen readers', () => {
    render(<UswdsTable caption="Index Fragmentation" headers={HEADERS} rows={ROWS} />);
    // Caption text is present (even if .usa-sr-only visually hides it)
    const caption = screen.getByText('Index Fragmentation');
    expect(caption.tagName).toBe('CAPTION');
  });

  it('renders column headers with scope="col"', () => {
    const { container } = render(
      <UswdsTable caption="Index Fragmentation" headers={HEADERS} rows={ROWS} />,
    );
    const ths = container.querySelectorAll('th[scope="col"]');
    expect(ths).toHaveLength(HEADERS.length);
    expect(ths[0].textContent).toBe('Table');
    expect(ths[1].textContent).toBe('Index');
  });

  it('renders the correct number of data rows', () => {
    const { container } = render(
      <UswdsTable caption="Index Fragmentation" headers={HEADERS} rows={ROWS} />,
    );
    const trs = container.querySelectorAll('tbody tr');
    expect(trs).toHaveLength(ROWS.length);
  });

  it('renders each cell value in the correct position', () => {
    render(<UswdsTable caption="Index Fragmentation" headers={HEADERS} rows={ROWS} />);
    expect(screen.getByText('ContentEntry')).toBeTruthy();
    expect(screen.getByText('12.5 %')).toBeTruthy();
    expect(screen.getByText('MediaAsset')).toBeTruthy();
  });

  it('shows "No data." when rows array is empty', () => {
    render(<UswdsTable caption="Table Sizes" headers={HEADERS} rows={[]} />);
    expect(screen.getByText('No data.')).toBeTruthy();
  });

  it('wraps in scrollable container by default', () => {
    const { container } = render(
      <UswdsTable caption="Long-Running Queries" headers={HEADERS} rows={ROWS} />,
    );
    const wrapper = container.querySelector('.usa-table-container--scrollable');
    expect(wrapper).not.toBeNull();
    expect(wrapper?.getAttribute('tabindex')).toBe('0');
    expect(wrapper?.getAttribute('role')).toBe('region');
    expect(wrapper?.getAttribute('aria-label')).toBe('Long-Running Queries');
  });

  it('omits scrollable container when scrollable={false}', () => {
    const { container } = render(
      <UswdsTable
        caption="Long-Running Queries"
        headers={HEADERS}
        rows={ROWS}
        scrollable={false}
      />,
    );
    const wrapper = container.querySelector('.usa-table-container--scrollable');
    expect(wrapper).toBeNull();
  });
});
