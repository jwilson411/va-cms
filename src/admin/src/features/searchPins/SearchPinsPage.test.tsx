/**
 * Tests for SearchPinsPage — issue #52 (FR-SEARCH-04).
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { SearchPinsPage } from './SearchPinsPage';
import * as hooks from './useSearchPins';

// ── mock the hooks ────────────────────────────────────────────────────────────

vi.mock('./useSearchPins');

const mockUseSearchPins    = vi.mocked(hooks.useSearchPins);
const mockUseCreatePin     = vi.mocked(hooks.useCreateSearchPin);
const mockUseDeletePin     = vi.mocked(hooks.useDeleteSearchPin);

// ── helpers ───────────────────────────────────────────────────────────────────

function buildPin(overrides: Partial<hooks.SearchPinItem> = {}): hooks.SearchPinItem {
  return {
    id:                 1,
    queryString:        'va benefits',
    contentEntryId:     42,
    createdAt:          '2026-09-01T00:00:00Z',
    entrySlug:          'benefits/overview',
    entryStatus:        'Published',
    entryContentTypeId: 1,
    entryTitle:         'VA Benefits Overview',
    ...overrides,
  };
}

function mockCreate(
  opts: { isPending?: boolean; isError?: boolean } = {},
) {
  const mutateMock = vi.fn();
  mockUseCreatePin.mockReturnValue({
    mutate: mutateMock,
    isPending: opts.isPending ?? false,
  } as unknown as ReturnType<typeof hooks.useCreateSearchPin>);
  return mutateMock;
}

function mockDelete() {
  const mutateMock = vi.fn();
  mockUseDeletePin.mockReturnValue({
    mutate: mutateMock,
    isPending: false,
  } as unknown as ReturnType<typeof hooks.useDeleteSearchPin>);
  return mutateMock;
}

// ── tests ─────────────────────────────────────────────────────────────────────

describe('SearchPinsPage', () => {
  beforeEach(() => {
    // Default: empty list, idle mutations
    mockUseSearchPins.mockReturnValue({
      data: { items: [] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);
    mockCreate();
    mockDelete();
  });

  // ── Loading state ──────────────────────────────────────────────────────────

  it('shows loading indicator while fetching', () => {
    mockUseSearchPins.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    expect(screen.getByText(/Loading pinned results/i)).toBeTruthy();
  });

  // ── Error state ────────────────────────────────────────────────────────────

  it('shows error alert when fetch fails', () => {
    mockUseSearchPins.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText(/Failed to load pinned results/i)).toBeTruthy();
  });

  // ── Empty state ────────────────────────────────────────────────────────────

  it('shows empty-state message when there are no pins', () => {
    render(<SearchPinsPage />);
    expect(screen.getByText(/No pinned results yet/i)).toBeTruthy();
  });

  // ── Table rendering ────────────────────────────────────────────────────────

  it('renders a row for each pin', () => {
    mockUseSearchPins.mockReturnValue({
      data: { items: [buildPin({ id: 1, queryString: 'pin one' }), buildPin({ id: 2, queryString: 'pin two' })] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    expect(screen.getByText('pin one')).toBeTruthy();
    expect(screen.getByText('pin two')).toBeTruthy();
  });

  it('renders entry title and slug in the row', () => {
    mockUseSearchPins.mockReturnValue({
      data: {
        items: [buildPin({ entryTitle: 'My Entry', entrySlug: 'my/entry' })],
      },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    expect(screen.getByText('My Entry')).toBeTruthy();
    expect(screen.getByText('/my/entry')).toBeTruthy();
  });

  it('renders "Untitled" when entry title is null', () => {
    mockUseSearchPins.mockReturnValue({
      data: { items: [buildPin({ entryTitle: null })] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    expect(screen.getByText('Untitled')).toBeTruthy();
  });

  // ── Add pin form ──────────────────────────────────────────────────────────

  it('renders add-pin form with labelled inputs', () => {
    render(<SearchPinsPage />);
    expect(screen.getByLabelText(/Query string/i)).toBeTruthy();
    expect(screen.getByLabelText(/Content entry ID/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /Add pin/i })).toBeTruthy();
  });

  it('shows validation error when query string is empty on submit', () => {
    render(<SearchPinsPage />);
    const form = screen.getByRole('button', { name: /Add pin/i });
    fireEvent.click(form);
    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText(/Query string is required/i)).toBeTruthy();
  });

  it('shows validation error when content entry ID is invalid on submit', () => {
    render(<SearchPinsPage />);
    fireEvent.change(screen.getByLabelText(/Query string/i), { target: { value: 'test query' } });
    fireEvent.change(screen.getByLabelText(/Content entry ID/i), { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: /Add pin/i }));
    expect(screen.getByText(/Content entry ID must be a positive number/i)).toBeTruthy();
  });

  it('calls createPin.mutate with trimmed query and numeric entryId on valid submit', () => {
    const mutateMock = mockCreate();
    render(<SearchPinsPage />);

    fireEvent.change(screen.getByLabelText(/Query string/i), { target: { value: '  va benefits  ' } });
    fireEvent.change(screen.getByLabelText(/Content entry ID/i), { target: { value: '42' } });
    fireEvent.click(screen.getByRole('button', { name: /Add pin/i }));

    expect(mutateMock).toHaveBeenCalledWith(
      { queryString: 'va benefits', contentEntryId: 42 },
      expect.any(Object),
    );
  });

  it('disables the submit button while create is pending', () => {
    mockCreate({ isPending: true });
    render(<SearchPinsPage />);
    const btn = screen.getByRole('button', { name: /Saving/i });
    expect(btn).toBeTruthy();
    expect((btn as HTMLButtonElement).disabled).toBe(true);
  });

  // ── Delete flow ───────────────────────────────────────────────────────────

  it('shows confirmation dialog when Remove is clicked', () => {
    mockUseSearchPins.mockReturnValue({
      data: { items: [buildPin()] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    fireEvent.click(screen.getByRole('button', { name: /Remove pin for query/i }));
    expect(screen.getByRole('alertdialog')).toBeTruthy();
    expect(screen.getByText(/Are you sure/i)).toBeTruthy();
  });

  it('calls deletePin.mutate when Remove is confirmed', () => {
    const deleteMock = mockDelete();
    mockUseSearchPins.mockReturnValue({
      data: { items: [buildPin({ id: 7 })] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    fireEvent.click(screen.getByRole('button', { name: /Remove pin for query/i }));
    fireEvent.click(screen.getByRole('button', { name: /Confirm remove/i }));

    expect(deleteMock).toHaveBeenCalledWith(7, expect.any(Object));
  });

  it('dismisses confirmation dialog on Cancel', () => {
    mockUseSearchPins.mockReturnValue({
      data: { items: [buildPin()] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchPins>);

    render(<SearchPinsPage />);
    fireEvent.click(screen.getByRole('button', { name: /Remove pin for query/i }));
    expect(screen.getByRole('alertdialog')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });
});

// ── useSearchPins hook types ──────────────────────────────────────────────────

describe('useSearchPins hook types', () => {
  it('SearchPinItem has all required fields', () => {
    const pin: hooks.SearchPinItem = {
      id:                 1,
      queryString:        'q',
      contentEntryId:     1,
      createdAt:          '2026-01-01T00:00:00Z',
      entrySlug:          null,
      entryStatus:        null,
      entryContentTypeId: null,
      entryTitle:         null,
    };
    expect(pin.id).toBe(1);
    expect(pin.queryString).toBe('q');
  });
});
