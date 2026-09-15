/**
 * Tests for issue #44 — Media usage tracking and safe delete.
 *
 * Acceptance criteria verified:
 *   AC1: Usage list items show entry title (entryTitle field).
 *   AC2: Usage list items render as links pointing to /admin/content/{id}/edit.
 *   AC3: Usage section shows count of usages.
 *   AC4: "Not referenced" message shown when usages list is empty.
 *   AC5: contentTypeName and status shown in subtitle below the link.
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
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
const mockUseUpdateMediaMetadata = useUpdateMediaMetadata as ReturnType<typeof vi.fn>;

// ── Fixtures ───────────────────────────────────────────────────────────────────

const mockAsset: MediaAssetSummary = {
  id:                20,
  fileName:          'diagram.png',
  storagePath:       'uploads/xyz/diagram.png',
  mimeType:          'image/png',
  fileSizeBytes:     81920,
  altText:           'Architecture diagram',
  title:             null,
  width:             800,
  height:            600,
  webPStoragePath:   null,
  isVirusScanPassed: true,
  createdAt:         '2026-09-12T10:00:00Z',
};

function makeDetail(usages: MediaDetailDto['usages']): MediaDetailDto {
  return {
    id:                20,
    fileName:          'diagram.png',
    storagePath:       'uploads/xyz/diagram.png',
    storageBackend:    'local',
    mimeType:          'image/png',
    fileSizeBytes:     81920,
    altText:           'Architecture diagram',
    title:             null,
    description:       null,
    tags:              null,
    width:             800,
    height:            600,
    webPStoragePath:   null,
    isVirusScanPassed: true,
    uploadedById:      1,
    createdAt:         '2026-09-12T10:00:00Z',
    updatedAt:         '2026-09-12T10:00:00Z',
    usages,
  };
}

function wrapper(ui: React.ReactElement): React.ReactElement {
  return (
    <QueryClientProvider client={new QueryClient()}>
      {ui}
    </QueryClientProvider>
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('MediaLibraryPage — issue #44: usage tracking with titles and links', () => {
  const listData: MediaListDto = {
    items:      [mockAsset],
    page:       1,
    pageSize:   48,
    totalItems: 1,
  };

  beforeEach(() => {
    mockUseMediaAssets.mockReturnValue({ data: listData, isLoading: false, isError: false });
    mockUseUpdateMediaMetadata.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  });

  // ── AC4: empty usages → "Not referenced" message ──────────────────────────

  it('AC4: shows "Not referenced" when usages list is empty', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));

    // Open detail panel by clicking the card
    fireEvent.click(screen.getByTestId('media-card-20'));

    expect(screen.getByTestId('media-detail-usages')).toBeInTheDocument();
    expect(screen.getByText(/Not referenced by any content entry/i)).toBeInTheDocument();
  });

  // ── AC3: usage count shown in heading ─────────────────────────────────────

  it('AC3: shows usage count in the "Used in" heading', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([
        {
          contentEntryId:  101,
          fieldName:       'heroImage',
          slug:            'about-va',
          status:          'Published',
          contentTypeId:   5,
          contentTypeName: 'Standard Page',
          entryTitle:      'About VA',
        },
      ]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));
    fireEvent.click(screen.getByTestId('media-card-20'));

    // Heading shows "Used in (1)"
    expect(screen.getByText(/Used in \(1\)/i)).toBeInTheDocument();
  });

  // ── AC1: entry title shown in usage item ──────────────────────────────────

  it('AC1: usage item shows entryTitle text', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([
        {
          contentEntryId:  101,
          fieldName:       'heroImage',
          slug:            'about-va',
          status:          'Published',
          contentTypeId:   5,
          contentTypeName: 'Standard Page',
          entryTitle:      'About VA',
        },
      ]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));
    fireEvent.click(screen.getByTestId('media-card-20'));

    expect(screen.getByText('About VA')).toBeInTheDocument();
  });

  // ── AC2: usage item is a link to the content entry edit page ──────────────

  it('AC2: usage item renders as a link pointing to /admin/content/{id}/edit', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([
        {
          contentEntryId:  101,
          fieldName:       'heroImage',
          slug:            'about-va',
          status:          'Published',
          contentTypeId:   5,
          contentTypeName: 'Standard Page',
          entryTitle:      'About VA',
        },
      ]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));
    fireEvent.click(screen.getByTestId('media-card-20'));

    const link = screen.getByTestId('usage-link-101');
    expect(link).toBeInTheDocument();
    expect(link).toHaveAttribute('href', '/admin/content/101/edit');
    expect(link).toHaveTextContent('About VA');
  });

  // ── AC5: contentTypeName and status shown in subtitle ─────────────────────

  it('AC5: subtitle shows contentTypeName, status, and fieldName', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([
        {
          contentEntryId:  101,
          fieldName:       'heroImage',
          slug:            'about-va',
          status:          'Published',
          contentTypeId:   5,
          contentTypeName: 'Standard Page',
          entryTitle:      'About VA',
        },
      ]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));
    fireEvent.click(screen.getByTestId('media-card-20'));

    const usageItem = screen.getByTestId('usage-101');
    expect(usageItem).toHaveTextContent('Standard Page');
    expect(usageItem).toHaveTextContent('Published');
    expect(usageItem).toHaveTextContent('heroImage');
  });

  // ── Multiple usage items all have links ───────────────────────────────────

  it('renders a link for each usage item when multiple usages exist', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([
        {
          contentEntryId:  101,
          fieldName:       'heroImage',
          slug:            'about-va',
          status:          'Published',
          contentTypeId:   5,
          contentTypeName: 'Standard Page',
          entryTitle:      'About VA',
        },
        {
          contentEntryId:  202,
          fieldName:       'thumbnail',
          slug:            'news-article',
          status:          'Draft',
          contentTypeId:   7,
          contentTypeName: 'News Article',
          entryTitle:      'Latest VA News',
        },
      ]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));
    fireEvent.click(screen.getByTestId('media-card-20'));

    expect(screen.getByTestId('usage-link-101')).toHaveAttribute('href', '/admin/content/101/edit');
    expect(screen.getByTestId('usage-link-202')).toHaveAttribute('href', '/admin/content/202/edit');
    expect(screen.getByText('About VA')).toBeInTheDocument();
    expect(screen.getByText('Latest VA News')).toBeInTheDocument();
    expect(screen.getByText(/Used in \(2\)/i)).toBeInTheDocument();
  });

  // ── Falls back to slug when entryTitle is empty ───────────────────────────

  it('falls back to slug when entryTitle is empty', () => {
    mockUseMediaDetail.mockReturnValue({
      data: makeDetail([
        {
          contentEntryId:  303,
          fieldName:       'icon',
          slug:            'fallback-slug',
          status:          'Draft',
          contentTypeId:   3,
          contentTypeName: 'Standard Page',
          entryTitle:      '',
        },
      ]),
      isLoading: false,
    });

    render(wrapper(<MediaLibraryPage />));
    fireEvent.click(screen.getByTestId('media-card-20'));

    const link = screen.getByTestId('usage-link-303');
    expect(link).toHaveTextContent('fallback-slug');
  });
});
