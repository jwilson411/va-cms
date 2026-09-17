/**
 * app/api/revalidate-nav/route.ts
 *
 * On-demand ISR revalidation endpoint for the primary navigation.
 *
 * Issue #47 — AC: Nav updates without a code deploy (ISR revalidation on menu save).
 *
 * The CMS API calls this endpoint (via a webhook or admin save hook) whenever
 * the primary navigation menu is updated.  Next.js then re-fetches the nav
 * from the CMS API on the next page request, discarding the stale cached result.
 *
 * Usage from the CMS API:
 *   POST /api/revalidate-nav
 *   Authorization: Bearer <REVALIDATE_TOKEN>  (env var REVALIDATE_SECRET)
 *
 * Local dev: curl -X POST http://localhost:3000/api/revalidate-nav \
 *   -H "Authorization: Bearer dev-secret"
 */

import { revalidateTag } from 'next/cache';
import { NextRequest, NextResponse } from 'next/server';
import { NAV_CACHE_TAG } from '@/lib/cms/navigation';

export async function POST(request: NextRequest): Promise<NextResponse> {
  // Verify the caller is the CMS API (simple bearer token check).
  const secret = process.env.REVALIDATE_SECRET;
  const authHeader = request.headers.get('authorization') ?? '';
  const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : null;

  if (secret && token !== secret) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  // Invalidate the navigation cache tag — Next.js will re-fetch on next request.
  revalidateTag(NAV_CACHE_TAG, 'max');

  return NextResponse.json({ revalidated: true, tag: NAV_CACHE_TAG });
}
