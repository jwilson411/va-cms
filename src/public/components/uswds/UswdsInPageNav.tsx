/**
 * UswdsInPageNav — USWDS In-Page Navigation component.
 *
 * Renders the usa-in-page-nav sidebar navigation for long-form pages
 * that have 3 or more major H2 sections.
 *
 * Per USWDS spec: render only when there are 3+ major sections.
 * The links scroll to anchor IDs already injected into the rendered body.
 *
 * BRD NFR-A11Y-02: USWDS components only.
 * BRD Issue #58 AC: In-page navigation renders for pages with 3+ major sections.
 */

import React from 'react';

export interface InPageNavSection {
  /** Anchor id matching the H2 heading in the body content */
  id: string;
  /** Display label for the nav link */
  text: string;
}

export interface UswdsInPageNavProps {
  /** The H2 sections extracted from the page body */
  sections: InPageNavSection[];
  /** Accessible heading label for the in-page nav (defaults to "On this page") */
  navLabel?: string;
}

/**
 * Renders the USWDS in-page navigation block.
 * Returns null if sections.length < 3 (USWDS spec threshold).
 */
export function UswdsInPageNav({
  sections,
  navLabel = 'On this page',
}: UswdsInPageNavProps): React.ReactElement | null {
  if (!sections || sections.length < 3) return null;

  return (
    <nav
      aria-label={navLabel}
      className="usa-in-page-nav"
    >
      <div className="usa-in-page-nav__header">
        <h4 className="usa-in-page-nav__heading">{navLabel}</h4>
      </div>
      <ul className="usa-in-page-nav__list">
        {sections.map((section) => (
          <li key={section.id} className="usa-in-page-nav__item usa-in-page-nav__item--primary">
            <a
              className="usa-in-page-nav__link"
              href={`#${section.id}`}
            >
              {section.text}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}
