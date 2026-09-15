/**
 * app/layout.tsx — Root layout for VA CMS public site.
 *
 * Issue #47 — BRD FR-NAV-01:
 *   USWDS Header primary navigation is driven by the CMS-managed menu.
 *   fetchPrimaryNav() is called at render time with ISR tagging so the
 *   cache invalidates when an admin saves the navigation menu.
 *
 * Mobile hamburger menu works at 320px+ via UswdsHeader's built-in USWDS
 * responsive behaviour (usa-header--extended + usa-menu-btn).
 */

import type { Metadata } from 'next';
import '@uswds/uswds/css/uswds.css';
import { UswdsHeader } from '@/components/uswds/UswdsHeader';
import { fetchPrimaryNav } from '@/lib/cms/navigation';

export const metadata: Metadata = {
  title: 'VA CMS Public Site',
  description: 'USWDS-compliant VA content management system public site',
};

/**
 * RootLayout fetches the primary nav from the CMS API at render time.
 * This is an async Server Component — it runs on the server during ISR
 * and the result is cached and tagged for on-demand revalidation.
 */
export default async function RootLayout({
  children,
}: {
  children: React.ReactNode;
}): Promise<React.ReactElement> {
  // Fetch CMS-managed primary navigation (ISR-cached, tag: 'cms-primary-nav').
  // Falls back to empty array on error so the page still renders without nav.
  const navigation = await fetchPrimaryNav();

  return (
    <html lang="en">
      <body>
        <UswdsHeader
          siteTitle="Department of Veterans Affairs"
          navigation={navigation}
        />
        <main id="main-content">{children}</main>
      </body>
    </html>
  );
}
