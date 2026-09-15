import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ContentTypeBrowserPage } from '../../pages/ContentTypeBrowserPage';
import type { ContentTypeSummaryDto, ContentTypeDetailDto } from './types';

// ── Mock hooks ───────────────────────────────────────────────────────────────

vi.mock('./useContentTypes', () => ({
  useContentTypes: vi.fn(),
  useContentTypeDetail: vi.fn(),
}));

import { useContentTypes, useContentTypeDetail } from './useContentTypes';

const mockUseContentTypes = useContentTypes as ReturnType<typeof vi.fn>;
const mockUseContentTypeDetail = useContentTypeDetail as ReturnType<typeof vi.fn>;

// ── Fixtures ─────────────────────────────────────────────────────────────────

const mockTypes: ContentTypeSummaryDto[] = [
  {
    name: 'standard_page',
    displayName: 'Standard Page',
    description: null,
    fieldCount: 4,
    allowWorkflow: true,
    fields: [
      { name: 'title', label: 'title', type: 'ShortText', required: true, maxLength: 200 },
      { name: 'summary', label: 'summary', type: 'LongText', required: false, maxLength: 500 },
      { name: 'body', label: 'body', type: 'RichText', required: true, maxLength: null },
      { name: 'heroImage', label: 'heroImage', type: 'MediaReference', required: false, maxLength: null },
    ],
  },
];

const mockDetail: ContentTypeDetailDto = {
  name: 'standard_page',
  displayName: 'Standard Page',
  description: 'The default page type.',
  templateId: 'StandardPageTemplate',
  allowWorkflow: true,
  fields: mockTypes[0].fields,
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderWithQuery(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>{ui}</QueryClientProvider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('ContentTypeBrowserPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseContentTypeDetail.mockReturnValue({ data: mockDetail, isLoading: false, isError: false });
  });

  it('renders loading state', () => {
    mockUseContentTypes.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);
    expect(screen.getByText('Loading content types…')).toBeInTheDocument();
  });

  it('renders error state', () => {
    mockUseContentTypes.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error('Network error'),
    });
    renderWithQuery(<ContentTypeBrowserPage />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText(/Network error/)).toBeInTheDocument();
  });

  it('lists all registered content types in a table', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    // Heading
    expect(screen.getByRole('heading', { name: /Content Types/i })).toBeInTheDocument();

    // Table row for standard_page
    expect(screen.getByText('Standard Page')).toBeInTheDocument();
    expect(screen.getByText('standard_page')).toBeInTheDocument();

    // Field count
    expect(screen.getByText('4')).toBeInTheDocument();

    // View schema link
    expect(screen.getByRole('button', { name: /View schema for Standard Page/i })).toBeInTheDocument();
  });

  it('shows empty state when no types are registered', () => {
    mockUseContentTypes.mockReturnValue({ data: [], isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);
    expect(screen.getByText('No content types are registered.')).toBeInTheDocument();
  });

  it('clicking View schema shows the detail panel', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));

    // Detail panel heading
    expect(screen.getByRole('heading', { name: 'Standard Page' })).toBeInTheDocument();
  });

  it('detail panel shows description when present', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));

    expect(screen.getByText('The default page type.')).toBeInTheDocument();
  });

  it('detail panel shows ordered field list', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));

    // All 4 fields appear
    expect(screen.getByText('title')).toBeInTheDocument();
    expect(screen.getByText('summary')).toBeInTheDocument();
    expect(screen.getByText('body')).toBeInTheDocument();
    expect(screen.getByText('heroImage')).toBeInTheDocument();
  });

  it('detail panel shows field type', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));

    expect(screen.getByText('ShortText')).toBeInTheDocument();
    expect(screen.getByText('RichText')).toBeInTheDocument();
  });

  it('detail panel shows required flag', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));

    expect(screen.getAllByText('✓ Yes').length).toBeGreaterThan(0);
    expect(screen.getAllByText('No').length).toBeGreaterThan(0);
  });

  it('detail panel shows maxLength constraint', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));

    expect(screen.getByText('Max 200 chars')).toBeInTheDocument();
    expect(screen.getByText('Max 500 chars')).toBeInTheDocument();
  });

  it('clicking Back returns to the list', () => {
    mockUseContentTypes.mockReturnValue({ data: mockTypes, isLoading: false, isError: false });
    renderWithQuery(<ContentTypeBrowserPage />);

    fireEvent.click(screen.getByRole('button', { name: /View schema for Standard Page/i }));
    fireEvent.click(screen.getByRole('button', { name: /Back to content type list/i }));

    // Back on the list
    expect(screen.getByRole('heading', { name: /Content Types/i })).toBeInTheDocument();
  });
});
