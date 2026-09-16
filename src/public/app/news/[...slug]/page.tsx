/**
 * app/news/[...slug]/page.tsx — News Article route.
 *
 * Catch-all: CMS slugs may contain "/" (e.g. demo/news/va-cms-launched),
 * so /news/a/b/c resolves the slug "a/b/c".
 *
 * Issue #59 — Build News Article public template
 * AC: Template renders: Banner, Header, Breadcrumb, article header (title,
 *     author, date, featured image), usa-prose body, tags, Footer, Identifier.
 * AC: Featured image has alt text from media asset.
 * AC: Structured data (JSON-LD Article) in page <head>.
 * AC: axe-core zero critical violations.
 *
 * This is a Next.js 14 App Router Server Component.
 * ISR: cache is tagged per-slug; revalidated via revalidateTag() when
 * the admin publishes or unpublishes the article.
 *
 * 404: notFound() is called when the CMS returns null for the slug.
 */

import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import { fetchNewsArticle } from '@/lib/cms/content';
import { fetchPrimaryNav } from '@/lib/cms/navigation';
import { NewsArticleTemplate } from '@/components/templates/NewsArticleTemplate';
import type { BreadcrumbItem } from '@/components/uswds/UswdsBreadcrumb';

interface PageProps {
  params: { slug: string[] };
}

/** Join the catch-all segments back into the CMS slug. */
const slugFromParams = (params: PageProps['params']): string => params.slug.join('/');

/**
 * Canonical URL for this article — used in JSON-LD and <link rel="canonical">.
 * Reads NEXT_PUBLIC_SITE_URL or falls back to relative path.
 */
function buildCanonicalUrl(slug: string): string {
  const base = process.env.NEXT_PUBLIC_SITE_URL ?? '';
  return `${base}/news/${slug}`;
}

/**
 * Generate Next.js metadata (title, description, OG tags) from the CMS article.
 * Runs on the server alongside the page component.
 */
export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const article = await fetchNewsArticle(slugFromParams(params));
  if (!article) return { title: 'Page Not Found' };

  const canonicalUrl = buildCanonicalUrl(slugFromParams(params));

  return {
    title: `${article.fields.title} | Department of Veterans Affairs`,
    description: article.fields.summary,
    alternates: {
      canonical: canonicalUrl,
    },
    openGraph: {
      title: article.fields.title,
      description: article.fields.summary,
      ...(article.fields.featuredImage && {
        images: [
          {
            url: article.fields.featuredImage.storageUrl,
            alt: article.fields.featuredImage.altText,
          },
        ],
      }),
    },
  };
}

/**
 * News Article route — server component.
 *
 * 1. Fetches the article entry from the CMS API by slug
 * 2. Fetches CMS-managed primary navigation (shared ISR cache)
 * 3. Passes all fields to NewsArticleTemplate for rendering
 */
export default async function NewsArticlePage({
  params,
}: PageProps): Promise<React.ReactElement> {
  const [article, navigation] = await Promise.all([
    fetchNewsArticle(slugFromParams(params)),
    fetchPrimaryNav(),
  ]);

  if (!article) {
    notFound();
  }

  // Use pre-rendered body from API (renderedBody) if available;
  // fall back to raw markdown display as plain text (dev/preview safety).
  const renderedBody = article.fields.renderedBody ?? `<p>${article.fields.body}</p>`;

  // Build breadcrumb trail: Home > News > article title (current)
  const breadcrumbs: BreadcrumbItem[] = [
    { label: 'Home', href: '/' },
    { label: 'News', href: '/news' },
    { label: article.fields.title },
  ];

  const canonicalUrl = buildCanonicalUrl(slugFromParams(params));

  return (
    <NewsArticleTemplate
      title={article.fields.title}
      renderedBody={renderedBody}
      author={article.fields.author}
      publishedAt={article.fields.publishDate ?? article.publishedAt}
      featuredImage={article.fields.featuredImage ?? null}
      tags={article.fields.topics ?? []}
      breadcrumbs={breadcrumbs}
      navigation={navigation}
      canonicalUrl={canonicalUrl}
    />
  );
}
