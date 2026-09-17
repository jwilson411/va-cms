/**
 * Content-Security-Policy for the public site (#162, BRD FR-SECURITY-03).
 *
 * Built per request by proxy.ts with a fresh nonce. Next.js reads the nonce from
 * the Content-Security-Policy *request* header and applies it to its own
 * framework scripts; our own inline scripts (JSON-LD structured data, the DAP tag)
 * receive it through the `nonce` prop. Nothing script-related is 'unsafe-inline'.
 *
 *   script-src   'self' 'nonce-…' 'strict-dynamic' + the DAP host
 *   style-src    'self' 'unsafe-inline'  (USWDS components and Next's style tags)
 *   img-src      'self' data: + the CMS API origin (media is served from /api/v1/media/serve)
 *   connect-src  'self' + the CMS API origin + DAP
 *   frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'
 */

export const CSP_REPORT_PATH = '/api/v1/security/csp-report';
export const DAP_HOST = 'https://dap.digitalgov.gov';

export interface PublicCspOptions {
  /** Origin of the CMS API (scheme + host), where media and search calls go. Empty when same-origin. */
  apiOrigin?: string;
  /** Extra hosts an operator needs (e.g. a self-hosted analytics collector). */
  extraConnect?: string[];
}

/** Origin of a URL, or '' when it is relative / unparsable. */
export function originOf(url: string | undefined): string {
  if (!url) return '';
  try {
    return new URL(url).origin;
  } catch {
    return '';
  }
}

export function buildPublicCspDirectives(nonce: string, options: PublicCspOptions = {}): Record<string, string[]> {
  const api = options.apiOrigin ? [options.apiOrigin] : [];
  const extra = options.extraConnect ?? [];
  return {
    'default-src': ["'self'"],
    'script-src': ["'self'", `'nonce-${nonce}'`, "'strict-dynamic'", DAP_HOST],
    'style-src': ["'self'", "'unsafe-inline'"],
    'img-src': ["'self'", 'data:', ...api],
    'font-src': ["'self'"],
    'connect-src': ["'self'", ...api, DAP_HOST, ...extra],
    'frame-ancestors': ["'none'"],
    'base-uri': ["'self'"],
    'form-action': ["'self'"],
    'object-src': ["'none'"],
    'manifest-src': ["'self'"],
  };
}

export function buildPublicCsp(nonce: string, options: PublicCspOptions = {}): string {
  const parts = Object.entries(buildPublicCspDirectives(nonce, options)).map(([k, v]) => `${k} ${v.join(' ')}`);
  parts.push(`report-uri ${apiReportUri(options.apiOrigin)}`);
  return parts.join('; ');
}

/** Reports go to the API's endpoint (cross-origin when the API is on another host). */
export function apiReportUri(apiOrigin?: string): string {
  return `${apiOrigin ?? ''}${CSP_REPORT_PATH}`;
}

/** The other response headers every page carries (mirrors the API middleware and the admin web.config). */
export const PUBLIC_SECURITY_HEADERS: Record<string, string> = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy':
    'accelerometer=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=(), xr-spatial-tracking=()',
  'Cross-Origin-Opener-Policy': 'same-origin',
};

export const HSTS_HEADER = 'max-age=31536000; includeSubDomains';
