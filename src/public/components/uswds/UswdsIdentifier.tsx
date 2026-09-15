/**
 * UswdsIdentifier — USWDS Identifier component for VA public pages.
 *
 * Renders agency identity information with required government links as
 * mandated by the 21st Century IDEA Act and USWDS 3.x Identifier spec.
 *
 * BRD NFR-A11Y-02: must come from @uswds/uswds, not rewritten.
 */

import React from 'react';

export interface IdentifierLink {
  href: string;
  text: string;
}

export interface UswdsIdentifierProps {
  /** Full agency name, e.g. "Department of Veterans Affairs" */
  agencyName: string;
  /** Short agency abbreviation, e.g. "VA" */
  agencyShortName?: string;
  /** URL to the agency's parent domain, e.g. "https://www.va.gov" */
  agencyHref?: string;
  /** Path to the agency logo image */
  agencyLogoSrc?: string;
  /** Alt text for the agency logo */
  agencyLogoAlt?: string;
  /**
   * Required government identifier links (About, Accessibility, etc.).
   * Defaults to the standard VA set if not provided.
   */
  identifierLinks?: IdentifierLink[];
}

const DEFAULT_IDENTIFIER_LINKS: IdentifierLink[] = [
  { href: '/about', text: 'About VA' },
  { href: '/accessibility', text: 'Accessibility statement' },
  { href: '/foia', text: 'FOIA requests' },
  { href: '/nofear', text: 'No FEAR Act data' },
  { href: '/ig', text: 'Office of the Inspector General' },
  { href: '/performance', text: 'Performance reports' },
  { href: '/privacy', text: 'Privacy policy' },
];

export function UswdsIdentifier({
  agencyName,
  agencyShortName,
  agencyHref = 'https://www.va.gov',
  agencyLogoSrc,
  agencyLogoAlt,
  identifierLinks = DEFAULT_IDENTIFIER_LINKS,
}: UswdsIdentifierProps): React.ReactElement {
  return (
    <div className="usa-identifier">
      <section
        className="usa-identifier__section usa-identifier__section--masthead"
        aria-label={`${agencyName} identifier`}
      >
        <div className="usa-identifier__container">
          {agencyLogoSrc && (
            <div className="usa-identifier__logos">
              <a
                href={agencyHref}
                className="usa-identifier__logo"
                aria-label={`${agencyName} logo`}
              >
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  className="usa-identifier__logo-img"
                  src={agencyLogoSrc}
                  alt={agencyLogoAlt ?? `${agencyName} logo`}
                  role="img"
                />
              </a>
            </div>
          )}
          <section
            className="usa-identifier__identity"
            aria-label={`${agencyName} description`}
          >
            <p className="usa-identifier__identity-domain">
              {agencyHref.replace(/^https?:\/\//, '')}
            </p>
            <p className="usa-identifier__identity-disclaimer">
              An official website of the{' '}
              <a href={agencyHref}>{agencyName}</a>
              {agencyShortName && <> ({agencyShortName})</>}
            </p>
          </section>
        </div>
      </section>
      <nav
        className="usa-identifier__section usa-identifier__section--required-links"
        aria-label="Important links"
      >
        <div className="usa-identifier__container">
          <ul className="usa-identifier__required-links-list">
            {identifierLinks.map((link) => (
              <li key={link.href} className="usa-identifier__required-links-item">
                <a href={link.href} className="usa-identifier__required-link usa-link">
                  {link.text}
                </a>
              </li>
            ))}
          </ul>
        </div>
      </nav>
      <section
        className="usa-identifier__section usa-identifier__section--usagov"
        aria-label="U.S. government information and services"
      >
        <div className="usa-identifier__container">
          <div className="usa-identifier__usagov-description">
            Looking for U.S. government information and services?{' '}
            <a href="https://www.usa.gov" className="usa-link">
              Visit USA.gov
            </a>
          </div>
        </div>
      </section>
    </div>
  );
}
