/**
 * StandardPageTemplate — USWDS-compliant Standard Page template.
 *
 * Issue #58 — BRD FR-DEV-03
 * AC: Template renders: Banner, Header (with CMS nav), Breadcrumb, H1 (page
 *     title), usa-prose body, Footer, Identifier.
 * AC: In-page navigation renders for pages with 3+ major H2 sections.
 *
 * This is a Server Component (no 'use client' directive). It receives
 * pre-fetched data from the Next.js page layer.
 *
 * USWDS classes used:
 *  - usa-prose      — typographically-correct body text container
 *  - grid-container — standard centered max-width container
 *  - grid-row       — USWDS flex grid row
 *  - grid-col       — USWDS flex grid column
 */

import React from 'react';
import { UswdsBanner } from '@/components/uswds/UswdsBanner';
import { UswdsHeader, NavItem } from '@/components/uswds/UswdsHeader';
import { UswdsFooter } from '@/components/uswds/UswdsFooter';
import { UswdsIdentifier } from '@/components/uswds/UswdsIdentifier';
import { UswdsBreadcrumb, BreadcrumbItem } from '@/components/uswds/UswdsBreadcrumb';
import { UswdsInPageNav, InPageNavSection } from '@/components/uswds/UswdsInPageNav';
import { DEFAULT_SITE_SETTINGS, type SiteChrome } from '@/lib/cms/settings';

export interface StandardPageTemplateProps {
  /** Page title — rendered as H1 */
  title: string;
  /**
   * Rendered HTML body (from Markdig on the server).
   * Must already have anchor ids injected on H2 headings (injectH2Ids()).
   */
  renderedBody: string;
  /** Breadcrumb trail, e.g. [{label:'Home',href:'/'}, {label:'About'}] */
  breadcrumbs?: BreadcrumbItem[];
  /**
   * H2 sections extracted from the body for In-Page Nav.
   * Pass empty array or omit to suppress the nav.
   * Nav renders only when sections.length >= 3 (USWDS spec).
   */
  sections?: InPageNavSection[];
  /** CMS-managed primary navigation items for the header */
  navigation: NavItem[];
  /** Site chrome (titles, agency, banner language) from CMS settings; code defaults when omitted. */
  site?: SiteChrome;
}

/**
 * Full-page Standard Page layout.
 *
 * Layout structure:
 *   <UswdsBanner lang={site.bannerLang} />       — top-of-page official gov banner (mandatory)
 *   <UswdsHeader />       — primary nav (mandatory)
 *   <main #main-content>
 *     <grid-container>
 *       <UswdsBreadcrumb />  — breadcrumb trail
 *       <h1>                 — page title
 *       <grid-row>
 *         [<UswdsInPageNav />] — sidebar, 3+ sections only
 *         <article.usa-prose> — body content
 *       </grid-row>
 *     </grid-container>
 *   </main>
 *   <UswdsFooter />        — footer (mandatory)
 *   <UswdsIdentifier />    — identifier (mandatory)
 */
export function StandardPageTemplate({
  title,
  renderedBody,
  breadcrumbs = [],
  sections = [],
  navigation,
  site = DEFAULT_SITE_SETTINGS,
}: StandardPageTemplateProps): React.ReactElement {
  const hasInPageNav = sections.length >= 3;

  return (
    <>
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

          {/* Page title — always H1, one per page */}
          <h1>{title}</h1>

          {hasInPageNav ? (
            /* Two-column layout: in-page nav sidebar + prose body */
            <div className="grid-row grid-gap">
              <aside className="desktop:grid-col-3">
                <UswdsInPageNav sections={sections} />
              </aside>
              <div className="desktop:grid-col-9">
                <article
                  className="usa-prose"
                  /* Rendered body is Markdig output — safe (DisableHtml() in pipeline) */
                  dangerouslySetInnerHTML={{ __html: renderedBody }}
                />
              </div>
            </div>
          ) : (
            /* Single-column prose layout */
            <article
              className="usa-prose"
              dangerouslySetInnerHTML={{ __html: renderedBody }}
            />
          )}
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
