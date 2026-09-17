/**
 * ServerErrorTemplate — USWDS-compliant 500 server error page template.
 *
 * Issue #61 — BRD FR-ERR-02
 * AC: 500 page uses USWDS Alert (error) with a friendly plain-language message.
 * AC: Includes Banner and Identifier.
 * AC: Passes axe-core with zero critical violations.
 *
 * This is a Server Component (no 'use client' directive).
 * It receives pre-fetched navigation from the calling page layer.
 *
 * USWDS classes used:
 *  - usa-alert usa-alert--error — error alert for server failure state
 *  - grid-container             — standard centered max-width container
 */

import React from 'react';
import { UswdsBanner } from '@/components/uswds/UswdsBanner';
import { UswdsHeader, NavItem } from '@/components/uswds/UswdsHeader';
import { UswdsFooter } from '@/components/uswds/UswdsFooter';
import { UswdsIdentifier } from '@/components/uswds/UswdsIdentifier';
import { DEFAULT_SITE_SETTINGS, type SiteChrome } from '@/lib/cms/settings';

export interface ServerErrorTemplateProps {
  /** CMS-managed primary navigation items for the header */
  navigation: NavItem[];
  /** Site chrome (titles, agency, banner language) from CMS settings; code defaults when omitted. */
  site?: SiteChrome;
}

/**
 * Full-page 500 Server Error layout.
 *
 * Layout structure:
 *   <UswdsBanner lang={site.bannerLang} />        — top-of-page official gov banner (mandatory)
 *   <UswdsHeader />        — primary nav (mandatory)
 *   <main #main-content>
 *     <grid-container>
 *       <h1>               — page title
 *       <usa-alert error>  — friendly plain-language error message
 *     </grid-container>
 *   </main>
 *   <UswdsFooter />        — footer (mandatory)
 *   <UswdsIdentifier />    — identifier (mandatory)
 */
export function ServerErrorTemplate({
  navigation,
  site = DEFAULT_SITE_SETTINGS,
}: ServerErrorTemplateProps): React.ReactElement {
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
          <h1>Something went wrong</h1>

          {/* USWDS Alert (error) — AC: friendly plain-language message */}
          <div
            className="usa-alert usa-alert--error"
            role="alert"
            aria-live="assertive"
          >
            <div className="usa-alert__body">
              <h2 className="usa-alert__heading">We&apos;re sorry — an error occurred</h2>
              <p className="usa-alert__text">
                We ran into a technical problem on our end. Our team has been notified
                and is working to fix the issue.
              </p>
              <p className="usa-alert__text">
                Please try again in a few minutes. If the problem continues, return to
                the{' '}
                <a href="/" className="usa-link">
                  home page
                </a>
                .
              </p>
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
