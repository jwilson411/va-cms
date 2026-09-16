/**
 * lib/cms/navigation.ts
 *
 * Fetch primary navigation from the CMS API and transform it into the
 * NavItem[] shape expected by UswdsHeader.
 *
 * Issue #47 — BRD FR-NAV-01
 * AC: Public site header reads primary nav from /api/v1/navigation/primary
 *     at build/render time.  ISR revalidation is tagged so Next.js can
 *     invalidate the cache when an admin saves the menu (via the API's
 *     revalidation webhook).
 *
 * NEXT_PUBLIC_API_URL must be set (e.g. http://localhost:5100) in .env.local
 * for local development.  In production it is the internal API base URL.
 */

import { NavItem } from '@/components/uswds/UswdsHeader';

/** Shape returned by GET /api/v1/navigation/{handle} */
export interface CmsNavItemDto {
  id: number;
  label: string;
  url: string;
  target: string;
  children: CmsNavItemDto[];
}

export interface CmsNavigationResponse {
  handle: string;
  items: CmsNavItemDto[];
}

/** Cache tag used for ISR on-demand revalidation via revalidateTag(). */
export const NAV_CACHE_TAG = 'cms-primary-nav';

/**
 * Fetch the primary navigation from the CMS API.
 *
 * Uses Next.js 14 App Router fetch() with:
 *  - revalidate: false (ISR via tag — cache indefinitely, invalidate on change)
 *  - tags: [NAV_CACHE_TAG] — allows `revalidateTag('cms-primary-nav')` from
 *    a Route Handler when the admin saves the navigation menu.
 *
 * Falls back to an empty array on network/parse errors so the page still renders.
 */
export async function fetchPrimaryNav(): Promise<NavItem[]> {
  const apiBase =
    process.env.NEXT_PUBLIC_API_URL ??
    process.env.CMS_API_URL ??
    'http://localhost:5100';

  const url = `${apiBase}/api/v1/navigation/primary`;

  try {
    // next.revalidate=false means "never expire via time" — revalidation is
    // triggered by revalidateTag() from an API route when the menu is updated.
    const res = await fetch(url, {
      next: { revalidate: false, tags: [NAV_CACHE_TAG] },
      headers: { Accept: 'application/json' },
    });

    if (!res.ok) {
      console.warn(`[navigation] CMS nav fetch returned ${res.status}: ${url}`);
      return [];
    }

    const data: CmsNavigationResponse = await res.json();
    return mapCmsItemsToNavItems(data.items ?? []);
  } catch (err) {
    console.error('[navigation] Failed to fetch primary nav:', err);
    return [];
  }
}

/**
 * Recursively map CMS DTO items to the NavItem shape consumed by UswdsHeader.
 * USWDS Header currently supports up to 3 levels (Depth < 3 enforced by SP).
 */
function mapCmsItemsToNavItems(items: CmsNavItemDto[]): NavItem[] {
  return items.map((item) => ({
    label: item.label,
    href: item.url,
    children:
      item.children && item.children.length > 0
        ? mapCmsItemsToNavItems(item.children)
        : undefined,
  }));
}
