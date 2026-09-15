import { UswdsBanner, UswdsHeader, UswdsFooter, UswdsIdentifier } from '@/components/uswds';

const NAV = [
  { label: 'Home', href: '/' },
  { label: 'News', href: '/news' },
  { label: 'About', href: '/about' },
];

export default function HomePage(): React.ReactElement {
  return (
    <>
      <UswdsBanner />
      <UswdsHeader siteTitle="Department of Veterans Affairs" navigation={NAV} />
      <main id="main-content">
        <div className="grid-container">
          <h1>Welcome</h1>
          <p>VA CMS public site</p>
        </div>
      </main>
      <UswdsFooter
        agencyName="Department of Veterans Affairs"
        agencyHref="https://www.va.gov"
      />
      <UswdsIdentifier
        agencyName="Department of Veterans Affairs"
        agencyShortName="VA"
        agencyHref="https://www.va.gov"
      />
    </>
  );
}
