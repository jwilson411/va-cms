/**
 * TermLandingPageTemplate — USWDS-compliant taxonomy term landing page template.
 *
 * Issue #63 — BRD NFR-A11Y-03 / Epic #14 Section 508 & Accessibility Hardening
 * AC: axe-core test exists for Term Landing Page.
 * AC: No critical or serious violations.
 *
 * Renders a taxonomy term page showing the term name, description, and a
 * paginated list of content entries tagged with that term.
 *
 * This is a Server Component (no 'use client' directive).
 * It receives pre-fetched data from the Next.js page layer.
 *
 * USWDS classes used:
 *  - usa-prose       — typographically-correct body text
 *  - usa-card        — content entry cards
 *  - grid-container  — standard centered max-width container
 *  - usa-tag         — term badge
 */

import React from 'react';
import { UswdsBanner } from '@/components/uswds/UswdsBanner';
import { UswdsHeader, NavItem } from '@/components/uswds/UswdsHeader';
import { UswdsFooter } from '@/components/uswds/UswdsFooter';
import { UswdsIdentifier } from '@/components/uswds/UswdsIdentifier';
import { UswdsBreadcrumb, BreadcrumbItem } from '@/components/uswds/UswdsBreadcrumb';

/** A single content entry associated with this term */
export interface TermEntryItem {
  id: number;
  title: string | null;
  slug: string;
  excerpt: string | null;
  contentTypeName: string | null;
  publishedAt: string | null;
}

export interface TermLandingPageTemplateProps {
  /** The taxonomy (e.g. "Topics") this term belongs to */
  taxonomyName: string;
  /** The human-readable term name (e.g. "Mental Health") */
  termName: string;
  /** Optional plain-text description of the term */
  description?: string | null;
  /** Content entries tagged with this term */
  entries: TermEntryItem[];
  /** Total number of matching entries (across all pages) */
  totalEntries: number;
  /** CMS-managed primary navigation items for the header */
  navigation: NavItem[];
  /** Optional breadcrumb trail */
  breadcrumbs?: BreadcrumbItem[];
}

/**
 * Full-page Term Landing Page layout.
 *
 * Layout structure:
 *   <UswdsBanner />              — top-of-page official gov banner (mandatory)
 *   <UswdsHeader />              — primary nav (mandatory)
 *   <main #main-content>
 *     <grid-container>
 *       [<UswdsBreadcrumb />]    — breadcrumb trail (optional)
 *       <h1>                     — term name
 *       <usa-tag>                — taxonomy badge
 *       [<p.usa-intro>]          — description (optional)
 *       <p role="status">        — result count
 *       <ul.usa-card-group>      — entry cards
 *     </grid-container>
 *   </main>
 *   <UswdsFooter />              — footer (mandatory)
 *   <UswdsIdentifier />          — identifier (mandatory)
 */
export function TermLandingPageTemplate({
  taxonomyName,
  termName,
  description,
  entries,
  totalEntries,
  navigation,
  breadcrumbs = [],
}: TermLandingPageTemplateProps): React.ReactElement {
  return (
    <>
      {/* Mandatory: Official government banner — top of every public page */}
      <UswdsBanner />

      {/* Mandatory: USWDS extended header with CMS-managed navigation */}
      <UswdsHeader
        siteTitle="Department of Veterans Affairs"
        navigation={navigation}
      />

      <main id="main-content" tabIndex={-1}>
        <div className="grid-container">
          {/* Breadcrumb navigation */}
          {breadcrumbs.length > 0 && <UswdsBreadcrumb items={breadcrumbs} />}

          {/* Term heading — always H1, one per page */}
          <h1>{termName}</h1>

          {/* Taxonomy badge — labelled for screen readers */}
          <span
            className="usa-tag"
            aria-label={`Taxonomy: ${taxonomyName}`}
          >
            {taxonomyName}
          </span>

          {/* Optional term description */}
          {description && (
            <p className="usa-intro margin-top-2">{description}</p>
          )}

          {/* Result count — live region so assistive tech announces updates */}
          <p
            className="usa-prose margin-top-3"
            role="status"
            aria-live="polite"
            aria-atomic="true"
          >
            {totalEntries === 0 ? (
              <>No content tagged with <strong>{termName}</strong>.</>
            ) : (
              <>
                Showing <strong>{entries.length}</strong> of{' '}
                <strong>{totalEntries.toLocaleString()}</strong> items tagged{' '}
                <strong>{termName}</strong>.
              </>
            )}
          </p>

          {/* Content entry cards */}
          {entries.length > 0 && (
            <ul
              className="usa-card-group margin-top-3"
              aria-label={`Content tagged ${termName}`}
            >
              {entries.map((entry) => (
                <li key={entry.id} className="usa-card tablet:grid-col-6 desktop:grid-col-4">
                  <div className="usa-card__container">
                    <div className="usa-card__header">
                      <h2 className="usa-card__heading">
                        <a href={`/${entry.slug}`} className="usa-link">
                          {entry.title ?? entry.slug}
                        </a>
                      </h2>
                    </div>
                    {entry.excerpt && (
                      <div className="usa-card__body">
                        <p>{entry.excerpt}</p>
                      </div>
                    )}
                    <div className="usa-card__footer">
                      {entry.contentTypeName && (
                        <span
                          className="usa-tag"
                          aria-label={`Content type: ${entry.contentTypeName}`}
                        >
                          {entry.contentTypeName}
                        </span>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}

          {/* Empty state */}
          {entries.length === 0 && (
            <div
              className="usa-alert usa-alert--info usa-alert--slim margin-top-3"
              role="region"
              aria-label="No content found"
            >
              <div className="usa-alert__body">
                <p className="usa-alert__text">
                  No content has been tagged with <strong>{termName}</strong> yet.{' '}
                  <a href="/" className="usa-link">
                    Return to the home page
                  </a>
                  .
                </p>
              </div>
            </div>
          )}
        </div>
      </main>

      {/* Mandatory: USWDS big footer */}
      <UswdsFooter
        agencyName="Department of Veterans Affairs"
        agencyHref="https://www.va.gov"
      />

      {/* Mandatory: USWDS Identifier — required by 21st Century IDEA Act */}
      <UswdsIdentifier
        agencyName="Department of Veterans Affairs"
        agencyShortName="VA"
        agencyHref="https://www.va.gov"
      />
    </>
  );
}
