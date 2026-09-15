import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ContentEntryListPage } from './ContentEntryListPage';
import type { ContentEntryAdminPageDto, ContentTypeSummaryForPicker } from './types';

// ── Mock hooks ────────────────────────────────────────────────────────────────

vi.mock('./useContentEntries', () => ({
  useContentEntries:        vi.fn(),
  useContentTypesForPicker: vi.fn(),
}));

import { useContentEntries, useContentTypesForPicker } from './useContentEntries';

const mockUseContentEntries        = useContentEntries        as ReturnType<typeof vi.fn>;
const mockUseContentTypesForPicker = useContentTypesForPicker as ReturnType<typeof vi.fn>;

// ── Fixtures ──────────────────────────────────────────────────────────────────

const mockPage: ContentEntryAdminPageDto = {
  totalRows:  2,
  totalPages: 1,
  page:       1,
  pageSize:   25,
  items: [
    {
      id:                1,
      slug:              'benefits/overview',
      title:             'Benefits Overview',
      contentTypeName:   'Standard Page',
      authorDisplayName: 'Alice Smith',
      status:            'Published',
      lastModified:      '2026-09-01T12:00:00Z',
    },
    {
      id:                2,
      slug:              'hr/policy',
      title:             'HR Policy',
      contentTypeName:   'News Article',
      authorDisplayName: 'Bob Jones',
      status:            'Draft',
      lastModified:      '2026-09-10T08:30:00Z',
    },
  ],
};

const mockTypes: ContentTypeSummaryForPicker[] = [
  { id: 10, name: 'standard_page', displayName: 'Standard Page', description: 'The default page type.' },
  { id: 11, name: 'news_article',  displayName: 'News Article',  description: null },
];

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderWithQuery(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('ContentEntryListPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseContentTypesForPicker.mockReturnValue({
      data: mockTypes, isLoading: false,
    });
  });

  // AC: Loading state
  it('shows loading indicator while fetching', () => {
    mockUseContentEntries.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByText('Loading content entries…')).toBeInTheDocument();
  });

  // AC: Error state
  it('shows error alert on fetch failure', () => {
    mockUseContentEntries.mockReturnValue({
      data:      undefined,
      isLoading: false,
      isError:   true,
      error:     new Error('Network error'),
    });
    renderWithQuery(<ContentEntryListPage />);
    const alert = screen.getByRole('alert');
    expect(alert).toBeInTheDocument();
    expect(within(alert).getByText(/Network error/)).toBeInTheDocument();
  });

  // AC: Empty state
  it('shows no-results message when list is empty', () => {
    mockUseContentEntries.mockReturnValue({
      data:      { ...mockPage, items: [], totalRows: 0, totalPages: 0 },
      isLoading: false,
      isError:   false,
    });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByText('No content entries found.')).toBeInTheDocument();
  });

  // AC: Required columns present
  it('renders Title, Content Type, Author, Status, Last Modified, Actions columns', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    expect(screen.getByRole('button', { name: /Sort by Title/i })).toBeInTheDocument();
    // Table column headers — look inside the table specifically
    const table = screen.getByRole('table');
    expect(within(table).getAllByRole('columnheader').some(
      (th) => th.textContent?.includes('Content Type'),
    )).toBe(true);
    expect(within(table).getAllByRole('columnheader').some(
      (th) => th.textContent?.includes('Author'),
    )).toBe(true);
    expect(screen.getByRole('button', { name: /Sort by Status/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Sort by Last Modified/i })).toBeInTheDocument();
    expect(within(table).getAllByRole('columnheader').some(
      (th) => th.textContent?.includes('Actions'),
    )).toBe(true);
  });

  // AC: Row data rendered
  it('renders row data for each content entry', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    // Use getAllByText for strings that might appear in multiple places (e.g. filter labels + table cells)
    expect(screen.getAllByText('Benefits Overview').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Standard Page').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Alice Smith').length).toBeGreaterThan(0);
    // Status badge inside the table row
    const table = screen.getByRole('table');
    expect(within(table).getAllByText('Published').length).toBeGreaterThan(0);

    expect(screen.getAllByText('HR Policy').length).toBeGreaterThan(0);
    expect(screen.getAllByText('News Article').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Bob Jones').length).toBeGreaterThan(0);
    expect(within(table).getAllByText('Draft').length).toBeGreaterThan(0);
  });

  // AC: Actions column has Edit button per row
  it('renders an Edit action button for each row', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    expect(screen.getByRole('button', { name: /Edit Benefits Overview/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Edit HR Policy/i })).toBeInTheDocument();
  });

  // AC: Filters visible
  it('renders Content Type, Status, Author, and Date Range filters', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    expect(screen.getByRole('combobox', { name: /Filter by content type/i })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /Filter by status/i })).toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: /Search by author name/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/Modified from/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Modified to/i)).toBeInTheDocument();
  });

  // AC: Content type options populated
  it('populates Content Type filter from available types', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    const select = screen.getByRole('combobox', { name: /Filter by content type/i });
    expect(within(select).getByRole('option', { name: 'Standard Page' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'News Article' })).toBeInTheDocument();
  });

  // AC: Status options contain all known statuses
  it('populates Status filter with known status options', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    const select = screen.getByRole('combobox', { name: /Filter by status/i });
    expect(within(select).getByRole('option', { name: 'Draft' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'Published' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'InReview' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'Approved' })).toBeInTheDocument();
  });

  // AC: Clear filters button
  it('renders a clear filters button', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByRole('button', { name: /Clear filters/i })).toBeInTheDocument();
  });

  // AC: Create new button visible
  it('renders a "Create new" button', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByRole('button', { name: /Create new content entry/i })).toBeInTheDocument();
  });

  // AC: Create new opens content type picker
  it('clicking Create new opens the content type picker dialog', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    fireEvent.click(screen.getByRole('button', { name: /Create new content entry/i }));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByRole('heading', { name: /Choose a content type/i })).toBeInTheDocument();
  });

  // AC: Picker shows content type buttons
  it('content type picker shows a button for each type', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    fireEvent.click(screen.getByRole('button', { name: /Create new content entry/i }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /Create new Standard Page/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /Create new News Article/i })).toBeInTheDocument();
  });

  // AC: Picker can be dismissed
  it('Cancel button in picker closes the dialog', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    fireEvent.click(screen.getByRole('button', { name: /Create new content entry/i }));
    fireEvent.click(screen.getByRole('button', { name: /Close content type picker/i }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  // AC: Sort buttons present and aria-sort set correctly
  it('Title column has aria-sort="descending" when sorted by Title DESC', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);

    // Click Title sort (default is UpdatedAt DESC, so clicking Title sets to Title DESC)
    fireEvent.click(screen.getByRole('button', { name: /Sort by Title/i }));

    const titleTh = screen.getByRole('button', { name: /Sort by Title/i }).closest('th');
    expect(titleTh).toHaveAttribute('aria-sort', 'descending');
  });

  // AC: USWDS table class used
  it('renders a USWDS table with usa-table class', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    const { container } = renderWithQuery(<ContentEntryListPage />);
    expect(container.querySelector('table.usa-table')).not.toBeNull();
  });

  // AC: Pagination shown when multiple pages
  it('shows pagination when totalPages > 1', () => {
    mockUseContentEntries.mockReturnValue({
      data:      { ...mockPage, totalRows: 50, totalPages: 2 },
      isLoading: false,
      isError:   false,
    });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByRole('navigation', { name: /Pagination/i })).toBeInTheDocument();
  });

  // AC: Pagination hidden for single page
  it('does not show pagination on a single page', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.queryByRole('navigation', { name: /Pagination/i })).toBeNull();
  });

  // AC: Previous/Next buttons present in pagination
  it('pagination has Previous and Next buttons', () => {
    mockUseContentEntries.mockReturnValue({
      data:      { ...mockPage, totalRows: 50, totalPages: 2 },
      isLoading: false,
      isError:   false,
    });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByRole('button', { name: /Previous page/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Next page/i })).toBeInTheDocument();
  });

  // AC: heading visible
  it('renders the Content Entries heading', () => {
    mockUseContentEntries.mockReturnValue({ data: mockPage, isLoading: false, isError: false });
    renderWithQuery(<ContentEntryListPage />);
    expect(screen.getByRole('heading', { name: /Content Entries/i })).toBeInTheDocument();
  });
});
