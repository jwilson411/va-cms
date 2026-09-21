/**
 * app/page.tsx — Public site homepage.
 *
 * Renders the full USWDS chrome itself (Banner → Header → main → Footer →
 * Identifier), like every template under components/templates — the root layout
 * renders no chrome. Header navigation is the CMS-managed primary menu (#47),
 * fetched with the shared ISR tag so an admin save invalidates it.
 *
 * The homepage itself is CMS-editable: the `site.homepageSlug` site setting (Site
 * category, Admin → Settings) names a Standard Page slug (as in /pages/{slug}) to
 * render at "/", via the same StandardPageTemplate the /pages/[...slug] route uses
 * — same body rendering, In-Page Nav for 3+ H2 sections, etc., just without a
 * breadcrumb (the homepage doesn't need one back to itself). Blank, or a slug with
 * no published page, falls back to the placeholder "Welcome" content below so the
 * site never 404s at "/".
 */

import { UswdsBanner, UswdsHeader, UswdsFooter, UswdsIdentifier } from '@/components/uswds';
import { fetchPrimaryNav } from '@/lib/cms/navigation';
import { fetchSiteSettings } from '@/lib/cms/settings';
import { fetchStandardPage, extractH2Sections, injectH2Ids } from '@/lib/cms/content';
import { StandardPageTemplate } from '@/components/templates/StandardPageTemplate';
import type { Metadata } from 'next';

export async function generateMetadata(): Promise<Metadata> {
  const site = await fetchSiteSettings();
  if (!site.homepageSlug) return {};

  const page = await fetchStandardPage(site.homepageSlug);
  if (!page) return {};

  return { title: `${page.fields.title} | ${site.siteTitle}` };
}

export default async function HomePage(): Promise<React.ReactElement> {
  const [navigation, site] = await Promise.all([fetchPrimaryNav(), fetchSiteSettings()]);

  const cmsPage = site.homepageSlug ? await fetchStandardPage(site.homepageSlug) : null;

  if (cmsPage) {
    const bodyHtml = injectH2Ids(cmsPage.fields.renderedBody ?? `<p>${cmsPage.fields.body}</p>`);
    const sections = extractH2Sections(bodyHtml);

    return (
      <StandardPageTemplate
        title={cmsPage.fields.title}
        renderedBody={bodyHtml}
        sections={sections}
        navigation={navigation}
        site={site}
      />
    );
  }

  // No homepage slug configured (or its page isn't published) — placeholder chrome.
  return (
    <>
      <UswdsBanner lang={site.bannerLang} />
      <UswdsHeader siteTitle={site.siteTitle} navigation={navigation} showSearch={site.publicSearchEnabled} />
      <main id="main-content" tabIndex={-1}>
        <div className="grid-container">
          <h1>Welcome</h1>
          <p>{site.metaTitle}</p>
        </div>
      </main>
      <UswdsFooter
        agencyName={site.agencyName}
        agencyHref={site.agencyHref}
        agencyLogoSrc={site.agencyLogoSrc || undefined}
      />
      <UswdsIdentifier
        agencyName={site.agencyName}
        agencyShortName={site.agencyShortName}
        agencyHref={site.agencyHref}
        agencyLogoSrc={site.agencyLogoSrc || undefined}
      />
    </>
  );
}
