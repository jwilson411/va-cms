/**
 * app/not-found.tsx — Next.js 14 App Router global 404 page.
 *
 * Issue #61 — BRD FR-ERR-01
 * AC: 404 page uses USWDS Alert (info) and suggests search or home link.
 * AC: Includes Banner and Identifier.
 * AC: Passes axe-core with zero critical violations.
 *
 * Called automatically by Next.js when notFound() is thrown from any
 * route in the app directory, or when no matching route is found.
 *
 * Fetches CMS-managed navigation for the header; falls back to empty
 * array if the API is unavailable so the page still renders.
 */

import type { Metadata } from 'next';
import { fetchPrimaryNav } from '@/lib/cms/navigation';
import { fetchSiteSettings } from '@/lib/cms/settings';
import { NotFoundTemplate } from '@/components/templates/NotFoundTemplate';

export async function generateMetadata(): Promise<Metadata> {
  const site = await fetchSiteSettings();
  return { title: `Page Not Found | ${site.siteTitle}` };
}

export default async function NotFoundPage(): Promise<React.ReactElement> {
  // Fetch CMS navigation and site settings — both fall back if the API is unavailable.
  const [navigation, site] = await Promise.all([fetchPrimaryNav().catch(() => []), fetchSiteSettings()]);

  return <NotFoundTemplate navigation={navigation} site={site} />;
}
