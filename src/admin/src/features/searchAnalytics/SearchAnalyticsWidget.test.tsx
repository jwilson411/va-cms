/**
 * Tests for SearchAnalyticsWidget and SearchAnalyticsPage — issue #51.
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SearchAnalyticsWidget } from './SearchAnalyticsWidget';
import { SearchAnalyticsPage } from './SearchAnalyticsPage';
import * as hooks from './useSearchAnalytics';

// Site settings (epic #141): render with the code defaults, no QueryClient needed.
vi.mock('../siteSettings/useClientSettings', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../siteSettings/useClientSettings')>();
  return {
    ...actual,
    useClientSettings: () => actual.buildClientSettings({}, { isLoading: false, isError: false }),
  };
});


// ── mock the hooks ─────────────────────────────────────────────────────────────

vi.mock('./useSearchAnalytics');

const mockUseSummary = vi.mocked(hooks.useSearchAnalyticsSummary);
const mockUseFull = vi.mocked(hooks.useSearchAnalyticsFull);

// ── helpers ───────────────────────────────────────────────────────────────────

function buildTopQuery(n: number) {
  return Array.from({ length: n }, (_, i) => ({
    query: `query-${i}`,
    searchCount: 10 - i,
    zeroResultCount: 0,
    avgResultCount: 5,
    lastSearchedAt: '2026-09-01T00:00:00Z',
  }));
}

function buildZeroQuery(n: number) {
  return Array.from({ length: n }, (_, i) => ({
    query: `zero-${i}`,
    zeroResultCount: 5 - i,
    lastSearchedAt: '2026-09-01T00:00:00Z',
  }));
}

function buildFullPage(count: number) {
  return {
    totalRows: count,
    totalPages: count > 50 ? 2 : 1,
    page: 1,
    pageSize: 50,
    daysBack: 30,
    items: Array.from({ length: count }, (_, i) => ({
      query: `query-${i}`,
      searchCount: 10,
      zeroResultCount: 1,
      avgResultCount: 5.0,
      lastSearchedAt: '2026-09-01T00:00:00Z',
      clickCount: 2,
      clickThroughRate: 20.0,
    })),
  };
}

// ── SearchAnalyticsWidget tests ───────────────────────────────────────────────

describe('SearchAnalyticsWidget', () => {
  it('shows loading state', () => {
    mockUseSummary.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    expect(screen.getByText(/loading search analytics/i)).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockUseSummary.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('renders top queries table on success', () => {
    mockUseSummary.mockReturnValue({
      data: {
        topQueries: buildTopQuery(5),
        zeroResultQueries: buildZeroQuery(3),
      },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    expect(screen.getByText(/top 10 queries/i)).toBeInTheDocument();
    expect(screen.getByText('query-0')).toBeInTheDocument();
    expect(screen.getByText('query-4')).toBeInTheDocument();
  });

  it('renders zero-result queries table on success', () => {
    mockUseSummary.mockReturnValue({
      data: {
        topQueries: buildTopQuery(2),
        zeroResultQueries: buildZeroQuery(3),
      },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    expect(screen.getByText(/top 10 zero-result/i)).toBeInTheDocument();
    expect(screen.getByText('zero-0')).toBeInTheDocument();
  });

  it('shows empty state message when no queries', () => {
    mockUseSummary.mockReturnValue({
      data: { topQueries: [], zeroResultQueries: [] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    expect(screen.getAllByText(/no search data in the last 30 days/i).length).toBeGreaterThan(0);
  });

  it('has a link to the full analytics page', () => {
    mockUseSummary.mockReturnValue({
      data: { topQueries: [], zeroResultQueries: [] },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    const link = screen.getByRole('link', { name: /view full search analytics/i });
    expect(link).toHaveAttribute('href', '/admin/search/analytics');
  });

  it('tables have accessible headings', () => {
    mockUseSummary.mockReturnValue({
      data: {
        topQueries: buildTopQuery(3),
        zeroResultQueries: buildZeroQuery(2),
      },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsSummary>);

    render(<SearchAnalyticsWidget />);
    expect(screen.getByText(/top 10 queries/i)).toBeInTheDocument();
    expect(screen.getByText(/top 10 zero-result/i)).toBeInTheDocument();
  });
});

// ── SearchAnalyticsPage tests ─────────────────────────────────────────────────

describe('SearchAnalyticsPage', () => {
  it('shows loading state', () => {
    mockUseFull.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsFull>);

    render(<SearchAnalyticsPage />);
    expect(screen.getByText(/loading analytics/i)).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockUseFull.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsFull>);

    render(<SearchAnalyticsPage />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('renders analytics table with CTR column', () => {
    mockUseFull.mockReturnValue({
      data: buildFullPage(5),
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsFull>);

    render(<SearchAnalyticsPage />);
    expect(screen.getByText(/ctr/i)).toBeInTheDocument();
    expect(screen.getByText('query-0')).toBeInTheDocument();
    expect(screen.getByText('query-4')).toBeInTheDocument();
  });

  it('shows empty state when no items', () => {
    mockUseFull.mockReturnValue({
      data: { ...buildFullPage(0), items: [], totalRows: 0, totalPages: 0 },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsFull>);

    render(<SearchAnalyticsPage />);
    expect(screen.getByText(/no search activity/i)).toBeInTheDocument();
  });

  it('date range select has accessible label', () => {
    mockUseFull.mockReturnValue({
      data: buildFullPage(0),
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsFull>);

    render(<SearchAnalyticsPage />);
    expect(screen.getByLabelText(/date range/i)).toBeInTheDocument();
  });

  it('shows pagination when multiple pages', async () => {
    mockUseFull.mockReturnValue({
      data: { ...buildFullPage(100), totalRows: 100, totalPages: 2, page: 1 },
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof hooks.useSearchAnalyticsFull>);

    render(<SearchAnalyticsPage />);
    const nextBtn = screen.getByRole('button', { name: /next page/i });
    expect(nextBtn).toBeInTheDocument();
    await userEvent.click(nextBtn);
    // After click, hook is called again — just verify button was present
    expect(nextBtn).toBeInTheDocument();
  });
});
