/**
 * lib/cms/search.ts
 *
 * Client functions for the CMS search API.
 *
 * Issue #50 — BRD FR-SEARCH-01
 * AC: Search results page at /search fetches results from GET /api/v1/search
 *     with support for q, type, from, to, tag, page, and pageSize filters.
 *
 * This module is used by the Next.js App Router page (Server Component).
 * It has no direct browser dependencies — safe to use in server context.
 *
 * NEXT_PUBLIC_API_URL / CMS_API_URL must be set for the API base URL.
 */

/** A single search result item from GET /api/v1/search */
export interface SearchResultItem {
  id: number;
  title: string | null;
  slug: string;
  contentTypeId: number;
  contentTypeName: string;
  excerpt: string | null;
  publishedAt: string; // ISO 8601 UTC
  rank: number;
}

/** Full response envelope from GET /api/v1/search */
export interface SearchResponse {
  query: string;
  page: number;
  pageSize: number;
  totalItems: number;
  items: SearchResultItem[];
}

/** Filter parameters accepted by GET /api/v1/search (all optional) */
export interface SearchFilters {
  /** Filter by ContentTypeId */
  type?: number | null;
  /** ISO 8601 date — published on or after */
  from?: string | null;
  /** ISO 8601 date — published on or before */
  to?: string | null;
  /** Taxonomy term ID */
  tag?: number | null;
  /** 1-based page number (default 1) */
  page?: number;
  /** Results per page, 1–100 (default 25) */
  pageSize?: number;
}

/**
 * Fetch search results from the CMS API.
 *
 * Runs on the server (Next.js Server Component). Uses no-store cache
 * so search results are always fresh — search should not be ISR-cached.
 *
 * Returns an empty response on network/parse errors so the page renders gracefully.
 */
export async function fetchSearchResults(
  query: string,
  filters: SearchFilters = {},
): Promise<SearchResponse> {
  const apiBase =
    process.env.NEXT_PUBLIC_API_URL ??
    process.env.CMS_API_URL ??
    'http://localhost:5100';

  const params = new URLSearchParams({ q: query.trim() });

  if (filters.type != null) params.set('type', String(filters.type));
  if (filters.from) params.set('from', filters.from);
  if (filters.to) params.set('to', filters.to);
  if (filters.tag != null) params.set('tag', String(filters.tag));
  if (filters.page != null && filters.page > 0) params.set('page', String(filters.page));
  if (filters.pageSize != null && filters.pageSize > 0) params.set('pageSize', String(filters.pageSize));

  const url = `${apiBase}/api/v1/search?${params.toString()}`;

  try {
    const res = await fetch(url, {
      cache: 'no-store', // search is not cacheable per-query
      headers: { Accept: 'application/json' },
    });

    if (!res.ok) {
      console.warn(`[search] CMS search API returned ${res.status}: ${url}`);
      return emptyResponse(query, filters);
    }

    const data: SearchResponse = await res.json();
    return data;
  } catch (err) {
    console.error('[search] Failed to fetch search results:', err);
    return emptyResponse(query, filters);
  }
}

/** Build an empty SearchResponse (used for error fallback). */
function emptyResponse(query: string, filters: SearchFilters): SearchResponse {
  return {
    query,
    page: filters.page ?? 1,
    pageSize: filters.pageSize ?? 25,
    totalItems: 0,
    items: [],
  };
}

/**
 * Compute total pages from totalItems and pageSize.
 * Returns at least 1 (even when there are no results, the UI shows page 1 of 1).
 */
export function totalPages(totalItems: number, pageSize: number): number {
  if (pageSize <= 0) return 1;
  return Math.max(1, Math.ceil(totalItems / pageSize));
}

/**
 * Build an href for a search result card.
 * Slug is used as-is; prepend a leading slash if missing.
 */
export function searchResultHref(slug: string): string {
  return slug.startsWith('/') ? slug : `/${slug}`;
}

/**
 * Format a published-at ISO string to a human-readable date.
 * Returns empty string on parse failure.
 */
export function formatPublishedAt(isoDate: string): string {
  try {
    return new Date(isoDate).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  } catch {
    return '';
  }
}
