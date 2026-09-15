import React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ContentEntryListPage } from './ContentEntryListPage';
import type { ContentEntryAdminPageDto, ContentTypeSummaryForPicker } from './types';

// ── Mock hooks ────────────────────────────────────────────────────────────────

vi.mock('./useContentEntries', () => ({
  useContentEntries:        vi.fn(),
  useContentTypesForPicker: vi.fn(),
  useDuplicateEntry:        vi.fn(),
}));

import {
  useContentEntries,
  useContentTypesForPicker,
  useDuplicateEntry,
} from './useContentEntries';

const mockUseContentEntries        = useContentEntries        as ReturnType<typeof vi.fn>;
const mockUseContentTypesForPicker = useContentTypesForPicker as ReturnType<typeof vi.fn>;
const mockUseDuplicateEntry        = useDuplicateEntry        as ReturnType<typeof vi.fn>;

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

describe('ContentEntryListPage — Duplicate action (issue #36)', () => {
  let mockMutate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.clearAllMocks();
    mockMutate = vi.fn();
    mockUseContentTypesForPicker.mockReturnValue({
      data: mockTypes, isLoading: false,
    });
    mockUseContentEntries.mockReturnValue({
      data: mockPage, isLoading: false, isError: false,
    });
    mockUseDuplicateEntry.mockReturnValue({
      mutate:    mockMutate,
      isPending: false,
      isError:   false,
    });
  });

  // AC: Duplicate button rendered for each row
  it('renders a Duplicate button for each content entry row', () => {
    renderWithQuery(<ContentEntryListPage />);

    expect(screen.getByRole('button', { name: /Duplicate Benefits Overview/i }))
      .toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Duplicate HR Policy/i }))
      .toBeInTheDocument();
  });

  // AC: Clicking Duplicate calls the mutation with the correct entry id
  it('clicking Duplicate button calls useDuplicateEntry.mutate with the entry id', () => {
    renderWithQuery(<ContentEntryListPage />);

    fireEvent.click(screen.getByRole('button', { name: /Duplicate Benefits Overview/i }));

    expect(mockMutate).toHaveBeenCalledTimes(1);
    expect(mockMutate).toHaveBeenCalledWith(
      1, // entry id
      expect.objectContaining({ onError: expect.any(Function) }),
    );
  });

  // AC: Duplicate button is disabled while mutation is pending
  it('Duplicate buttons are disabled when a duplicate mutation is in progress', () => {
    mockUseDuplicateEntry.mockReturnValue({
      mutate:    mockMutate,
      isPending: true,
      isError:   false,
    });

    renderWithQuery(<ContentEntryListPage />);

    expect(screen.getByRole('button', { name: /Duplicate Benefits Overview/i }))
      .toBeDisabled();
    expect(screen.getByRole('button', { name: /Duplicate HR Policy/i }))
      .toBeDisabled();
  });

  // AC: Error from mutation displayed as alert
  it('shows a "Duplicate failed" error alert when onError callback fires', () => {
    // Set up mutate to immediately invoke the onError callback
    mockMutate.mockImplementation((_id: number, callbacks: { onError?: (err: Error) => void }) => {
      callbacks?.onError?.(new Error('Source entry not found.'));
    });

    renderWithQuery(<ContentEntryListPage />);

    fireEvent.click(screen.getByRole('button', { name: /Duplicate Benefits Overview/i }));

    const alert = screen.getByRole('alert');
    expect(alert).toBeInTheDocument();
    expect(within(alert).getByText(/Duplicate failed/i)).toBeInTheDocument();
    expect(within(alert).getByText(/Source entry not found/i)).toBeInTheDocument();
  });

  // AC: Duplicate action does not affect the Edit button
  it('Edit button is still rendered alongside the Duplicate button', () => {
    renderWithQuery(<ContentEntryListPage />);

    expect(screen.getByRole('button', { name: /Edit Benefits Overview/i }))
      .toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Duplicate Benefits Overview/i }))
      .toBeInTheDocument();
  });
});
