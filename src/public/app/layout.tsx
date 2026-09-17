/**
 * app/layout.tsx — Root layout for VA CMS public site.
 *
 * Issue #47 — BRD FR-NAV-01:
 *   USWDS Header primary navigation is driven by the CMS-managed menu.
 *   fetchPrimaryNav() is called at render time with ISR tagging so the
 *   cache invalidates when an admin saves the navigation menu.
 *
 * Issue #149 (epic #141):
 *   Site title, metadata and DAP analytics codes come from the CMS site settings
 *   (GET /api/v1/settings/public, ISR tag cms-site-settings) — an admin change is live
 *   on the next render, no build required.
 *
 * Mobile hamburger menu works at 320px+ via UswdsHeader's built-in USWDS
 * responsive behaviour (usa-header--extended + usa-menu-btn).
 */

import type { Metadata } from 'next';
import '@/styles/uswds-theme.scss';
import { UswdsHeader } from '@/components/uswds/UswdsHeader';
import { fetchPrimaryNav } from '@/lib/cms/navigation';
import { fetchSiteSettings } from '@/lib/cms/settings';
import { DapScript } from '@/components/analytics/DapScript';

export async function generateMetadata(): Promise<Metadata> {
  const site = await fetchSiteSettings();
  return {
    title: site.metaTitle,
    description: site.metaDescription,
  };
}

/**
 * RootLayout fetches the primary nav and site settings from the CMS API at
 * render time. This is an async Server Component — it runs on the server during
 * ISR and the results are cached and tagged for on-demand revalidation.
 */
export default async function RootLayout({
  children,
}: {
  children: React.ReactNode;
}): Promise<React.ReactElement> {
  // Both fall back to safe defaults on error so the page still renders.
  const [navigation, site] = await Promise.all([fetchPrimaryNav(), fetchSiteSettings()]);

  return (
    <html lang="en">
      <head>
        {/* DAP analytics — issue #60, BRD FR-ANALYTICS-02.
            Loaded afterInteractive (non-blocking). Enabled and configured by the
            analytics.dapEnabled / dapAgency / dapSubagency site settings. */}
        <DapScript
          enabled={site.dapEnabled}
          agency={site.dapAgency}
          subagency={site.dapSubagency}
        />
      </head>
      <body>
        <UswdsHeader
          siteTitle={site.siteTitle}
          navigation={navigation}
          showSearch={site.publicSearchEnabled}
        />
        <main id="main-content">{children}</main>
      </body>
    </html>
  );
}
