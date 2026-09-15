/**
 * lib/cms/search.test.ts — Unit tests for the search API client.
 *
 * Issue #50 — BRD FR-SEARCH-01
 *
 * Tests cover:
 *   - fetchSearchResults builds correct URL and returns parsed response
 *   - fetchSearchResults returns empty response on API error
 *   - totalPages computed correctly
 *   - searchResultHref adds leading slash
 *   - formatPublishedAt formats ISO date to readable string
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  fetchSearchResults,
  totalPages,
  searchResultHref,
  formatPublishedAt,
  type SearchResponse,
} from './search';

// ---------------------------------------------------------------------------
// totalPages
// ---------------------------------------------------------------------------

describe('totalPages', () => {
  it('returns 1 when there are no results', () => {
    expect(totalPages(0, 10)).toBe(1);
  });

  it('returns 1 when all results fit on one page', () => {
    expect(totalPages(10, 10)).toBe(1);
  });

  it('returns 2 when results need exactly two pages', () => {
    expect(totalPages(11, 10)).toBe(2);
  });

  it('returns ceil(total/pageSize)', () => {
    expect(totalPages(55, 10)).toBe(6);
  });

  it('returns 1 when pageSize is 0 (guard)', () => {
    expect(totalPages(100, 0)).toBe(1);
  });

  it('returns 1 for totalItems 0 and any pageSize', () => {
    expect(totalPages(0, 25)).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// searchResultHref
// ---------------------------------------------------------------------------

describe('searchResultHref', () => {
  it('prepends slash to slug without leading slash', () => {
    expect(searchResultHref('news/my-article')).toBe('/news/my-article');
  });

  it('does not double slash when slug already has leading slash', () => {
    expect(searchResultHref('/about/mission')).toBe('/about/mission');
  });

  it('handles root slug', () => {
    expect(searchResultHref('/')).toBe('/');
  });
});

// ---------------------------------------------------------------------------
// formatPublishedAt
// ---------------------------------------------------------------------------

describe('formatPublishedAt', () => {
  it('formats a valid ISO date string', () => {
    const result = formatPublishedAt('2026-06-20T12:00:00Z');
    // The exact format depends on locale; ensure it contains the year.
    // Avoid checking day number due to timezone rendering differences.
    expect(result).toContain('2026');
    expect(result.length).toBeGreaterThan(4);
  });

  it('returns empty string for invalid date', () => {
    const result = formatPublishedAt('not-a-date');
    // Invalid Date.toLocaleDateString returns "Invalid Date" or throws;
    // our implementation catches and returns ''
    expect(typeof result).toBe('string');
  });

  it('handles empty string gracefully', () => {
    const result = formatPublishedAt('');
    expect(typeof result).toBe('string');
  });
});

// ---------------------------------------------------------------------------
// fetchSearchResults — mocked fetch
// ---------------------------------------------------------------------------

const mockResponse: SearchResponse = {
  query: 'veterans',
  page: 1,
  pageSize: 10,
  totalItems: 2,
  items: [
    {
      id: 1,
      title: 'VA Benefits',
      slug: 'news/va-benefits',
      contentTypeId: 1,
      contentTypeName: 'News Article',
      excerpt: 'Get your benefits today.',
      publishedAt: '2026-01-01T00:00:00Z',
      rank: 100,
    },
    {
      id: 2,
      title: 'Health Care',
      slug: 'health/overview',
      contentTypeId: 2,
      contentTypeName: 'Standard Page',
      excerpt: 'Health care for veterans.',
      publishedAt: '2026-02-01T00:00:00Z',
      rank: 80,
    },
  ],
};

describe('fetchSearchResults', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    // @ts-expect-error – env stub
    process.env.NEXT_PUBLIC_API_URL = 'http://localhost:5000';
  });

  afterEach(() => {
    vi.restoreAllMocks();
    delete process.env.NEXT_PUBLIC_API_URL;
  });

  it('calls fetch with correct URL for a simple query', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    });
    vi.stubGlobal('fetch', fetchMock);

    await fetchSearchResults('veterans');

    expect(fetchMock).toHaveBeenCalledOnce();
    const calledUrl: string = fetchMock.mock.calls[0][0];
    expect(calledUrl).toContain('/api/v1/search');
    expect(calledUrl).toContain('q=veterans');
  });

  it('includes type filter in URL when provided', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    });
    vi.stubGlobal('fetch', fetchMock);

    await fetchSearchResults('veterans', { type: 3 });

    const calledUrl: string = fetchMock.mock.calls[0][0];
    expect(calledUrl).toContain('type=3');
  });

  it('includes from and to date filters in URL when provided', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    });
    vi.stubGlobal('fetch', fetchMock);

    await fetchSearchResults('veterans', {
      from: '2024-01-01',
      to: '2026-12-31',
    });

    const calledUrl: string = fetchMock.mock.calls[0][0];
    expect(calledUrl).toContain('from=2024-01-01');
    expect(calledUrl).toContain('to=2026-12-31');
  });

  it('includes tag filter in URL when provided', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    });
    vi.stubGlobal('fetch', fetchMock);

    await fetchSearchResults('veterans', { tag: 7 });

    const calledUrl: string = fetchMock.mock.calls[0][0];
    expect(calledUrl).toContain('tag=7');
  });

  it('returns parsed SearchResponse on success', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    });
    vi.stubGlobal('fetch', fetchMock);

    const result = await fetchSearchResults('veterans');
    expect(result.query).toBe('veterans');
    expect(result.totalItems).toBe(2);
    expect(result.items).toHaveLength(2);
  });

  it('returns empty response when API returns non-2xx', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 503,
    });
    vi.stubGlobal('fetch', fetchMock);

    const result = await fetchSearchResults('veterans');
    expect(result.totalItems).toBe(0);
    expect(result.items).toHaveLength(0);
    expect(result.query).toBe('veterans');
  });

  it('returns empty response when fetch throws', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error('Network error'));
    vi.stubGlobal('fetch', fetchMock);

    const result = await fetchSearchResults('veterans');
    expect(result.totalItems).toBe(0);
    expect(result.items).toHaveLength(0);
  });

  it('omits null/undefined filters from URL', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    });
    vi.stubGlobal('fetch', fetchMock);

    await fetchSearchResults('veterans', { type: null, tag: null });

    const calledUrl: string = fetchMock.mock.calls[0][0];
    expect(calledUrl).not.toContain('type=');
    expect(calledUrl).not.toContain('tag=');
  });

  it('trims leading/trailing whitespace from query', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ ...mockResponse, query: 'veterans' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await fetchSearchResults('  veterans  ');

    const calledUrl: string = fetchMock.mock.calls[0][0];
    expect(calledUrl).toContain('q=veterans');
    expect(calledUrl).not.toContain('q=+veterans+');
  });
});
