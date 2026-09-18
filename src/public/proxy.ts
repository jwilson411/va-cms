/**
 * proxy.ts — per-request security headers (#162) and CMS redirects (#169) for the
 * public site. (Next 16's name for middleware.ts.)
 *
 * Redirects first: a GET/HEAD whose pathname matches an active rule in the CMS
 * redirect table (slug changes and admin-created rules) is answered here with the
 * configured 301/302, query string preserved. The lookup
 * goes to GET /api/v1/redirects/resolve through a process-local TTL cache and fails
 * open, so an unreachable API costs at most the lookup timeout, never the page.
 *
 * Then headers: generates a nonce, hands it to the render through the x-nonce and
 * Content-Security-Policy request headers (Next applies it to its own scripts),
 * and sets the response headers: CSP — enforced, or Report-Only while
 * CSP_REPORT_ONLY=true (the deployment-phase switch; a per-request site-setting
 * lookup is not available here) — plus nosniff, frame denial, referrer and
 * permissions policies, and HSTS on HTTPS in production. Redirect responses get
 * the same headers minus CSP (there is no document to police).
 */
import { NextResponse, type NextRequest } from 'next/server';
import { lookupRedirect, proxyCache, redirectLocation, redirectStatus } from '@/lib/cms/redirects';
import { buildPublicCsp, HSTS_HEADER, originOf, PUBLIC_SECURITY_HEADERS } from '@/lib/security/csp';

const API_ORIGIN = originOf(process.env.NEXT_PUBLIC_API_URL) || originOf(process.env.CMS_API_URL);
const REPORT_ONLY = (process.env.CSP_REPORT_ONLY ?? '').toLowerCase() === 'true';

export function generateNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes));
}

function isHttps(request: NextRequest): boolean {
  return (
    request.nextUrl.protocol === 'https:' || request.headers.get('x-forwarded-proto')?.split(',')[0]?.trim() === 'https'
  );
}

function applyTransportHeaders(request: NextRequest, response: NextResponse): void {
  for (const [name, value] of Object.entries(PUBLIC_SECURITY_HEADERS)) response.headers.set(name, value);
  if (isHttps(request) && process.env.NODE_ENV === 'production')
    response.headers.set('Strict-Transport-Security', HSTS_HEADER);
}

/** The redirect response for a request, or null when no rule matches its pathname. */
export async function cmsRedirectFor(request: NextRequest): Promise<NextResponse | null> {
  if (request.method !== 'GET' && request.method !== 'HEAD') return null;

  const hit = await lookupRedirect(request.nextUrl.pathname, { cache: proxyCache });
  if (!hit) return null;

  // Next requires an absolute Location from the proxy; a site-relative target is
  // resolved against the request's own origin (the Host / X-Forwarded-Host the
  // reverse proxy passes through), so the site never needs to know its public name.
  const location = new URL(redirectLocation(hit.toPath, request.nextUrl.search), request.nextUrl);
  const response = NextResponse.redirect(location, redirectStatus(hit.statusCode));
  applyTransportHeaders(request, response);
  return response;
}

export async function proxy(request: NextRequest): Promise<NextResponse> {
  const redirected = await cmsRedirectFor(request);
  if (redirected) return redirected;

  const nonce = generateNonce();
  const csp = buildPublicCsp(nonce, { apiOrigin: API_ORIGIN });

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set('x-nonce', nonce);
  requestHeaders.set('Content-Security-Policy', csp);

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set(REPORT_ONLY ? 'Content-Security-Policy-Report-Only' : 'Content-Security-Policy', csp);
  applyTransportHeaders(request, response);

  return response;
}

export const config = {
  // Everything except Next's own static output, the copied USWDS assets and the webhook receivers
  // (machine callers; no HTML, and the API sets its own headers for /api on the CMS host).
  matcher: ['/((?!_next/static|_next/image|uswds/|favicon.ico|api/revalidate|api/revalidate-nav).*)'],
};
