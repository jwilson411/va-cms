/**
 * Content-Security-Policy for the admin SPA (#162, BRD FR-SECURITY-03).
 *
 * The SPA is a static Vite build served by IIS, so the policy is fixed at build
 * time and delivered two ways: `public/web.config` (IIS customHeaders, production)
 * and the Vite dev/preview server headers (vite.config.ts). It contains no
 * 'unsafe-inline' for scripts — the build emits a single external module script
 * (build.modulePreload.polyfill is off) — and blob: for the authenticated image
 * previews (AuthedImage). Inline *styles* stay allowed: React style props and
 * USWDS components rely on them. Violations report to the API.
 */

export const CSP_REPORT_PATH = '/api/v1/security/csp-report';

export const ADMIN_CSP_DIRECTIVES: Record<string, string[]> = {
  'default-src': ["'self'"],
  'script-src': ["'self'"],
  'style-src': ["'self'", "'unsafe-inline'"],
  'img-src': ["'self'", 'data:', 'blob:'],
  'font-src': ["'self'"],
  'connect-src': ["'self'"],
  'frame-ancestors': ["'none'"],
  'base-uri': ["'self'"],
  'form-action': ["'self'"],
  'object-src': ["'none'"],
  'worker-src': ["'self'", 'blob:'],
  'manifest-src': ["'self'"],
};

/** Serialises the directive map; `report-uri` is appended so the report-only phase has data. */
export function buildAdminCsp(directives: Record<string, string[]> = ADMIN_CSP_DIRECTIVES): string {
  const parts = Object.entries(directives).map(([name, values]) => `${name} ${values.join(' ')}`);
  parts.push(`report-uri ${CSP_REPORT_PATH}`);
  return parts.join('; ');
}

/** The other response headers every SPA asset should carry (mirrors the API middleware). */
export const ADMIN_SECURITY_HEADERS: Record<string, string> = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy':
    'accelerometer=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=(), xr-spatial-tracking=()',
  'Cross-Origin-Opener-Policy': 'same-origin',
};
