/**
 * app/pages/[...slug]/page.tsx — Standard Page route.
 *
 * Catch-all: CMS slugs may contain "/" (section prefixes such as
 * health-care/eligibility), so /pages/a/b/c resolves the slug "a/b/c".
 *
 * Issue #58 — BRD FR-DEV-03, FR-DEV-04
 * AC: Template renders published CMS Standard Page content.
 *     Includes: Banner, Header (CMS nav), Breadcrumb, H1, usa-prose body,
 *               Footer, Identifier.
 *     In-Page Nav renders for pages with 3+ major H2 sections.
 *
 * This is a Next.js 14 App Router Server Component.
 * ISR: cache is tagged per-slug; revalidated via revalidateTag() when
 * the admin publishes or unpublishes the page.
 *
 * 404: notFound() is called when the CMS returns null for the slug — after the
 * redirect table has been checked (#169), so a renamed slug lands on its new URL.
 */

import { notFound } from 'next/navigation';
import { redirectIfMoved } from '@/lib/cms/redirect-if-moved';
import type { Metadata } from 'next';
import { fetchStandardPage, extractH2Sections, injectH2Ids } from '@/lib/cms/content';
import { fetchPrimaryNav } from '@/lib/cms/navigation';
import { fetchSiteSettings } from '@/lib/cms/settings';
import { StandardPageTemplate } from '@/components/templates/StandardPageTemplate';
import { BreadcrumbItem } from '@/components/uswds/UswdsBreadcrumb';

interface PageProps {
  /** Next 15+: route params resolve asynchronously. */
  params: Promise<{ slug: string[] }>;
}

/** Join the catch-all segments back into the CMS slug. */
const slugFromParams = async (params: PageProps['params']): Promise<string> => (await params).slug.join('/');

/**
 * Generate Next.js metadata (title tag) from the CMS page title.
 * Runs on the server alongside the page component.
 */
export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const [page, site] = await Promise.all([fetchStandardPage(await slugFromParams(params)), fetchSiteSettings()]);
  if (!page) return { title: 'Page Not Found' };

  return {
    title: `${page.fields.title} | ${site.siteTitle}`,
    description: undefined, // Standard pages do not have a summary field in #58 scope
  };
}

/**
 * Standard Page route — server component.
 *
 * 1. Fetches the page entry from the CMS API by slug
 * 2. Fetches CMS-managed primary navigation (shared ISR cache)
 * 3. Injects anchor IDs into H2 headings for In-Page Nav
 * 4. Extracts H2 sections to decide whether to show In-Page Nav
 * 5. Renders StandardPageTemplate with all required USWDS chrome
 */
export default async function StandardPage({ params }: PageProps): Promise<React.ReactElement> {
  const slug = await slugFromParams(params);
  const [page, navigation, site] = await Promise.all([fetchStandardPage(slug), fetchPrimaryNav(), fetchSiteSettings()]);

  if (!page) {
    await redirectIfMoved(`/pages/${slug}`);
    notFound();
  }

  // Use pre-rendered body from API (renderedBody) if available;
  // fall back to raw markdown display as plain text (dev/preview safety).
  const rawHtml = page.fields.renderedBody ?? `<p>${page.fields.body}</p>`;

  // Inject anchor ids on H2 headings so In-Page Nav links work
  const bodyHtml = injectH2Ids(rawHtml);

  // Extract H2 sections to determine In-Page Nav eligibility (threshold: 3)
  const sections = extractH2Sections(bodyHtml);

  // Build breadcrumb trail: Home > page title (current)
  // The API may provide a richer trail via fields.breadcrumbs
  const breadcrumbs: BreadcrumbItem[] =
    page.fields.breadcrumbs && page.fields.breadcrumbs.length > 0
      ? page.fields.breadcrumbs
      : [
          { label: 'Home', href: '/' },
          { label: page.fields.title },
        ];

  return (
    <StandardPageTemplate
      title={page.fields.title}
      renderedBody={bodyHtml}
      breadcrumbs={breadcrumbs}
      sections={sections}
      navigation={navigation}
      site={site}
    />
  );
}
