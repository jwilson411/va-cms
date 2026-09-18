/**
 * lib/cms/redirects.ts — serve the CMS redirect table (#169, BRD FR-NAV-05/06).
 *
 * The API answers GET /api/v1/redirects/resolve?path=<pathname> with
 * { fromPath, toPath, statusCode } (200) or 404, both carrying
 * Cache-Control: public, max-age=<redirects.cacheSeconds>. Two consumers:
 *
 *  1. proxy.ts asks on every page request and answers with the configured 301/302
 *     itself. The proxy runtime has no Next data cache, so answers (hits *and*
 *     misses) live in a process-local TTL map sized by the API's max-age. That is
 *     the propagation delay for a new rule: at most redirects.cacheSeconds.
 *  2. The page routes ask again (redirect-if-moved.ts), through the Next data cache
 *     tagged REDIRECTS_CACHE_TAG, when the CMS has no entry for a slug — so a rule
 *     created moments ago (still a cached miss in the proxy) still lands on the new
 *     page. The `redirects.updated` webhook drops that tag at once.
 *
 * Lookups fail open: a slow or unreachable API never blocks a page from rendering.
 */
/** Cache tag for the page-route lookups; dropped by /api/revalidate on `redirects.updated`. */
export const REDIRECTS_CACHE_TAG = 'cms-redirects';

/** Longest pathname the API will look at (the FromPath column width). */
export const MAX_PATH_LENGTH = 2000;

/** How long the proxy waits for the resolver before rendering the page anyway. */
export const LOOKUP_TIMEOUT_MS = 750;

/** Used when the API sends no usable max-age. */
export const DEFAULT_TTL_SECONDS = 60;

export interface RedirectHit {
  fromPath: string;
  toPath: string;
  statusCode: number;
}

export const getApiBase = (): string =>
  process.env.NEXT_PUBLIC_API_URL ?? process.env.CMS_API_URL ?? 'http://localhost:5100';

export const resolveUrl = (apiBase: string, pathname: string): string =>
  `${apiBase}/api/v1/redirects/resolve?path=${encodeURIComponent(pathname)}`;

/** True for a pathname the resolver could match: site-relative, not scheme-relative, bounded. */
export function isResolvablePath(pathname: string): boolean {
  return (
    pathname.length > 0 &&
    pathname.length <= MAX_PATH_LENGTH &&
    pathname.startsWith('/') &&
    !pathname.startsWith('//') &&
    !pathname.startsWith('/\\')
  );
}

/** The `max-age` of a Cache-Control header in seconds, or null when absent/invalid. */
export function parseMaxAge(cacheControl: string | null): number | null {
  if (!cacheControl) return null;
  const m = /(?:^|,)\s*max-age=(\d+)/i.exec(cacheControl);
  if (!m) return null;
  const n = Number.parseInt(m[1], 10);
  return Number.isFinite(n) && n >= 0 ? n : null;
}

/** Only redirect statuses a rule may carry; anything else becomes a 301. */
export function redirectStatus(code: number): 301 | 302 | 307 | 308 {
  return code === 302 || code === 307 || code === 308 ? code : 301;
}

/**
 * The Location for a hit. A site-relative target keeps the request's query string
 * (unless the rule carries its own); an external https target is sent as stored.
 * The proxy resolves a site-relative result against the request's own origin.
 */
export function redirectLocation(toPath: string, search: string): string {
  if (/^https?:\/\//i.test(toPath)) return toPath;
  return toPath.includes('?') || !search ? toPath : `${toPath}${search}`;
}

/** A hit (or a miss, as null) remembered until `expiresAt`. */
interface Entry {
  hit: RedirectHit | null;
  expiresAt: number;
}

/**
 * Process-local TTL cache with a size cap; the oldest entry goes when it is full.
 * Misses are cached too, since almost every page request is a miss.
 */
export class RedirectCache {
  private readonly entries = new Map<string, Entry>();

  constructor(private readonly maxEntries = 5000, private readonly now: () => number = Date.now) {}

  get(pathname: string): Entry | undefined {
    const e = this.entries.get(pathname);
    if (!e) return undefined;
    if (e.expiresAt <= this.now()) {
      this.entries.delete(pathname);
      return undefined;
    }
    return e;
  }

  set(pathname: string, hit: RedirectHit | null, ttlSeconds: number): void {
    if (ttlSeconds <= 0) return;
    if (this.entries.size >= this.maxEntries) {
      const oldest = this.entries.keys().next().value;
      if (oldest !== undefined) this.entries.delete(oldest);
    }
    this.entries.delete(pathname); // re-insert so insertion order tracks recency
    this.entries.set(pathname, { hit, expiresAt: this.now() + ttlSeconds * 1000 });
  }

  clear(): void {
    this.entries.clear();
  }

  get size(): number {
    return this.entries.size;
  }
}

export interface LookupOptions {
  fetchImpl?: typeof fetch;
  cache?: RedirectCache;
  timeoutMs?: number;
  apiBase?: string;
  /** Extra fetch init (the page routes pass `next: { tags }`). */
  init?: RequestInit;
}

/** The proxy's cache; one per server process. */
export const proxyCache = new RedirectCache();

/**
 * Ask the API for the rule matching `pathname`. Null on a miss; also null (and
 * nothing cached) when the API errors or does not answer within the timeout.
 */
export async function lookupRedirect(pathname: string, options: LookupOptions = {}): Promise<RedirectHit | null> {
  if (!isResolvablePath(pathname)) return null;

  const cache = options.cache;
  const cached = cache?.get(pathname);
  if (cached) return cached.hit;

  const fetchImpl = options.fetchImpl ?? fetch;
  const url = resolveUrl(options.apiBase ?? getApiBase(), pathname);
  const timeoutMs = options.timeoutMs ?? LOOKUP_TIMEOUT_MS;

  try {
    const res = await fetchImpl(url, {
      ...options.init,
      headers: { Accept: 'application/json', ...(options.init?.headers ?? {}) },
      signal: timeoutMs > 0 ? AbortSignal.timeout(timeoutMs) : undefined,
    });

    const ttl = parseMaxAge(res.headers.get('cache-control')) ?? DEFAULT_TTL_SECONDS;

    if (res.status === 404) {
      cache?.set(pathname, null, ttl);
      return null;
    }
    if (!res.ok) {
      console.warn(`[redirects] resolver returned ${res.status} for ${pathname}`);
      return null;
    }

    const body = (await res.json()) as Partial<RedirectHit>;
    if (typeof body.toPath !== 'string' || body.toPath.length === 0) return null;
    const hit: RedirectHit = {
      fromPath: typeof body.fromPath === 'string' ? body.fromPath : pathname,
      toPath: body.toPath,
      statusCode: redirectStatus(Number(body.statusCode)),
    };
    cache?.set(pathname, hit, ttl);
    return hit;
  } catch (err) {
    console.warn(`[redirects] lookup failed for ${pathname}:`, err instanceof Error ? err.message : err);
    return null;
  }
}
