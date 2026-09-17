/**
 * Tests for lib/cms/settings.ts (issue #149, epic #141).
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  DEFAULT_SITE_SETTINGS,
  SITE_SETTINGS_CACHE_TAG,
  fetchSiteSettings,
  mapSiteSettings,
} from './settings';

describe('mapSiteSettings', () => {
  it('returns defaults for an empty map', () => {
    expect(mapSiteSettings({})).toEqual(DEFAULT_SITE_SETTINGS);
  });

  it('maps and types every key', () => {
    const s = mapSiteSettings({
      'site.title': 'VA Benefits',
      'site.metaTitle': 'Benefits',
      'site.metaDescription': 'desc',
      'site.agencyName': 'Veterans Benefits Administration',
      'site.agencyShortName': 'VBA',
      'site.agencyHref': 'https://benefits.va.gov',
      'site.agencyLogoSrc': '/logo.svg',
      'site.bannerLang': 'ES',
      'analytics.dapEnabled': 'true',
      'analytics.dapAgency': 'VA',
      'analytics.dapSubagency': 'VBA',
      'features.publicSearch': 'false',
      'search.publicPageSize': '25',
    });
    expect(s).toEqual({
      siteTitle: 'VA Benefits',
      metaTitle: 'Benefits',
      metaDescription: 'desc',
      agencyName: 'Veterans Benefits Administration',
      agencyShortName: 'VBA',
      agencyHref: 'https://benefits.va.gov',
      agencyLogoSrc: '/logo.svg',
      bannerLang: 'es',
      dapEnabled: true,
      dapAgency: 'VA',
      dapSubagency: 'VBA',
      publicSearchEnabled: false,
      searchPageSize: 25,
    });
  });

  it('falls back on unparseable values', () => {
    const s = mapSiteSettings({
      'site.bannerLang': 'fr',
      'analytics.dapEnabled': 'maybe',
      'search.publicPageSize': '0',
      'features.publicSearch': null,
    });
    expect(s.bannerLang).toBe('en');
    expect(s.dapEnabled).toBe(false);
    expect(s.searchPageSize).toBe(10);
    expect(s.publicSearchEnabled).toBe(true);
  });
});

describe('fetchSiteSettings', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    globalThis.fetch = vi.fn();
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it('calls /api/v1/settings/public with the ISR cache tag', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue({
      ok: true,
      json: async () => ({ 'site.title': 'From API' }),
    } as Response);

    const s = await fetchSiteSettings();

    expect(s.siteTitle).toBe('From API');
    expect(s.agencyShortName).toBe('VA');
    const [url, init] = vi.mocked(globalThis.fetch).mock.calls[0];
    expect(String(url)).toMatch(/\/api\/v1\/settings\/public$/);
    expect((init as { next: { tags: string[] } }).next.tags).toEqual([SITE_SETTINGS_CACHE_TAG]);
  });

  it('returns defaults on a non-OK response', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue({ ok: false, status: 503 } as Response);
    expect(await fetchSiteSettings()).toEqual(DEFAULT_SITE_SETTINGS);
  });

  it('returns defaults when fetch throws', async () => {
    vi.mocked(globalThis.fetch).mockRejectedValue(new Error('ECONNREFUSED'));
    expect(await fetchSiteSettings()).toEqual(DEFAULT_SITE_SETTINGS);
  });
});
