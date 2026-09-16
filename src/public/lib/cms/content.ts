/**
 * lib/cms/content.ts
 *
 * CMS content fetching client for the Next.js public site.
 *
 * Issue #58 — BRD FR-DEV-03 / FR-DEV-04
 * Issue #59 — News Article template
 *
 * NEXT_PUBLIC_API_URL / CMS_API_URL must be set for the API base URL.
 */

// ---------------------------------------------------------------------------
// Shared helpers (must come before any usage)
// ---------------------------------------------------------------------------

const getApiBase = (): string =>
  process.env.NEXT_PUBLIC_API_URL ??
  process.env.CMS_API_URL ??
  'http://localhost:5100';

// ---------------------------------------------------------------------------
// Shared types
// ---------------------------------------------------------------------------

/** Shape of a published content entry from GET /api/v1/content/{slug} */
export interface ContentEntry<TFields = Record<string, unknown>> {
  id: number;
  contentTypeId: number;
  contentTypeName: string;
  slug: string;
  locale: string;
  status: string;
  fields: TFields;
  publishedAt: string | null;
}

/**
 * A resolved media asset reference returned by the API.
 * Matches the shape produced when the API expands a MediaReference field.
 */
export interface MediaAssetRef {
  storageUrl: string;
  altText: string;
  width?: number;
  height?: number;
}

/** A resolved taxonomy term reference */
export interface TaxonomyTermRef {
  slug: string;
  name: string;
}

// ---------------------------------------------------------------------------
// Standard Page
// ---------------------------------------------------------------------------

/** Fields stored in FieldsJson for a Standard Page content type */
export interface StandardPageFields {
  title: string;
  body: string;
  /** Rendered HTML from Markdig on the server side */
  renderedBody?: string;
  /** Optional breadcrumb trail items (injected by API) */
  breadcrumbs?: Array<{ label: string; href: string }>;
}

/** Cache tag for Standard Page ISR revalidation. */
export const STANDARD_PAGE_CACHE_TAG = 'cms-standard-page';

/**
 * Fetch a published Standard Page content entry by slug.
 *
 * Returns null when:
 *  - The API returns a non-200 status (e.g. 404 = page not found)
 *  - Network or parse error
 *
 * Uses ISR tag-based revalidation so the cache invalidates when the
 * admin publishes or unpublishes the page.
 */
export async function fetchStandardPage(
  slug: string,
): Promise<ContentEntry<StandardPageFields> | null> {
  const url = `${getApiBase()}/api/v1/content/${encodeURIComponent(slug)}?type=standard_page`;

  try {
    const res = await fetch(url, {
      next: {
        revalidate: false,
        tags: [STANDARD_PAGE_CACHE_TAG, `cms-page-${slug}`],
      },
      headers: { Accept: 'application/json' },
    });

    if (res.status === 404) return null;

    if (!res.ok) {
      console.warn(`[content] Fetch returned ${res.status}: ${url}`);
      return null;
    }

    const data: ContentEntry<StandardPageFields> = await res.json();
    return data;
  } catch (err) {
    console.error('[content] Failed to fetch standard page:', err);
    return null;
  }
}

// ---------------------------------------------------------------------------
// News Article
// ---------------------------------------------------------------------------

/** Fields stored in FieldsJson for a News Article content type */
export interface NewsArticleFields {
  title: string;
  summary: string;
  body: string;
  /** Rendered HTML from Markdig on the server side */
  renderedBody?: string;
  /** Optional featured image expanded from MediaReference field */
  featuredImage?: MediaAssetRef | null;
  /** ISO 8601 publish date */
  publishDate?: string | null;
  /** Display name of the author (resolved from User or free-text field) */
  author?: string;
  /** Resolved taxonomy terms for the "topics" vocabulary */
  topics?: TaxonomyTermRef[];
}

/** Cache tag for News Article ISR revalidation. */
export const NEWS_ARTICLE_CACHE_TAG = 'cms-news-article';

/**
 * Fetch a published News Article content entry by slug.
 *
 * Returns null when:
 *  - The API returns a non-200 status (e.g. 404 = article not found)
 *  - Network or parse error
 *
 * Uses ISR tag-based revalidation so the cache invalidates when the
 * admin publishes or unpublishes the article.
 */
export async function fetchNewsArticle(
  slug: string,
): Promise<ContentEntry<NewsArticleFields> | null> {
  const url = `${getApiBase()}/api/v1/content/${encodeURIComponent(slug)}?type=news_article`;

  try {
    const res = await fetch(url, {
      next: {
        revalidate: false,
        tags: [NEWS_ARTICLE_CACHE_TAG, `cms-article-${slug}`],
      },
      headers: { Accept: 'application/json' },
    });

    if (res.status === 404) return null;

    if (!res.ok) {
      console.warn(`[content] Fetch returned ${res.status}: ${url}`);
      return null;
    }

    const data: ContentEntry<NewsArticleFields> = await res.json();
    return data;
  } catch (err) {
    console.error('[content] Failed to fetch news article:', err);
    return null;
  }
}

// ---------------------------------------------------------------------------
// HTML utilities (shared across template types)
// ---------------------------------------------------------------------------

/**
 * Extract headings (H2-level sections) from rendered HTML body.
 *
 * Used to determine whether In-Page Navigation should be rendered
 * (USWDS spec: render when there are 3 or more major H2 sections).
 *
 * Runs in server context via regex — no DOM dependency.
 */
export function extractH2Sections(
  renderedHtml: string,
): Array<{ id: string; text: string }> {
  const sections: Array<{ id: string; text: string }> = [];
  // Match <h2 ...> tags — Markdig renders semantic h2 from ## headings
  const h2Regex = /<h2(?:[^>]* id="([^"]*)"[^>]*)?>([^<]*)<\/h2>/gi;
  let match: RegExpExecArray | null;

  while ((match = h2Regex.exec(renderedHtml)) !== null) {
    const rawText = match[2].trim();
    // Generate a slug-style id if Markdig didn't add one
    const id =
      match[1] ??
      rawText
        .toLowerCase()
        .replace(/[^a-z0-9\s-]/g, '')
        .replace(/\s+/g, '-');
    if (rawText) {
      sections.push({ id, text: rawText });
    }
  }

  return sections;
}

/**
 * Inject anchor ids into H2 headings in rendered HTML so In-Page Nav links work.
 *
 * Markdig does not automatically emit ids on headings (no AutoIdentifier extension
 * in the safe pipeline). We add them here during server rendering.
 */
export function injectH2Ids(renderedHtml: string): string {
  return renderedHtml.replace(/<h2(?![^>]*\bid=)([^>]*)>(.*?)<\/h2>/gi, (_, attrs, inner) => {
    const text = inner.replace(/<[^>]+>/g, '').trim();
    const id = text
      .toLowerCase()
      .replace(/[^a-z0-9\s-]/g, '')
      .replace(/\s+/g, '-');
    return `<h2 id="${id}"${attrs}>${inner}</h2>`;
  });
}
