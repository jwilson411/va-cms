/**
 * UswdsBanner — Official government banner for VA public pages.
 *
 * Renders the standard USWDS "An official website of the United States government"
 * banner markup exactly as specified in USWDS 3.x.
 *
 * BRD NFR-A11Y-02: must come from @uswds/uswds, not rewritten.
 */

'use client';

import React, { useState } from 'react';

interface UswdsBannerProps {
  /** Language of the page for the banner text. Defaults to 'en'. */
  lang?: 'en' | 'es';
}

export function UswdsBanner({ lang = 'en' }: UswdsBannerProps): React.ReactElement {
  const [expanded, setExpanded] = useState(false);

  const isEs = lang === 'es';

  const headerText = isEs
    ? 'Un sitio web oficial del Gobierno de Estados Unidos'
    : 'An official website of the United States government';

  const buttonText = isEs
    ? "Así es como usted puede verificarlo"
    : "Here's how you know";

  return (
    <section
      className="usa-banner"
      aria-label={
        isEs
          ? 'Mensaje de sitio oficial del gobierno'
          : 'Official government website'
      }
    >
      <div className="usa-accordion">
        <header className="usa-banner__header">
          <div className="usa-banner__inner">
            <div className="grid-col-auto">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                aria-hidden="true"
                className="usa-banner__header-flag"
                src="/assets/img/us_flag_small.png"
                alt=""
              />
            </div>
            <div className="grid-col-fill tablet:grid-col-auto" aria-hidden="true">
              <p className="usa-banner__header-text">{headerText}</p>
              <p className="usa-banner__header-action">{buttonText}</p>
            </div>
            <button
              type="button"
              className="usa-accordion__button usa-banner__button"
              aria-expanded={expanded}
              aria-controls="gov-banner-default"
              onClick={() => setExpanded((prev) => !prev)}
            >
              <span className="usa-banner__button-text">{buttonText}</span>
            </button>
          </div>
        </header>
        <div
          className="usa-banner__content usa-accordion__content"
          id="gov-banner-default"
          hidden={!expanded}
        >
          <div className="grid-row grid-gap-lg">
            <div className="usa-banner__guidance tablet:grid-col-6">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                className="usa-banner__icon usa-media-block__img"
                src="/assets/img/icon-dot-gov.svg"
                role="img"
                alt=""
                aria-hidden="true"
              />
              <div className="usa-media-block__body">
                <p>
                  <strong>
                    {isEs
                      ? 'Los sitios web oficiales usan .gov'
                      : 'Official websites use .gov'}
                  </strong>
                  <br />
                  {isEs
                    ? 'Un sitio web .gov pertenece a una organización oficial del Gobierno de Estados Unidos.'
                    : 'A .gov website belongs to an official government organization in the United States.'}
                </p>
              </div>
            </div>
            <div className="usa-banner__guidance tablet:grid-col-6">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                className="usa-banner__icon usa-media-block__img"
                src="/assets/img/icon-https.svg"
                role="img"
                alt=""
                aria-hidden="true"
              />
              <div className="usa-media-block__body">
                <p>
                  <strong>
                    {isEs
                      ? 'Los sitios web seguros .gov usan HTTPS'
                      : 'Secure .gov websites use HTTPS'}
                  </strong>
                  <br />
                  {isEs ? (
                    <>
                      Un <strong>candado</strong> o <strong>https://</strong> significa que usted se
                      conectó de forma segura a un sitio web .gov. Comparta información sensible sólo
                      en sitios web oficiales y seguros.
                    </>
                  ) : (
                    <>
                      A <strong>lock</strong> (<span className="icon-lock">🔒</span>) or{' '}
                      <strong>https://</strong> means you&apos;ve safely connected to the .gov
                      website. Share sensitive information only on official, secure websites.
                    </>
                  )}
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
