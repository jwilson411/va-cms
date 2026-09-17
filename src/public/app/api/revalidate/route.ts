/**
 * app/api/revalidate/route.ts
 *
 * CMS webhook receiver → on-demand ISR revalidation (issue #54 / #47).
 *
 * The CMS API delivers signed webhooks (X-CMS-Signature: sha256=<hmac of raw body>,
 * X-CMS-Event: <event name>) for:
 *   content.published / content.unpublished / content.archived
 *     → drop the cached page for that slug (cms-page-{slug} / cms-article-{slug})
 *       plus the type-wide tag so listings refresh
 *   navigation.updated
 *     → drop the primary nav (same as /api/revalidate-nav)
 *   settings.updated
 *     → drop the cached site settings (cms-site-settings) — issue #149 / epic #141
 *
 * Register it once against the API (same secret in both places):
 *   POST /api/v1/webhooks { name, url: "<site>/api/revalidate", secret, events: [...] }
 *
 * REVALIDATE_SECRET must match the webhook secret. If it is unset the signature is
 * not checked (local dev only) — a warning is logged.
 */
import { createHmac, timingSafeEqual } from 'node:crypto';
import { revalidateTag } from 'next/cache';
import { NextRequest, NextResponse } from 'next/server';
import { NAV_CACHE_TAG } from '@/lib/cms/navigation';
import { SITE_SETTINGS_CACHE_TAG } from '@/lib/cms/settings';
import { tagsForContentEvent, type ContentEventPayload } from '@/lib/cms/revalidation';

function signatureMatches(secret: string, rawBody: string, header: string | null): boolean {
  if (!header) return false;
  const provided = header.startsWith('sha256=') ? header.slice(7) : header;
  const expected = createHmac('sha256', secret).update(rawBody, 'utf8').digest('hex');
  const a = Buffer.from(provided.toLowerCase(), 'utf8');
  const b = Buffer.from(expected, 'utf8');
  return a.length === b.length && timingSafeEqual(a, b);
}

export async function POST(request: NextRequest): Promise<NextResponse> {
  const rawBody = await request.text();

  const secret = process.env.REVALIDATE_SECRET;
  if (secret) {
    if (!signatureMatches(secret, rawBody, request.headers.get('x-cms-signature'))) {
      return NextResponse.json({ error: 'Invalid signature' }, { status: 401 });
    }
  } else if (process.env.NODE_ENV !== 'production') {
    console.warn('[revalidate] REVALIDATE_SECRET is not set — accepting unsigned webhook (dev only).');
  } else {
    return NextResponse.json({ error: 'REVALIDATE_SECRET is not configured' }, { status: 503 });
  }

  const event = request.headers.get('x-cms-event') ?? '';
  let payload: ContentEventPayload = {};
  if (rawBody) {
    try {
      payload = JSON.parse(rawBody) as ContentEventPayload;
    } catch {
      return NextResponse.json({ error: 'Body is not JSON' }, { status: 400 });
    }
  }

  let tags: string[];
  if (event === 'navigation.updated') {
    tags = [NAV_CACHE_TAG];
  } else if (event === 'settings.updated') {
    tags = [SITE_SETTINGS_CACHE_TAG];
  } else if (event.startsWith('content.')) {
    tags = tagsForContentEvent(payload);
  } else {
    return NextResponse.json({ revalidated: false, ignored: event || '(none)' });
  }

  for (const tag of tags) revalidateTag(tag);
  return NextResponse.json({ revalidated: true, event, tags });
}
