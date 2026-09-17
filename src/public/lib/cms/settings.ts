/**
 * lib/cms/settings.ts
 *
 * Public site settings from the CMS (issue #149, epic #141).
 *
 * The API exposes Public-scoped site settings at GET /api/v1/settings/public as a flat
 * key → value map. This module fetches them with ISR tagging (cms-site-settings) so an
 * admin change — delivered as a `settings.updated` webhook to /api/revalidate — is live
 * on the next render without a build.
 *
 * Every consumer gets a fully-populated SiteSettings object: the code defaults below
 * match SiteSettingDefinitions.cs and apply whenever the API is unreachable or a key is
 * missing, so a page never renders with blank chrome.
 */

/** Cache tag for ISR on-demand revalidation via revalidateTag(). */
export const SITE_SETTINGS_CACHE_TAG = 'cms-site-settings';

export interface SiteSettings {
  /** Header masthead title and `<title>` suffix. */
  siteTitle: string;
  /** `<title>` of the home page. */
  metaTitle: string;
  /** Default `<meta name="description">`. */
  metaDescription: string;
  /** Full agency name for the footer and identifier. */
  agencyName: string;
  /** Short agency name for the identifier. */
  agencyShortName: string;
  /** Agency parent-domain URL. */
  agencyHref: string;
  /** Optional agency logo URL; empty hides the logo. */
  agencyLogoSrc: string;
  /** Government banner language. */
  bannerLang: 'en' | 'es';
  /** DAP analytics. */
  dapEnabled: boolean;
  dapAgency: string;
  dapSubagency: string;
  /** features.publicSearch — search box and results page. */
  publicSearchEnabled: boolean;
  /** search.publicPageSize — results per page. */
  searchPageSize: number;
}

/** The subset every page template needs for its USWDS chrome. */
export type SiteChrome = Pick<
  SiteSettings,
  'siteTitle' | 'agencyName' | 'agencyShortName' | 'agencyHref' | 'agencyLogoSrc' | 'bannerLang' | 'publicSearchEnabled'
>;

/** Code defaults — keep in sync with SiteSettingDefinitions.cs. */
export const DEFAULT_SITE_SETTINGS: SiteSettings = {
  siteTitle: 'Department of Veterans Affairs',
  metaTitle: 'VA CMS Public Site',
  metaDescription: 'USWDS-compliant VA content management system public site',
  agencyName: 'Department of Veterans Affairs',
  agencyShortName: 'VA',
  agencyHref: 'https://www.va.gov',
  agencyLogoSrc: '',
  bannerLang: 'en',
  dapEnabled: false,
  dapAgency: '',
  dapSubagency: '',
  publicSearchEnabled: true,
  searchPageSize: 10,
};

export type RawSiteSettings = Record<string, string | null | undefined>;

function str(raw: RawSiteSettings, key: string, fallback: string): string {
  const v = raw[key];
  return typeof v === 'string' ? v : fallback;
}

function bool(raw: RawSiteSettings, key: string, fallback: boolean): boolean {
  const v = raw[key]?.trim().toLowerCase();
  if (v === 'true' || v === '1' || v === 'yes' || v === 'on') return true;
  if (v === 'false' || v === '0' || v === 'no' || v === 'off') return false;
  return fallback;
}

function int(raw: RawSiteSettings, key: string, fallback: number): number {
  const n = Number.parseInt(raw[key] ?? '', 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

/** Map the API's key → value map onto SiteSettings, filling gaps with defaults. */
export function mapSiteSettings(raw: RawSiteSettings): SiteSettings {
  const d = DEFAULT_SITE_SETTINGS;
  const lang = str(raw, 'site.bannerLang', d.bannerLang).trim().toLowerCase();
  return {
    siteTitle: str(raw, 'site.title', d.siteTitle),
    metaTitle: str(raw, 'site.metaTitle', d.metaTitle),
    metaDescription: str(raw, 'site.metaDescription', d.metaDescription),
    agencyName: str(raw, 'site.agencyName', d.agencyName),
    agencyShortName: str(raw, 'site.agencyShortName', d.agencyShortName),
    agencyHref: str(raw, 'site.agencyHref', d.agencyHref),
    agencyLogoSrc: str(raw, 'site.agencyLogoSrc', d.agencyLogoSrc),
    bannerLang: lang === 'es' ? 'es' : 'en',
    dapEnabled: bool(raw, 'analytics.dapEnabled', d.dapEnabled),
    dapAgency: str(raw, 'analytics.dapAgency', d.dapAgency),
    dapSubagency: str(raw, 'analytics.dapSubagency', d.dapSubagency),
    publicSearchEnabled: bool(raw, 'features.publicSearch', d.publicSearchEnabled),
    searchPageSize: int(raw, 'search.publicPageSize', d.searchPageSize),
  };
}

const getApiBase = (): string =>
  process.env.NEXT_PUBLIC_API_URL ??
  process.env.CMS_API_URL ??
  'http://localhost:5100';

/**
 * Fetch public site settings (ISR-cached, tag: cms-site-settings).
 * Never throws — returns defaults on any error so the page still renders.
 */
export async function fetchSiteSettings(): Promise<SiteSettings> {
  const url = `${getApiBase()}/api/v1/settings/public`;

  try {
    const res = await fetch(url, {
      next: { revalidate: false, tags: [SITE_SETTINGS_CACHE_TAG] },
      headers: { Accept: 'application/json' },
    });

    if (!res.ok) {
      console.warn(`[settings] CMS settings fetch returned ${res.status}: ${url}`);
      return DEFAULT_SITE_SETTINGS;
    }

    return mapSiteSettings((await res.json()) as RawSiteSettings);
  } catch (err) {
    console.error('[settings] Failed to fetch site settings:', err);
    return DEFAULT_SITE_SETTINGS;
  }
}
