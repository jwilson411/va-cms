/**
 * Tests for issue #43 — Alt text enforcement in MediaDetailPanel.
 *
 * Acceptance criteria verified:
 *   AC1: USWDS Alert shown when image is missing alt text.
 *   AC2: USWDS Alert NOT shown when image has alt text.
 *   AC3: USWDS Alert NOT shown for non-image assets (PDFs).
 *   AC4: Required alt text field shown for images (label + abbr).
 *   AC5: Edit button opens alt text input.
 *   AC6: Save button calls PATCH mutation.
 *   AC7: Cancel button closes edit mode without saving.
 *   AC8: Alt text updated via PATCH /api/v1/media/{id} (useUpdateMediaMetadata called).
 */

import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MediaLibraryPage } from './MediaLibraryPage';
import type { MediaListDto, MediaDetailDto, MediaAssetSummary } from './mediaTypes';

// ── Mock hooks ─────────────────────────────────────────────────────────────────

vi.mock('./useMediaAssets', () => ({
  useMediaAssets:          vi.fn(),
  useMediaDetail:          vi.fn(),
  useUpdateMediaMetadata:  vi.fn(),
}));

import { useMediaAssets, useMediaDetail, useUpdateMediaMetadata } from './useMediaAssets';

const mockUseMediaAssets         = useMediaAssets         as ReturnType<typeof vi.fn>;
const mockUseMediaDetail         = useMediaDetail         as ReturnType<typeof vi.fn>;
const mockUseUpdateMediaMetadata = useUpdateMediaMetadata as ReturnType<typeof vi.fn>;

// ── Fixtures ───────────────────────────────────────────────────────────────────

const mockImageNoAlt: MediaAssetSummary = {
  id:                10,
  fileName:          'banner.jpg',
  storagePath:       'uploads/abc/banner.jpg',
  mimeType:          'image/jpeg',
  fileSizeBytes:     512000,
  altText:           null,
  title:             null,
  width:             1280,
  height:            720,
  webPStoragePath:   null,
  isVirusScanPassed: true,
  createdAt:         '2026-09-10T08:00:00Z',
};

const mockImageWithAlt: MediaAssetSummary = {
  ...mockImageNoAlt,
  id:      11,
  altText: 'A banner image',
};

const mockPdfNoAlt: MediaAssetSummary = {
  id:                12,
  fileName:          'report.pdf',
  storagePath:       'uploads/xyz/report.pdf',
  mimeType:          'application/pdf',
  fileSizeBytes:     204800,
  altText:           null,
  title:             null,
  width:             null,
  height:            null,
  webPStoragePath:   null,
  isVirusScanPassed: true,
  createdAt:         '2026-09-10T08:00:00Z',
};

const buildDetailDto = (asset: MediaAssetSummary): MediaDetailDto => ({
  id:                asset.id,
  fileName:          asset.fileName,
  storagePath:       asset.storagePath,
  storageBackend:    'local',
  mimeType:          asset.mimeType,
  fileSizeBytes:     asset.fileSizeBytes,
  altText:           asset.altText,
  title:             asset.title,
  description:       null,
  tags:              null,
  width:             asset.width,
  height:            asset.height,
  webPStoragePath:   asset.webPStoragePath,
  isVirusScanPassed: asset.isVirusScanPassed,
  uploadedById:      99,
  createdAt:         asset.createdAt,
  updatedAt:         asset.createdAt,
  usages:            [],
});

const mockListData: MediaListDto = {
  items:      [mockImageNoAlt, mockImageWithAlt, mockPdfNoAlt],
  page:       1,
  pageSize:   48,
  totalItems: 3,
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderWithQuery(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const mockMutateAsync = vi.fn().mockResolvedValue(undefined);

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('Issue #43 — Alt text enforcement in MediaDetailPanel', () => {
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
    mockUseUpdateMediaMetadata.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending:   false,
    });
    mockMutateAsync.mockReset().mockResolvedValue(undefined);
  });

  // ── AC1: USWDS Alert shown for image missing alt text ─────────────────────

  it('AC1: shows USWDS alert when image has no alt text', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-10'));

    await waitFor(() => {
      expect(screen.getByTestId('alt-text-required-alert')).toBeDefined();
      expect(screen.getByTestId('alt-text-required-alert').textContent)
        .toContain('This image needs alt text before it can be used in published content');
    });
  });

  // ── AC2: USWDS Alert NOT shown when image has alt text ───────────────────

  it('AC2: does not show alert when image already has alt text', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageWithAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-11'));

    await waitFor(() => screen.getByTestId('media-detail-panel'));

    expect(screen.queryByTestId('alt-text-required-alert')).toBeNull();
  });

  // ── AC3: USWDS Alert NOT shown for PDFs ──────────────────────────────────

  it('AC3: does not show alert for non-image assets (PDF) with no alt text', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockPdfNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-12'));

    await waitFor(() => screen.getByTestId('media-detail-panel'));

    expect(screen.queryByTestId('alt-text-required-alert')).toBeNull();
  });

  // ── AC4: Required alt text field shown for images ────────────────────────

  it('AC4: shows alt text field group for images', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-10'));

    await waitFor(() => {
      expect(screen.getByTestId('alt-text-field-group')).toBeDefined();
    });
  });

  it('AC4: does NOT show alt text field group for PDFs', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockPdfNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-12'));

    await waitFor(() => screen.getByTestId('media-detail-panel'));

    expect(screen.queryByTestId('alt-text-field-group')).toBeNull();
  });

  // ── AC5: Edit button opens alt text input ────────────────────────────────

  it('AC5: clicking Edit button shows alt text input', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-10'));
    await waitFor(() => screen.getByTestId('alt-text-edit-button'));

    fireEvent.click(screen.getByTestId('alt-text-edit-button'));

    await waitFor(() => {
      expect(screen.getByTestId('alt-text-input')).toBeDefined();
    });
  });

  // ── AC6: Save button calls mutation ──────────────────────────────────────

  it('AC6: Save button calls useUpdateMediaMetadata with new alt text', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-10'));
    await waitFor(() => screen.getByTestId('alt-text-edit-button'));

    fireEvent.click(screen.getByTestId('alt-text-edit-button'));
    await waitFor(() => screen.getByTestId('alt-text-input'));

    fireEvent.change(screen.getByTestId('alt-text-input'), {
      target: { value: 'A descriptive banner' },
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('alt-text-save-button'));
    });

    expect(mockMutateAsync).toHaveBeenCalledWith({ altText: 'A descriptive banner' });
  });

  // ── AC7: Cancel button closes edit mode ──────────────────────────────────

  it('AC7: Cancel button closes edit mode without saving', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-10'));
    await waitFor(() => screen.getByTestId('alt-text-edit-button'));

    fireEvent.click(screen.getByTestId('alt-text-edit-button'));
    await waitFor(() => screen.getByTestId('alt-text-input'));

    fireEvent.click(screen.getByTestId('alt-text-cancel-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('alt-text-input')).toBeNull();
    });

    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  // ── AC8: After save, edit mode closes ────────────────────────────────────

  it('AC8: after successful save, edit mode closes', async () => {
    mockUseMediaDetail.mockReturnValue({
      data:      buildDetailDto(mockImageNoAlt),
      isLoading: false,
    });

    renderWithQuery(<MediaLibraryPage />);
    fireEvent.click(screen.getByTestId('media-card-10'));
    await waitFor(() => screen.getByTestId('alt-text-edit-button'));

    fireEvent.click(screen.getByTestId('alt-text-edit-button'));
    await waitFor(() => screen.getByTestId('alt-text-input'));

    fireEvent.change(screen.getByTestId('alt-text-input'), {
      target: { value: 'Fixed alt text' },
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('alt-text-save-button'));
    });

    await waitFor(() => {
      expect(screen.queryByTestId('alt-text-input')).toBeNull();
    });
  });
});
