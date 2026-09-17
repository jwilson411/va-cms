import { UswdsBanner, UswdsHeader, UswdsFooter, UswdsIdentifier } from '@/components/uswds';
import { fetchSiteSettings } from '@/lib/cms/settings';

const NAV = [
  { label: 'Home', href: '/' },
  { label: 'News', href: '/news' },
  { label: 'About', href: '/about' },
];

export default async function HomePage(): Promise<React.ReactElement> {
  const site = await fetchSiteSettings();

  return (
    <>
      <UswdsBanner lang={site.bannerLang} />
      <UswdsHeader siteTitle={site.siteTitle} navigation={NAV} showSearch={site.publicSearchEnabled} />
      <main id="main-content">
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
