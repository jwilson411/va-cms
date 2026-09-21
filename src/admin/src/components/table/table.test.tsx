import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { SortableHeader } from './SortableHeader';
import { AdminPagination } from './AdminPagination';
import { RowActions } from './RowActions';

describe('SortableHeader', () => {
  it('renders the USWDS sortable header markup and names the sort button', () => {
    render(
      <table>
        <thead>
          <tr>
            <SortableHeader
              label="Title"
              field="Title"
              currentSortBy="UpdatedAt"
              currentSortDir="DESC"
              onSort={() => undefined}
            />
          </tr>
        </thead>
      </table>,
    );

    const header = screen.getByRole('button', { name: /Sort by Title/i }).closest('th');
    expect(header).toHaveAttribute('data-sortable');
    expect(header).not.toHaveAttribute('aria-sort');
  });

  it('sets aria-sort on the active column and reports the next direction', () => {
    render(
      <table>
        <thead>
          <tr>
            <SortableHeader
              label="Status"
              field="Status"
              currentSortBy="Status"
              currentSortDir="ASC"
              onSort={() => undefined}
            />
          </tr>
        </thead>
      </table>,
    );

    const header = screen.getByRole('button', { name: /Sort by Status/i }).closest('th');
    expect(header).toHaveAttribute('aria-sort', 'ascending');
  });

  it('invokes onSort with the column field', () => {
    const onSort = vi.fn();
    render(
      <table>
        <thead>
          <tr>
            <SortableHeader
              label="Title"
              field="Title"
              currentSortBy="UpdatedAt"
              currentSortDir="DESC"
              onSort={onSort}
            />
          </tr>
        </thead>
      </table>,
    );

    fireEvent.click(screen.getByRole('button', { name: /Sort by Title/i }));
    expect(onSort).toHaveBeenCalledWith('Title');
  });
});

describe('AdminPagination', () => {
  it('renders nothing for a single page', () => {
    const { container } = render(
      <AdminPagination page={1} totalPages={1} onPage={() => undefined} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders USWDS pagination with the current page marked', () => {
    render(<AdminPagination page={2} totalPages={5} onPage={() => undefined} totalRows={120} />);

    expect(screen.getByRole('navigation', { name: /pagination/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: /Previous page/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Next page/i })).toBeInTheDocument();
    expect(screen.getByText(/120 items/)).toBeInTheDocument();
  });

  it('calls onPage with the selected page', () => {
    const onPage = vi.fn();
    render(<AdminPagination page={1} totalPages={3} onPage={onPage} />);

    fireEvent.click(screen.getByRole('button', { name: 'Page 3' }));
    expect(onPage).toHaveBeenCalledWith(3);
  });
});

describe('RowActions', () => {
  it('groups actions in a spacing wrapper', () => {
    const { container } = render(
      <RowActions>
        <button type="button">Edit</button>
        <button type="button">Duplicate</button>
      </RowActions>,
    );

    const group = container.querySelector('.va-cms-row-actions');
    expect(group).not.toBeNull();
    expect(group?.querySelectorAll('button')).toHaveLength(2);
  });
});
