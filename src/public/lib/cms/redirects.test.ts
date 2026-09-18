/**
 * lib/cms/redirects.test.ts — redirect serving on the public site (#169).
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  DEFAULT_TTL_SECONDS,
  isResolvablePath,
  lookupRedirect,
  parseMaxAge,
  RedirectCache,
  redirectLocation,
  redirectStatus,
  resolveUrl,
} from './redirects';

function jsonResponse(body: unknown, status = 200, cacheControl = 'public, max-age=120'): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json', 'cache-control': cacheControl },
  });
}

function fetchStub(responder: (url: string) => Response | Promise<Response>) {
  return vi.fn(async (input: RequestInfo | URL) => responder(String(input))) as unknown as typeof fetch;
}

describe('isResolvablePath', () => {
  it('accepts site-relative paths and rejects what cannot be a FromPath', () => {
    expect(isResolvablePath('/pages/a')).toBe(true);
    expect(isResolvablePath('/')).toBe(true);
    expect(isResolvablePath('')).toBe(false);
    expect(isResolvablePath('pages/a')).toBe(false);
    expect(isResolvablePath('//evil.example/x')).toBe(false);
    expect(isResolvablePath('/\\evil.example/x')).toBe(false);
    expect(isResolvablePath('/' + 'a'.repeat(2000))).toBe(false);
  });
});

describe('parseMaxAge / redirectStatus / redirectLocation', () => {
  it('reads max-age out of Cache-Control', () => {
    expect(parseMaxAge('public, max-age=120')).toBe(120);
    expect(parseMaxAge('max-age=0')).toBe(0);
    expect(parseMaxAge('no-store')).toBeNull();
    expect(parseMaxAge(null)).toBeNull();
    expect(parseMaxAge('s-maxage=5')).toBeNull();
  });

  it('only lets redirect statuses through', () => {
    expect(redirectStatus(301)).toBe(301);
    expect(redirectStatus(302)).toBe(302);
    expect(redirectStatus(308)).toBe(308);
    expect(redirectStatus(200)).toBe(301);
    expect(redirectStatus(Number.NaN)).toBe(301);
  });

  it('keeps the query string for site-relative targets only', () => {
    expect(redirectLocation('/pages/new', '?utm=1')).toBe('/pages/new?utm=1');
    expect(redirectLocation('/pages/new?x=1', '?utm=1')).toBe('/pages/new?x=1');
    expect(redirectLocation('/pages/new', '')).toBe('/pages/new');
    expect(redirectLocation('https://www.va.gov/health-care', '?utm=1')).toBe('https://www.va.gov/health-care');
  });

  it('encodes the path into the resolver URL', () => {
    expect(resolveUrl('http://api', '/pages/a b')).toBe('http://api/api/v1/redirects/resolve?path=%2Fpages%2Fa%20b');
  });
});

describe('RedirectCache', () => {
  it('expires entries and evicts the oldest when full', () => {
    let now = 1_000;
    const cache = new RedirectCache(2, () => now);
    cache.set('/a', null, 10);
    cache.set('/b', { fromPath: '/b', toPath: '/c', statusCode: 301 }, 10);
    expect(cache.get('/a')?.hit).toBeNull();
    expect(cache.get('/b')?.hit?.toPath).toBe('/c');

    cache.set('/d', null, 10); // full: /a goes
    expect(cache.size).toBe(2);
    expect(cache.get('/a')).toBeUndefined();

    now += 10_001;
    expect(cache.get('/b')).toBeUndefined();
    expect(cache.size).toBe(1);
  });

  it('does not store with a zero ttl', () => {
    const cache = new RedirectCache();
    cache.set('/a', null, 0);
    expect(cache.size).toBe(0);
  });
});

describe('lookupRedirect', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('returns the hit with a sanitised status and caches it for max-age', async () => {
    const fetchImpl = fetchStub(() => jsonResponse({ fromPath: '/old', toPath: '/new', statusCode: 302 }));
    const cache = new RedirectCache();

    const hit = await lookupRedirect('/old', { fetchImpl, cache, apiBase: 'http://api' });
    expect(hit).toEqual({ fromPath: '/old', toPath: '/new', statusCode: 302 });
    expect(cache.get('/old')?.expiresAt).toBeGreaterThan(Date.now() + 100_000);

    await lookupRedirect('/old', { fetchImpl, cache, apiBase: 'http://api' });
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it('caches a 404 as a miss', async () => {
    const fetchImpl = fetchStub(() => jsonResponse({}, 404, 'public, max-age=30'));
    const cache = new RedirectCache();

    expect(await lookupRedirect('/nope', { fetchImpl, cache })).toBeNull();
    expect(await lookupRedirect('/nope', { fetchImpl, cache })).toBeNull();
    expect(fetchImpl).toHaveBeenCalledTimes(1);
    expect(cache.get('/nope')?.hit).toBeNull();
  });

  it('falls back to the default ttl without a max-age', async () => {
    const fetchImpl = fetchStub(() => jsonResponse({}, 404, 'no-cache'));
    let now = 0;
    const cache = new RedirectCache(10, () => now);
    await lookupRedirect('/x', { fetchImpl, cache });
    now = DEFAULT_TTL_SECONDS * 1000 - 1;
    expect(cache.get('/x')).toBeDefined();
    now = DEFAULT_TTL_SECONDS * 1000 + 1;
    expect(cache.get('/x')).toBeUndefined();
  });

  it('fails open on server errors, network errors and timeouts, caching nothing', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    const cache = new RedirectCache();

    expect(await lookupRedirect('/a', { fetchImpl: fetchStub(() => jsonResponse({}, 503)), cache })).toBeNull();
    expect(
      await lookupRedirect('/b', {
        fetchImpl: fetchStub(() => {
          throw new Error('ECONNREFUSED');
        }),
        cache,
      }),
    ).toBeNull();
    const slow = fetchStub(
      (): Promise<Response> => new Promise((resolve) => setTimeout(() => resolve(jsonResponse({})), 200)),
    );
    expect(await lookupRedirect('/c', { fetchImpl: slow, cache, timeoutMs: 20 })).toBeNull();
    expect(cache.size).toBe(0);
  });

  it('ignores a body without a usable toPath', async () => {
    const fetchImpl = fetchStub(() => jsonResponse({ fromPath: '/old' }));
    expect(await lookupRedirect('/old', { fetchImpl })).toBeNull();
  });

  it('never asks about a path the resolver would reject', async () => {
    const fetchImpl = fetchStub(() => jsonResponse({}));
    expect(await lookupRedirect('//evil.example', { fetchImpl })).toBeNull();
    expect(fetchImpl).not.toHaveBeenCalled();
  });
});
