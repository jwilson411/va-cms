/**
 * UswdsFooter — USWDS big footer for VA public pages.
 *
 * Renders the usa-footer--big variant with agency branding and navigation.
 *
 * BRD NFR-A11Y-02: must come from @uswds/uswds, not rewritten.
 */

import React from 'react';

export interface FooterNavColumn {
  header: string;
  links: Array<{ href: string; text: string }>;
}

export interface UswdsFooterProps {
  /** Agency name for the footer masthead */
  agencyName: string;
  /** Optional agency logo image source */
  agencyLogoSrc?: string;
  /** Alt text for the agency logo */
  agencyLogoAlt?: string;
  /** Optional agency website URL */
  agencyHref?: string;
  /** Navigation columns for the big footer grid */
  navColumns?: FooterNavColumn[];
  /** Optional contact information or secondary content */
  contactInfo?: React.ReactNode;
}

export function UswdsFooter({
  agencyName,
  agencyLogoSrc,
  agencyLogoAlt,
  agencyHref = 'https://www.va.gov',
  navColumns = [],
  contactInfo,
}: UswdsFooterProps): React.ReactElement {
  return (
    <footer className="usa-footer usa-footer--big" role="contentinfo">
      <div className="grid-container usa-footer__return-to-top">
        <a href="#">Return to top</a>
      </div>

      {navColumns.length > 0 && (
        <div className="usa-footer__primary-section">
          <div className="usa-footer__primary-container grid-row">
            {navColumns.map((col) => (
              <div
                key={col.header}
                className="mobile-lg:grid-col-6 desktop:grid-col-3"
              >
                <section className="usa-footer__primary-content usa-footer__primary-content--collapsible">
                  <h4 className="usa-footer__primary-link">{col.header}</h4>
                  <ul className="usa-list usa-list--unstyled">
                    {col.links.map((link) => (
                      <li key={link.href} className="usa-footer__secondary-link">
                        <a href={link.href}>{link.text}</a>
                      </li>
                    ))}
                  </ul>
                </section>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="usa-footer__secondary-section">
        <div className="grid-container">
          <div className="grid-row grid-gap">
            <div className="usa-footer__logo grid-row mobile-lg:grid-col-6 mobile-lg:grid-gap-2">
              {agencyLogoSrc && (
                <div className="mobile-lg:grid-col-auto">
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    className="usa-footer__logo-img"
                    src={agencyLogoSrc}
                    alt={agencyLogoAlt ?? `${agencyName} logo`}
                    role="img"
                  />
                </div>
              )}
              <div className="mobile-lg:grid-col-auto">
                <p className="usa-footer__logo-heading">
                  <a href={agencyHref}>{agencyName}</a>
                </p>
              </div>
            </div>

            {contactInfo && (
              <div className="usa-footer__contact-links mobile-lg:grid-col-6">
                {contactInfo}
              </div>
            )}
          </div>
        </div>
      </div>
    </footer>
  );
}
