/**
 * app/layout.tsx — Root layout for VA CMS public site.
 *
 * Issue #149 (epic #141):
 *   Site metadata and DAP analytics codes come from the CMS site settings
 *   (GET /api/v1/settings/public, ISR tag cms-site-settings) — an admin change is live
 *   on the next render, no build required.
 *
 * The layout deliberately renders NO page chrome. Every route renders one of the
 * components/templates/*Template components, and each template owns the full USWDS
 * chrome (Banner → Header with CMS-managed nav (#47) → <main #main-content> → Footer →
 * Identifier). Rendering a header or <main> here as well would double them on every
 * page and nest <main> inside <main>.
 */

import type { Metadata } from 'next';
import '@/styles/uswds-theme.scss';
import { fetchSiteSettings } from '@/lib/cms/settings';
import { headers } from 'next/headers';
import { DapScript } from '@/components/analytics/DapScript';

export async function generateMetadata(): Promise<Metadata> {
  const site = await fetchSiteSettings();
  return {
    title: site.metaTitle,
    description: site.metaDescription,
  };
}

/**
 * RootLayout fetches the site settings from the CMS API at render time (for the
 * DAP script). This is an async Server Component — it runs on the server during
 * ISR and the result is cached and tagged for on-demand revalidation.
 */
export default async function RootLayout({
  children,
}: {
  children: React.ReactNode;
}): Promise<React.ReactElement> {
  // Settings fall back to safe defaults on error so the page still renders.
  const [site, requestHeaders] = await Promise.all([fetchSiteSettings(), headers()]);
  // Per-request CSP nonce from proxy.ts (#162); undefined in tests / when the proxy did not run.
  const nonce = requestHeaders.get('x-nonce') ?? undefined;

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
          nonce={nonce}
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
