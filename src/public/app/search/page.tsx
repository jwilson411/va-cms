/**
 * app/search/page.tsx — Public search results page.
 *
 * Issue #50 — BRD FR-SEARCH-01
 *
 * Acceptance criteria:
 *   - USWDS Search component in public header (already present via UswdsHeader, issue #47)
 *   - Search results page at /search renders USWDS-compliant result cards
 *   - Filter sidebar: content type, date range, tags
 *   - Pagination on results (USWDS Pagination component)
 *
 * This is a Next.js 14 App Router Server Component. It:
 *   1. Reads searchParams from the URL (q, type, from, to, tag, page)
 *   2. Fetches results from the CMS search API (no-store, server-side),
 *      the CMS-managed primary navigation and the site settings
 *   3. Renders SearchResultsTemplate, which owns the full USWDS chrome
 *      (the root layout renders none) plus result cards, filter sidebar
 *      and pagination
 *
 * When q is absent, the template renders the search form with empty state.
 * When the features.publicSearch setting is off, it renders an
 * "unavailable" notice instead of the search UI.
 * Errors in the API call are handled gracefully (empty result set shown).
 */

import React from 'react';
import type { Metadata } from 'next';
import { fetchSearchResults, totalPages } from '@/lib/cms/search';
import { fetchPrimaryNav } from '@/lib/cms/navigation';
import { fetchSiteSettings } from '@/lib/cms/settings';
import { SearchResultsTemplate } from '@/components/templates/SearchResultsTemplate';

export async function generateMetadata(): Promise<Metadata> {
  const site = await fetchSiteSettings();
  return {
    title: `Search — ${site.siteTitle}`,
    description: `Search published ${site.agencyShortName} content`,
  };
}

/** Force dynamic rendering — search is never static. */
export const dynamic = 'force-dynamic';

interface SearchQuery {
  q?: string;
  type?: string;
  from?: string;
  to?: string;
  tag?: string;
  page?: string;
}

interface SearchPageProps {
  /** Next 15+: search params resolve asynchronously. */
  searchParams: Promise<SearchQuery>;
}

export default async function SearchPage({
  searchParams: searchParamsPromise,
}: SearchPageProps): Promise<React.ReactElement> {
  const searchParams = await searchParamsPromise;
  const query = (searchParams.q ?? '').trim();
  const typeParam = searchParams.type ? parseInt(searchParams.type, 10) : null;
  const fromParam = searchParams.from ?? null;
  const toParam = searchParams.to ?? null;
  const tagParam = searchParams.tag ? parseInt(searchParams.tag, 10) : null;
  const pageParam = searchParams.page ? Math.max(1, parseInt(searchParams.page, 10)) : 1;

  // Page size and the on/off switch are site settings (issue #149, epic #141).
  const [site, navigation] = await Promise.all([fetchSiteSettings(), fetchPrimaryNav()]);
  const PAGE_SIZE = site.searchPageSize;

  if (!site.publicSearchEnabled) {
    return (
      <SearchResultsTemplate
        searchEnabled={false}
        query=""
        items={[]}
        totalItems={0}
        currentPage={1}
        totalPages={1}
        buildPageHref={() => '/search'}
        contentTypes={[]}
        tags={[]}
        navigation={navigation}
        site={site}
      />
    );
  }

  const response = query
    ? await fetchSearchResults(query, {
        type: Number.isNaN(typeParam) ? null : typeParam,
        from: fromParam || null,
        to: toParam || null,
        tag: Number.isNaN(tagParam) ? null : tagParam,
        page: pageParam,
        pageSize: PAGE_SIZE,
      })
    : null;

  const numPages = response ? totalPages(response.totalItems, PAGE_SIZE) : 1;

  /** Build href for a page number, preserving all current filters */
  function buildPageHref(page: number): string {
    const params = new URLSearchParams();
    if (query) params.set('q', query);
    if (typeParam != null && !Number.isNaN(typeParam)) params.set('type', String(typeParam));
    if (fromParam) params.set('from', fromParam);
    if (toParam) params.set('to', toParam);
    if (tagParam != null && !Number.isNaN(tagParam)) params.set('tag', String(tagParam));
    params.set('page', String(page));
    return `/search?${params.toString()}`;
  }

  return (
    <SearchResultsTemplate
      query={query}
      items={response?.items ?? []}
      totalItems={response?.totalItems ?? 0}
      currentPage={pageParam}
      totalPages={numPages}
      pageSize={PAGE_SIZE}
      buildPageHref={buildPageHref}
      contentTypes={[]}
      tags={[]}
      navigation={navigation}
      site={site}
      selectedType={searchParams.type}
      selectedFrom={searchParams.from}
      selectedTo={searchParams.to}
      selectedTag={searchParams.tag}
    />
  );
}
