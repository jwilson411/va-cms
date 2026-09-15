/**
 * Tests for issue #32: VersionHistoryPanel — content versioning UI.
 *
 * AC covered:
 *  - Version list shows: version number, author, date, change note, status
 *  - 'Restore this version' button is rendered per row
 *  - Clicking restore calls the restore mutation
 *  - Restoring creates a new version (does not delete history) — verified via onRestored callback
 *  - Loading state is shown while fetching
 *  - Empty state is shown when there are no versions
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { VersionHistoryPanel } from './VersionHistoryPanel';

// ── Mock useContentVersions + useRestoreVersion ───────────────────────────────

vi.mock('./useContentVersions', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./useContentVersions')>();
  return {
    ...actual,
    useContentVersions: vi.fn(),
    useRestoreVersion: vi.fn(),
  };
});

import { useContentVersions, useRestoreVersion } from './useContentVersions';
const mockUseContentVersions = useContentVersions as ReturnType<typeof vi.fn>;
const mockUseRestoreVersion  = useRestoreVersion  as ReturnType<typeof vi.fn>;

// ── Fixtures ──────────────────────────────────────────────────────────────────

const sampleVersions = [
  {
    id: 10,
    versionNumber: 2,
    authorName: 'Alice Smith',
    createdAt: '2026-09-14T12:00:00Z',
    changeNote: 'Fixed typo in heading',
    status: 'Draft',
  },
  {
    id: 9,
    versionNumber: 1,
    authorName: 'Bob Jones',
    createdAt: '2026-09-13T09:00:00Z',
    changeNote: null,
    status: 'Draft',
  },
];

function makeMutationStub(overrides: Partial<{
  mutate: ReturnType<typeof vi.fn>;
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
}> = {}) {
  return {
    mutate: vi.fn(),
    isPending: false,
    isError: false,
    isSuccess: false,
    error: null,
    ...overrides,
  };
}

function renderPanel(entryId = 42, onRestored?: (id: number) => void) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <VersionHistoryPanel entryId={entryId} onRestored={onRestored} />
    </QueryClientProvider>
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('VersionHistoryPanel', () => {
  beforeEach(() => {
    mockUseRestoreVersion.mockReturnValue(makeMutationStub());
  });

  describe('loading state', () => {
    it('shows loading indicator while fetching', () => {
      mockUseContentVersions.mockReturnValue({ data: undefined, isLoading: true, error: null });

      renderPanel();

      expect(screen.getByText(/loading version history/i)).toBeInTheDocument();
    });
  });

  describe('error state', () => {
    it('shows error message when fetch fails', () => {
      mockUseContentVersions.mockReturnValue({
        data: undefined,
        isLoading: false,
        error: new Error('Network failure'),
      });

      renderPanel();

      expect(screen.getByRole('alert')).toBeInTheDocument();
      expect(screen.getByText(/unable to load version history/i)).toBeInTheDocument();
      expect(screen.getByText(/network failure/i)).toBeInTheDocument();
    });
  });

  describe('empty state', () => {
    it('shows empty message when no versions exist', () => {
      mockUseContentVersions.mockReturnValue({ data: [], isLoading: false, error: null });

      renderPanel();

      expect(screen.getByText(/no version history yet/i)).toBeInTheDocument();
    });
  });

  describe('version list', () => {
    beforeEach(() => {
      mockUseContentVersions.mockReturnValue({
        data: sampleVersions,
        isLoading: false,
        error: null,
      });
    });

    it('renders the version table', () => {
      renderPanel();

      const table = screen.getByTestId('version-history-table');
      expect(table).toBeInTheDocument();
    });

    it('shows a row for each version', () => {
      renderPanel();

      expect(screen.getByTestId('version-row-2')).toBeInTheDocument();
      expect(screen.getByTestId('version-row-1')).toBeInTheDocument();
    });

    it('shows required columns: version number, author, date, change note, status', () => {
      renderPanel();

      // Version numbers
      expect(screen.getByText('2')).toBeInTheDocument();
      expect(screen.getByText('1')).toBeInTheDocument();

      // Authors
      expect(screen.getByText('Alice Smith')).toBeInTheDocument();
      expect(screen.getByText('Bob Jones')).toBeInTheDocument();

      // Change note present for version 2
      expect(screen.getByText('Fixed typo in heading')).toBeInTheDocument();

      // Status
      const statusCells = screen.getAllByText('Draft');
      expect(statusCells.length).toBeGreaterThanOrEqual(2);
    });

    it('renders "Restore this version" button for each row', () => {
      renderPanel();

      const btn2 = screen.getByTestId('restore-btn-2');
      const btn1 = screen.getByTestId('restore-btn-1');
      expect(btn2).toBeInTheDocument();
      expect(btn1).toBeInTheDocument();
    });

    it('calls restore mutation when "Restore this version" is clicked', () => {
      const mockMutate = vi.fn();
      mockUseRestoreVersion.mockReturnValue(makeMutationStub({ mutate: mockMutate }));

      renderPanel();

      fireEvent.click(screen.getByTestId('restore-btn-2'));

      expect(mockMutate).toHaveBeenCalledWith(
        10, // id of version 2
        expect.objectContaining({ onSuccess: expect.any(Function) })
      );
    });

    it('calls onRestored callback with the new version id on success', async () => {
      const onRestored = vi.fn();
      const mockMutate = vi.fn().mockImplementation((_versionId, options) => {
        // Simulate onSuccess being called
        options?.onSuccess?.({ newVersionId: 99 });
      });
      mockUseRestoreVersion.mockReturnValue(makeMutationStub({ mutate: mockMutate }));

      renderPanel(42, onRestored);

      fireEvent.click(screen.getByTestId('restore-btn-2'));

      await waitFor(() => {
        expect(onRestored).toHaveBeenCalledWith(99);
      });
    });

    it('disables restore buttons while a restore is pending', () => {
      mockUseRestoreVersion.mockReturnValue(makeMutationStub({ isPending: true }));

      renderPanel();

      const btn2 = screen.getByTestId('restore-btn-2');
      expect(btn2).toBeDisabled();
    });

    it('shows success message after restore completes', () => {
      mockUseRestoreVersion.mockReturnValue(makeMutationStub({ isSuccess: true }));

      renderPanel();

      expect(screen.getByText(/version restored/i)).toBeInTheDocument();
    });

    it('shows error alert if restore fails', () => {
      mockUseRestoreVersion.mockReturnValue(
        makeMutationStub({ isError: true, error: new Error('Restore failed on server') })
      );

      renderPanel();

      // The alert heading uses the class usa-alert__heading
      expect(screen.getByRole('heading', { name: /restore failed/i })).toBeInTheDocument();
      expect(screen.getByText(/restore failed on server/i)).toBeInTheDocument();
    });
  });
});
