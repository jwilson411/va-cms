/**
 * NotFoundTemplate — USWDS-compliant 404 error page template.
 *
 * Issue #61 — BRD FR-ERR-01
 * AC: 404 page uses USWDS Alert (info) and suggests search or home link.
 * AC: Includes Banner and Identifier.
 * AC: Passes axe-core with zero critical violations.
 *
 * This is a Server Component (no 'use client' directive).
 * It receives pre-fetched navigation from the calling page layer.
 *
 * USWDS classes used:
 *  - usa-alert usa-alert--info  — informational alert for page-not-found state
 *  - grid-container             — standard centered max-width container
 */

import React from 'react';
import { UswdsBanner } from '@/components/uswds/UswdsBanner';
import { UswdsHeader, NavItem } from '@/components/uswds/UswdsHeader';
import { UswdsFooter } from '@/components/uswds/UswdsFooter';
import { UswdsIdentifier } from '@/components/uswds/UswdsIdentifier';
import { DEFAULT_SITE_SETTINGS, type SiteChrome } from '@/lib/cms/settings';

export interface NotFoundTemplateProps {
  /** CMS-managed primary navigation items for the header */
  navigation: NavItem[];
  /** Site chrome (titles, agency, banner language) from CMS settings; code defaults when omitted. */
  site?: SiteChrome;
}

/**
 * Full-page 404 Not Found layout.
 *
 * Layout structure:
 *   <UswdsBanner lang={site.bannerLang} />        — top-of-page official gov banner (mandatory)
 *   <UswdsHeader />        — primary nav (mandatory)
 *   <main #main-content>
 *     <grid-container>
 *       <h1>              — page title
 *       <usa-alert info>  — informational message with search/home suggestion
 *     </grid-container>
 *   </main>
 *   <UswdsFooter />        — footer (mandatory)
 *   <UswdsIdentifier />    — identifier (mandatory)
 */
export function NotFoundTemplate({ navigation, site = DEFAULT_SITE_SETTINGS }: NotFoundTemplateProps): React.ReactElement {
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
          <h1>Page not found</h1>

          {/* USWDS Alert (info) — AC: suggests search or home link */}
          <div
            className="usa-alert usa-alert--info"
            role="region"
            aria-label="Page not found information"
          >
            <div className="usa-alert__body">
              <h2 className="usa-alert__heading">We can&apos;t find that page</h2>
              <p className="usa-alert__text">
                The page you&apos;re looking for may have moved or may no longer be
                available.
              </p>
              <ul className="usa-alert__text">
                <li>
                  Try our{' '}
                  <a href="/search" className="usa-link">
                    site search
                  </a>{' '}
                  to find what you need.
                </li>
                <li>
                  Return to the{' '}
                  <a href="/" className="usa-link">
                    home page
                  </a>
                  .
                </li>
              </ul>
            </div>
          </div>
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
