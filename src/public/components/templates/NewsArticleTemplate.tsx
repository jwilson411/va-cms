/**
 * NewsArticleTemplate — USWDS-compliant News Article public template.
 *
 * Issue #59 — BRD FR-DEV-03
 * AC: Template renders: Banner, Header, Breadcrumb, article header (title,
 *     author, date, featured image), usa-prose body, tags, Footer, Identifier.
 * AC: Featured image has alt text from media asset.
 * AC: Structured data (JSON-LD Article) in page <head>.
 * AC: axe-core zero critical violations.
 *
 * This is a Server Component (no 'use client' directive). It receives
 * pre-fetched data from the Next.js page layer.
 *
 * USWDS classes used:
 *  - usa-prose      — typographically-correct body text container
 *  - grid-container — standard centered max-width container
 *  - grid-row       — USWDS flex grid row
 *  - grid-col       — USWDS flex grid column
 *  - usa-tag        — USWDS tag component for taxonomy terms
 */

import React from 'react';
import { UswdsBanner } from '@/components/uswds/UswdsBanner';
import { UswdsHeader, NavItem } from '@/components/uswds/UswdsHeader';
import { UswdsFooter } from '@/components/uswds/UswdsFooter';
import { UswdsIdentifier } from '@/components/uswds/UswdsIdentifier';
import { UswdsBreadcrumb, BreadcrumbItem } from '@/components/uswds/UswdsBreadcrumb';
import { DEFAULT_SITE_SETTINGS, type SiteChrome } from '@/lib/cms/settings';

/** A media asset reference returned by the API for featuredImage fields */
export interface FeaturedImage {
  /** Storage URL for the image file */
  storageUrl: string;
  /** Required alt text from the MediaAsset record */
  altText: string;
  /** Optional width in pixels */
  width?: number;
  /** Optional height in pixels */
  height?: number;
}

/** A resolved taxonomy tag term */
export interface ArticleTag {
  /** Term slug */
  slug: string;
  /** Display name */
  name: string;
}

export interface NewsArticleTemplateProps {
  /** Per-request CSP nonce for the JSON-LD script (#162). */
  nonce?: string;
  /** Article title — rendered as H1 in the article header */
  title: string;
  /**
   * Rendered HTML body (from Markdig on the server).
   * Already safe (DisableHtml() in Markdig pipeline — no raw HTML passthrough).
   */
  renderedBody: string;
  /** Display name of the content author */
  author?: string;
  /**
   * ISO 8601 publish date string (e.g. "2026-09-15T00:00:00Z").
   * Rendered in a <time> element with a human-readable format.
   */
  publishedAt?: string | null;
  /** Featured image with required alt text. Omit to suppress the image. */
  featuredImage?: FeaturedImage | null;
  /** Resolved taxonomy tags to display beneath the article */
  tags?: ArticleTag[];
  /** Breadcrumb trail e.g. [{label:'Home',href:'/'}, {label:'News',href:'/news'}, {label:title}] */
  breadcrumbs?: BreadcrumbItem[];
  /** CMS-managed primary navigation items for the header */
  navigation: NavItem[];
  /** Site chrome (titles, agency, banner language) from CMS settings; code defaults when omitted. */
  site?: SiteChrome;
  /** Canonical URL of this article (used in JSON-LD) */
  canonicalUrl?: string;
}

/**
 * Formats an ISO 8601 date string into a human-readable date (e.g. "September 15, 2026").
 * Returns the original string unchanged if parsing fails.
 */
function formatPublishDate(isoDate: string): string {
  try {
    return new Date(isoDate).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      timeZone: 'UTC',
    });
  } catch {
    return isoDate;
  }
}

/**
 * Builds a JSON-LD Article structured data object for a news article.
 * Injected into <head> via <script type="application/ld+json">.
 */
function buildArticleJsonLd(props: NewsArticleTemplateProps): string {
  const { title, author, publishedAt, featuredImage, canonicalUrl } = props;
  const site = props.site ?? DEFAULT_SITE_SETTINGS;

  const jsonLd: Record<string, unknown> = {
    '@context': 'https://schema.org',
    '@type': 'NewsArticle',
    headline: title,
    ...(author && {
      author: {
        '@type': 'Person',
        name: author,
      },
    }),
    ...(publishedAt && { datePublished: publishedAt }),
    ...(featuredImage && {
      image: featuredImage.storageUrl,
    }),
    ...(canonicalUrl && { url: canonicalUrl }),
    publisher: {
      '@type': 'Organization',
      name: site.agencyName,
      url: site.agencyHref,
    },
  };

  return JSON.stringify(jsonLd);
}

/**
 * Full-page News Article layout.
 *
 * Layout structure:
 *   <UswdsBanner lang={site.bannerLang} />       — top-of-page official gov banner (mandatory)
 *   <UswdsHeader />       — primary nav (mandatory)
 *   <main #main-content>
 *     <grid-container>
 *       <UswdsBreadcrumb />        — breadcrumb trail
 *       <article>
 *         <header>                 — article header: title H1, author, date, image
 *         <div.usa-prose>         — rendered body content
 *         <footer.usa-article-footer> — tags
 *       </article>
 *     </grid-container>
 *   </main>
 *   <UswdsFooter />        — footer (mandatory)
 *   <UswdsIdentifier />    — identifier (mandatory)
 *
 * JSON-LD Article structured data is injected via dangerouslySetInnerHTML on a
 * <script> tag in the article header. The JSON is built from trusted CMS fields
 * (no user-controlled HTML) so this is safe.
 */
export function NewsArticleTemplate({
  title,
  renderedBody,
  author,
  publishedAt,
  featuredImage,
  tags = [],
  breadcrumbs = [],
  navigation,
  site = DEFAULT_SITE_SETTINGS,
  canonicalUrl,
  nonce,
}: NewsArticleTemplateProps): React.ReactElement {
  const jsonLd = buildArticleJsonLd({
    title,
    renderedBody,
    author,
    publishedAt,
    featuredImage,
    tags,
    breadcrumbs,
    navigation,
    site,
    canonicalUrl,
  });

  return (
    <>
      {/* JSON-LD Article structured data injected into the document head */}
      <script
        type="application/ld+json"
        nonce={nonce}
        // eslint-disable-next-line react/no-danger
        dangerouslySetInnerHTML={{ __html: jsonLd }}
      />

      {/* Mandatory: Official government banner — top of every public page */}
      <UswdsBanner lang={site.bannerLang} />

      {/* Mandatory: USWDS extended header with CMS-managed navigation */}
      <UswdsHeader
        siteTitle={site.siteTitle}
        showSearch={site.publicSearchEnabled}
        navigation={navigation}
      />

      <main id="main-content" tabIndex={-1}>
        <div className="grid-container">
          {/* Breadcrumb navigation */}
          {breadcrumbs.length > 0 && <UswdsBreadcrumb items={breadcrumbs} />}

          <article>
            {/* Article header: title, byline, date, featured image */}
            <header className="usa-article-header">
              {/* Article title — always H1, one per page */}
              <h1>{title}</h1>

              {/* Byline and publication date */}
              {(author || publishedAt) && (
                <div className="usa-article-byline">
                  {author && (
                    <span className="usa-article-byline__author">
                      By{' '}
                      <span className="usa-article-byline__name">{author}</span>
                    </span>
                  )}
                  {author && publishedAt && (
                    <span className="usa-article-byline__separator" aria-hidden="true">
                      {' '}
                      |{' '}
                    </span>
                  )}
                  {publishedAt && (
                    <time
                      className="usa-article-byline__date"
                      dateTime={publishedAt}
                    >
                      {formatPublishDate(publishedAt)}
                    </time>
                  )}
                </div>
              )}

              {/* Featured image with required alt text from MediaAsset */}
              {featuredImage && (
                <figure className="usa-article-figure">
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={featuredImage.storageUrl}
                    alt={featuredImage.altText}
                    className="usa-article-figure__image"
                    {...(featuredImage.width && { width: featuredImage.width })}
                    {...(featuredImage.height && { height: featuredImage.height })}
                  />
                </figure>
              )}
            </header>

            {/* Article body — Markdig-rendered HTML (DisableHtml() makes this safe) */}
            <div
              className="usa-prose"
              // Rendered body is Markdig output — safe (DisableHtml() in pipeline)
              // eslint-disable-next-line react/no-danger
              dangerouslySetInnerHTML={{ __html: renderedBody }}
            />

            {/* Article footer: taxonomy tags */}
            {tags.length > 0 && (
              <footer className="usa-article-footer">
                <ul className="usa-article-tags" aria-label="Article tags">
                  {tags.map((tag) => (
                    <li key={tag.slug} className="usa-article-tags__item">
                      <a
                        href={`/topics/${tag.slug}`}
                        className="usa-tag"
                      >
                        {tag.name}
                      </a>
                    </li>
                  ))}
                </ul>
              </footer>
            )}
          </article>
        </div>
      </main>

      {/* Mandatory: USWDS big footer */}
      <UswdsFooter
        agencyName={site.agencyName}
        agencyHref={site.agencyHref}
        agencyLogoSrc={site.agencyLogoSrc || undefined}
      />

      {/* Mandatory: USWDS Identifier — required by 21st Century IDEA Act */}
      <UswdsIdentifier
        agencyName={site.agencyName}
        agencyShortName={site.agencyShortName}
        agencyHref={site.agencyHref}
        agencyLogoSrc={site.agencyLogoSrc || undefined}
      />
    </>
  );
}
