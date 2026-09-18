/**
 * lib/cms/redirect-if-moved.ts — page-route half of redirect serving (#169).
 *
 * Kept apart from redirects.ts because next/navigation's redirect() is for server
 * components; proxy.ts (which imports redirects.ts) answers with a Response instead.
 */
import { permanentRedirect, redirect } from 'next/navigation';
import { lookupRedirect, REDIRECTS_CACHE_TAG } from './redirects';

/**
 * When the CMS has no entry for the requested slug, leave for the redirect target
 * if a rule exists — otherwise return so the caller can notFound(). The lookup goes
 * through the Next data cache (tag REDIRECTS_CACHE_TAG) rather than the proxy's TTL
 * cache, so it is exact the moment the `redirects.updated` webhook lands.
 */
export async function redirectIfMoved(pathname: string): Promise<void> {
  const hit = await lookupRedirect(pathname, {
    timeoutMs: 0,
    init: { next: { revalidate: false, tags: [REDIRECTS_CACHE_TAG] } } as RequestInit,
  });
  if (!hit) return;

  // Server components cannot pick 301 vs 302; permanent → 308, temporary → 307.
  if (hit.statusCode === 301 || hit.statusCode === 308) permanentRedirect(hit.toPath);
  redirect(hit.toPath);
}
