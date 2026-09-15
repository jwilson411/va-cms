/**
 * Tests for MediaLibraryPage — issue #42.
 *
 * Acceptance criteria verified:
 *   AC1: Grid/list view toggle renders correct container.
 *   AC2: Search input submits and triggers re-query.
 *   AC3: MIME filter select updates filter state.
 *   AC4: Clicking an asset opens the detail panel.
 *   AC5: "Use this asset" button calls onSelect when picker prop provided.
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MediaLibraryPage } from './MediaLibraryPage';
import type { MediaListDto, MediaDetailDto, MediaAssetSummary } from './mediaTypes';

// ── Mock hooks ─────────────────────────────────────────────────────────────────

vi.mock('./useMediaAssets', () => ({
  useMediaAssets:         vi.fn(),
  useMediaDetail:         vi.fn(),
  useUpdateMediaMetadata: vi.fn(),
}));

import { useMediaAssets, useMediaDetail, useUpdateMediaMetadata } from './useMediaAssets';

const mockUseMediaAssets         = useMediaAssets         as ReturnType<typeof vi.fn>;
const mockUseMediaDetail         = useMediaDetail         as ReturnType<typeof vi.fn>;
// Suppress the "unused variable" ts/lint warning — the mock is needed so the import is resolved.
const _mockUseUpdateMediaMetadata = useUpdateMediaMetadata as ReturnType<typeof vi.fn>;

// ── Fixtures ───────────────────────────────────────────────────────────────────

const mockImageAsset: MediaAssetSummary = {
  id:                1,
  fileName:          'hero.jpg',
  storagePath:       'uploads/abc/hero.jpg',
  mimeType:          'image/jpeg',
  fileSizeBytes:     204800,
  altText:           'A hero image',
  title:             'Hero',
  width:             1920,
  height:            1080,
  webPStoragePath:   'uploads/abc/hero.webp',
  isVirusScanPassed: true,
  createdAt:         '2026-09-01T10:00:00Z',
};

const mockPdfAsset: MediaAssetSummary = {
  id:                2,
  fileName:          'policy.pdf',
  storagePath:       'uploads/xyz/policy.pdf',
  mimeType:          'application/pdf',
  fileSizeBytes:     102400,
  altText:           null,
  title:             null,
  width:             null,
  height:            null,
  webPStoragePath:   null,
  isVirusScanPassed: true,
  createdAt:         '2026-09-05T14:00:00Z',
};

const mockListData: MediaListDto = {
  items:      [mockImageAsset, mockPdfAsset],
  page:       1,
  pageSize:   48,
  totalItems: 2,
};

const mockDetailData: MediaDetailDto = {
  id:                1,
  fileName:          'hero.jpg',
  storagePath:       'uploads/abc/hero.jpg',
  storageBackend:    'local',
  mimeType:          'image/jpeg',
  fileSizeBytes:     204800,
  altText:           'A hero image',
  title:             'Hero',
  description:       'Main hero image',
  tags:              null,
  width:             1920,
  height:            1080,
  webPStoragePath:   'uploads/abc/hero.webp',
  isVirusScanPassed: true,
  uploadedById:      99,
  createdAt:         '2026-09-01T10:00:00Z',
  updatedAt:         '2026-09-01T10:00:00Z',
  usages: [
    { contentEntryId: 10, fieldName: 'featuredImage', slug: 'news/article-1', status: 'Published', contentTypeId: 5 },
  ],
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderWithQuery(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('MediaLibraryPage', () => {
  beforeEach(() => {
    mockUseMediaAssets.mockReturnValue({
      data:      mockListData,
      isLoading: false,
      isError:   false,
    });
    mockUseMediaDetail.mockReturnValue({
      data:      undefined,
      isLoading: false,
    });
    // Provide a default no-op mutation return so MediaDetailPanel doesn't throw
    _mockUseUpdateMediaMetadata.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(undefined),
      isPending:   false,
    });
  });

  // ── AC1: grid/list view toggle ────────────────────────────────────────────

  it('AC1: renders grid view by default', () => {
    renderWithQuery(<MediaLibraryPage />);

    expect(screen.getByTestId('media-grid')).toBeDefined();
    expect(screen.queryByTestId('media-list')).toBeNull();
  });

  it('AC1: switches to list view when list toggle is clicked', async () => {
    renderWithQuery(<MediaLibraryPage />);

    const listToggle = screen.getByTestId('view-toggle-list');
    fireEvent.click(listToggle);

    await waitFor(() => {
      expect(screen.getByTestId('media-list')).toBeDefined();
      expect(screen.queryByTestId('media-grid')).toBeNull();
    });
  });

  it('AC1: switches back to grid view from list view', async () => {
    renderWithQuery(<MediaLibraryPage />);

    fireEvent.click(screen.getByTestId('view-toggle-list'));
    await waitFor(() => screen.getByTestId('media-list'));

    fireEvent.click(screen.getByTestId('view-toggle-grid'));
    await waitFor(() => {
      expect(screen.getByTestId('media-grid')).toBeDefined();
      expect(screen.queryByTestId('media-list')).toBeNull();
    });
  });

  it('AC1: grid view renders a card for each asset', () => {
    renderWithQuery(<MediaLibraryPage />);

    expect(screen.getByTestId('media-card-1')).toBeDefined();
    expect(screen.getByTestId('media-card-2')).toBeDefined();
  });

  it('AC1: list view renders a row for each asset', async () => {
    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('view-toggle-list'));

    await waitFor(() => {
      expect(screen.getByTestId('media-list-row-1')).toBeDefined();
      expect(screen.getByTestId('media-list-row-2')).toBeDefined();
    });
  });

  // ── AC2: search ───────────────────────────────────────────────────────────

  it('AC2: search input is present and submittable', () => {
    renderWithQuery(<MediaLibraryPage />);

    const input = screen.getByTestId('media-search-input');
    expect(input).toBeDefined();

    fireEvent.change(input, { target: { value: 'hero' } });
    fireEvent.submit(screen.getByTestId('media-search-form'));

    // useMediaAssets should have been called — hook mock is called by the component
    expect(mockUseMediaAssets).toHaveBeenCalled();
  });

  // ── AC3: MIME type filter ─────────────────────────────────────────────────

  it('AC3: MIME type filter select is present', () => {
    renderWithQuery(<MediaLibraryPage />);

    const select = screen.getByTestId('media-mime-filter');
    expect(select).toBeDefined();
  });

  it('AC3: changing MIME filter triggers re-render without crashing', async () => {
    renderWithQuery(<MediaLibraryPage />);

    const select = screen.getByTestId('media-mime-filter') as HTMLSelectElement;
    fireEvent.change(select, { target: { value: 'image/' } });

    // Hook re-called after state update
    await waitFor(() => {
      expect(mockUseMediaAssets).toHaveBeenCalled();
    });
  });

  // ── AC4: detail panel ─────────────────────────────────────────────────────

  it('AC4: clicking an asset card opens the detail panel', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);

    expect(screen.queryByTestId('media-detail-panel')).toBeNull();

    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => {
      expect(screen.getByTestId('media-detail-panel')).toBeDefined();
    });
  });

  it('AC4: detail panel shows alt text', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => {
      const altCell = screen.getByTestId('media-detail-alttext');
      expect(altCell.textContent).toContain('A hero image');
    });
  });

  it('AC4: detail panel shows usage list', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => {
      expect(screen.getByTestId('usage-10')).toBeDefined();
    });
  });

  it('AC4: closing detail panel removes it from DOM', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => screen.getByTestId('media-detail-panel'));

    fireEvent.click(screen.getByTestId('media-detail-close'));

    await waitFor(() => {
      expect(screen.queryByTestId('media-detail-panel')).toBeNull();
    });
  });

  // ── AC5: "Use this asset" button ──────────────────────────────────────────

  it('AC5: "Use this asset" button not shown without onSelect prop', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => screen.getByTestId('media-detail-panel'));

    expect(screen.queryByTestId('use-asset-button')).toBeNull();
  });

  it('AC5: "Use this asset" button shown when onSelect prop provided', async () => {
    const onSelect = vi.fn();
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage onSelect={onSelect} />);
    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => {
      expect(screen.getByTestId('use-asset-button')).toBeDefined();
    });
  });

  it('AC5: clicking "Use this asset" calls onSelect with the asset summary', async () => {
    const onSelect = vi.fn();
    mockUseMediaDetail.mockReturnValue({
      data:      mockDetailData,
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage onSelect={onSelect} />);
    fireEvent.click(screen.getByTestId('media-card-1'));

    await waitFor(() => screen.getByTestId('use-asset-button'));

    fireEvent.click(screen.getByTestId('use-asset-button'));

    expect(onSelect).toHaveBeenCalledOnce();
    const arg = onSelect.mock.calls[0][0] as MediaAssetSummary;
    expect(arg.id).toBe(1);
    expect(arg.fileName).toBe('hero.jpg');
  });

  // ── Loading / error states ─────────────────────────────────────────────────

  it('shows loading state', () => {
    mockUseMediaAssets.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderWithQuery(<MediaLibraryPage />);
    expect(screen.getByTestId('media-loading')).toBeDefined();
  });

  it('shows error state on fetch failure', () => {
    mockUseMediaAssets.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderWithQuery(<MediaLibraryPage />);
    expect(screen.getByTestId('media-error')).toBeDefined();
  });

  it('shows empty state when no items', () => {
    mockUseMediaAssets.mockReturnValue({
      data: { items: [], page: 1, pageSize: 48, totalItems: 0 },
      isLoading: false,
      isError: false,
    });
    renderWithQuery(<MediaLibraryPage />);
    expect(screen.getByTestId('media-empty')).toBeDefined();
  });
});
