/**
 * proxy.ts — per-request security headers for the public site (#162).
 * (Next 16's name for middleware.ts.)
 *
 * Generates a nonce, hands it to the render through the x-nonce and
 * Content-Security-Policy request headers (Next applies it to its own scripts),
 * and sets the response headers: CSP — enforced, or Report-Only while
 * CSP_REPORT_ONLY=true (the deployment-phase switch; a per-request site-setting
 * lookup is not available here) — plus nosniff, frame denial, referrer and
 * permissions policies, and HSTS on HTTPS in production.
 */
import { NextResponse, type NextRequest } from 'next/server';
import { buildPublicCsp, HSTS_HEADER, originOf, PUBLIC_SECURITY_HEADERS } from '@/lib/security/csp';

const API_ORIGIN = originOf(process.env.NEXT_PUBLIC_API_URL) || originOf(process.env.CMS_API_URL);
const REPORT_ONLY = (process.env.CSP_REPORT_ONLY ?? '').toLowerCase() === 'true';

export function generateNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes));
}

export function proxy(request: NextRequest): NextResponse {
  const nonce = generateNonce();
  const csp = buildPublicCsp(nonce, { apiOrigin: API_ORIGIN });

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set('x-nonce', nonce);
  requestHeaders.set('Content-Security-Policy', csp);

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set(REPORT_ONLY ? 'Content-Security-Policy-Report-Only' : 'Content-Security-Policy', csp);
  for (const [name, value] of Object.entries(PUBLIC_SECURITY_HEADERS)) response.headers.set(name, value);

  const https =
    request.nextUrl.protocol === 'https:' || request.headers.get('x-forwarded-proto')?.split(',')[0]?.trim() === 'https';
  if (https && process.env.NODE_ENV === 'production') response.headers.set('Strict-Transport-Security', HSTS_HEADER);

  return response;
}

export const config = {
  // Everything except Next's own static output, the copied USWDS assets and the webhook receivers
  // (machine callers; no HTML, and the API sets its own headers for /api on the CMS host).
  matcher: ['/((?!_next/static|_next/image|uswds/|favicon.ico|api/revalidate|api/revalidate-nav).*)'],
};
