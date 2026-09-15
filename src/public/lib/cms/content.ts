/**
 * lib/cms/content.ts
 *
 * CMS content fetching client for the Next.js public site.
 *
 * Issue #58 — BRD FR-DEV-03 / FR-DEV-04
 * AC: Standard Page template renders published CMS content from the API.
 *
 * NEXT_PUBLIC_API_URL / CMS_API_URL must be set for the API base URL.
 */

/** Fields stored in FieldsJson for a Standard Page content type */
export interface StandardPageFields {
  title: string;
  body: string;
  /** Rendered HTML from Markdig on the server side */
  renderedBody?: string;
  /** Optional breadcrumb trail items (injected by API) */
  breadcrumbs?: Array<{ label: string; href: string }>;
}

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

/** Cache tag for Standard Page ISR revalidation. */
export const STANDARD_PAGE_CACHE_TAG = 'cms-standard-page';

const getApiBase = (): string =>
  process.env.NEXT_PUBLIC_API_URL ??
  process.env.CMS_API_URL ??
  'http://localhost:5000';

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
