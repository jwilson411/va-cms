/**
 * lib/cms/revalidation.ts — which ISR cache tags a CMS webhook event invalidates.
 * Kept out of the route file because Next.js only allows HTTP handlers to be
 * exported from app/api/** /route.ts.
 */
import { NEWS_ARTICLE_CACHE_TAG, STANDARD_PAGE_CACHE_TAG } from './content';
import { REDIRECTS_CACHE_TAG } from './redirects';

export interface ContentEventPayload {
  id?: number;
  slug?: string;
  contentTypeName?: string | null;
  status?: string;
}

/** Cache tags to drop for a content.* webhook event. */
export function tagsForContentEvent(payload: ContentEventPayload): string[] {
  const tags = new Set<string>();
  if (payload.slug) {
    tags.add(`cms-page-${payload.slug}`);
    tags.add(`cms-article-${payload.slug}`);
  }
  switch (payload.contentTypeName) {
    case 'news_article':
      tags.add(NEWS_ARTICLE_CACHE_TAG);
      break;
    case 'standard_page':
      tags.add(STANDARD_PAGE_CACHE_TAG);
      break;
    default:
      // Unknown/omitted type: refresh both listings rather than miss one.
      tags.add(NEWS_ARTICLE_CACHE_TAG);
      tags.add(STANDARD_PAGE_CACHE_TAG);
  }
  return [...tags];
}

/** Payload of a redirects.updated event; the slug fields are present only for a slug change. */
export interface RedirectsEventPayload extends ContentEventPayload {
  previousSlug?: string;
  fromPath?: string;
  toPath?: string;
}

/**
 * Cache tags to drop for a redirects.updated webhook event (#169): the redirect
 * lookups always; for a slug change also the pages cached under the old slug (now a
 * redirect) and the new one (now the live page).
 */
export function tagsForRedirectsEvent(payload: RedirectsEventPayload): string[] {
  const tags = new Set<string>([REDIRECTS_CACHE_TAG]);
  for (const slug of [payload.slug, payload.previousSlug]) {
    if (!slug) continue;
    tags.add(`cms-page-${slug}`);
    tags.add(`cms-article-${slug}`);
  }
  if (payload.slug || payload.previousSlug) {
    for (const tag of tagsForContentEvent({ contentTypeName: payload.contentTypeName })) tags.add(tag);
  }
  return [...tags];
}
