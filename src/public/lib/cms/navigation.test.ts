/**
 * lib/cms/navigation.test.ts
 *
 * Tests for issue #47 — primary navigation fetch and transform logic.
 *
 * Acceptance criteria covered:
 *   AC1: fetchPrimaryNav() returns NavItem[] shaped objects.
 *   AC2: Children are nested correctly from the API response.
 *   AC3: fetch() failure falls back to empty array.
 *   AC4: Empty items array from API returns empty NavItem[].
 */

import { describe, it, expect, vi, afterEach } from 'vitest';
import { fetchPrimaryNav, type CmsNavigationResponse } from './navigation';

afterEach(() => {
  vi.restoreAllMocks();
});

// ── helpers ─────────────────────────────────────────────────────────────────

function mockFetch(response: CmsNavigationResponse | null, status = 200): void {
  global.fetch = vi.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => response,
  } as Response);
}

// ── AC1: flat items are returned as NavItem[] ─────────────────────────────

describe('fetchPrimaryNav', () => {
  it('AC1: returns NavItem[] with label and href', async () => {
    mockFetch({
      handle: 'primary',
      items: [
        { id: 1, label: 'Home', url: '/', target: '_self', children: [] },
        { id: 2, label: 'About', url: '/about', target: '_self', children: [] },
      ],
    });

    const items = await fetchPrimaryNav();

    expect(items).toHaveLength(2);
    expect(items[0]).toMatchObject({ label: 'Home', href: '/' });
    expect(items[1]).toMatchObject({ label: 'About', href: '/about' });
  });

  // ── AC2: children are nested ──────────────────────────────────────────────

  it('AC2: nested children are mapped into NavItem.children', async () => {
    mockFetch({
      handle: 'primary',
      items: [
        {
          id: 1,
          label: 'Services',
          url: '/services',
          target: '_self',
          children: [
            { id: 2, label: 'Health Care', url: '/services/health', target: '_self', children: [] },
            { id: 3, label: 'Benefits', url: '/services/benefits', target: '_self', children: [] },
          ],
        },
      ],
    });

    const items = await fetchPrimaryNav();

    expect(items).toHaveLength(1);
    expect(items[0].label).toBe('Services');
    expect(items[0].children).toHaveLength(2);
    expect(items[0].children![0]).toMatchObject({ label: 'Health Care', href: '/services/health' });
    expect(items[0].children![1]).toMatchObject({ label: 'Benefits', href: '/services/benefits' });
  });

  // ── AC3: fetch failure returns empty array ────────────────────────────────

  it('AC3: returns empty array when fetch throws', async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error('ECONNREFUSED'));

    const items = await fetchPrimaryNav();
    expect(items).toEqual([]);
  });

  it('AC3: returns empty array when API returns non-OK status', async () => {
    mockFetch(null, 503);

    const items = await fetchPrimaryNav();
    expect(items).toEqual([]);
  });

  // ── AC4: empty items list returns empty array ─────────────────────────────

  it('AC4: API returning empty items array returns empty NavItem[]', async () => {
    mockFetch({ handle: 'primary', items: [] });

    const items = await fetchPrimaryNav();
    expect(items).toEqual([]);
  });

  // ── URL construction ──────────────────────────────────────────────────────

  it('uses NEXT_PUBLIC_API_URL env var if set', async () => {
    const capturedUrls: string[] = [];
    global.fetch = vi.fn().mockImplementation((url: string) => {
      capturedUrls.push(url);
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({ handle: 'primary', items: [] }),
      } as Response);
    });

    // Set the env var before calling (vitest doesn't reset process.env between tests)
    const original = process.env.NEXT_PUBLIC_API_URL;
    process.env.NEXT_PUBLIC_API_URL = 'http://api.test:5000';

    await fetchPrimaryNav();

    process.env.NEXT_PUBLIC_API_URL = original;

    expect(capturedUrls[0]).toBe('http://api.test:5000/api/v1/navigation/primary');
  });
});
