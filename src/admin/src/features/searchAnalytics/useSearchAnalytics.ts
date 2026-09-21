/**
 * API hooks for search analytics (issue #51 — FR-SEARCH-06).
 */

import { useMutation, useQuery } from '@tanstack/react-query';
import { useAuth } from '../../context/AuthContext';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface TopQueryItem {
  query: string;
  searchCount: number;
  zeroResultCount: number;
  avgResultCount: number;
  lastSearchedAt: string;
}

export interface ZeroResultQueryItem {
  query: string;
  zeroResultCount: number;
  lastSearchedAt: string;
}

export interface SearchAnalyticsSummary {
  topQueries: TopQueryItem[];
  zeroResultQueries: ZeroResultQueryItem[];
}

export interface SearchAnalyticsRow {
  query: string;
  searchCount: number;
  zeroResultCount: number;
  avgResultCount: number;
  lastSearchedAt: string;
  clickCount: number;
  clickThroughRate: number;
}

export interface SearchAnalyticsPage {
  totalRows: number;
  totalPages: number;
  page: number;
  pageSize: number;
  daysBack: number;
  items: SearchAnalyticsRow[];
}

export type SearchAnalyticsSortBy =
  | 'Query'
  | 'SearchCount'
  | 'ZeroResultCount'
  | 'AvgResultCount'
  | 'ClickCount'
  | 'ClickThroughRate'
  | 'LastSearchedAt';
export type SearchAnalyticsSortDir = 'ASC' | 'DESC';

// ── Query keys ────────────────────────────────────────────────────────────────

export const searchAnalyticsKeys = {
  summary: () => ['search-analytics', 'summary'] as const,
  full: (
    daysBack: number,
    page: number,
    pageSize: number,
    sortBy: SearchAnalyticsSortBy,
    sortDir: SearchAnalyticsSortDir,
    q: string,
  ) => ['search-analytics', 'full', daysBack, page, pageSize, sortBy, sortDir, q] as const,
};

// ── useSearchAnalyticsSummary ─────────────────────────────────────────────────

export function useSearchAnalyticsSummary() {
  const { authFetch } = useAuth();
  return useQuery<SearchAnalyticsSummary>({
    queryKey: searchAnalyticsKeys.summary(),
    queryFn: async () => {
      const res = await authFetch('/api/v1/admin/search/analytics/summary');
      if (!res.ok) throw new Error(`Search analytics summary fetch failed: ${res.status}`);
      return res.json() as Promise<SearchAnalyticsSummary>;
    },
    staleTime: 5 * 60 * 1000,
  });
}

// ── useSearchAnalyticsFull ────────────────────────────────────────────────────

export function useSearchAnalyticsFull(
  daysBack: number = 30,
  page: number = 1,
  pageSize: number = 50,
  sortBy: SearchAnalyticsSortBy = 'SearchCount',
  sortDir: SearchAnalyticsSortDir = 'DESC',
  q: string = '',
) {
  const { authFetch } = useAuth();
  return useQuery<SearchAnalyticsPage>({
    queryKey: searchAnalyticsKeys.full(daysBack, page, pageSize, sortBy, sortDir, q),
    queryFn: async () => {
      const params = new URLSearchParams({
        daysBack: String(daysBack),
        page: String(page),
        pageSize: String(pageSize),
        sortBy,
        sortDir,
      });
      if (q) params.set('q', q);
      const res = await authFetch(`/api/v1/admin/search/analytics?${params.toString()}`);
      if (!res.ok) throw new Error(`Search analytics fetch failed: ${res.status}`);
      return res.json() as Promise<SearchAnalyticsPage>;
    },
    staleTime: 5 * 60 * 1000,
  });
}

// ── useLogSearchClick ─────────────────────────────────────────────────────────

export function useLogSearchClick() {
  return useMutation({
    mutationFn: async (params: {
      query: string;
      clickedSlug: string;
      resultRank: number;
    }) => {
      const res = await fetch('/api/v1/search/click', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(params),
      });
      if (!res.ok && res.status !== 204) {
        console.warn('[search] Click log failed:', res.status);
      }
    },
  });
}
