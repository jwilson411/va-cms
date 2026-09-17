/**
 * #162: public-site CSP is nonce-based with no 'unsafe-inline' for scripts, and
 * proxy.ts sets it (plus the companion headers) on every page response.
 */
import { NextRequest } from 'next/server';
import { buildPublicCsp, buildPublicCspDirectives, originOf, PUBLIC_SECURITY_HEADERS } from './csp';
import { generateNonce, proxy } from '../../proxy';

describe('public CSP', () => {
  it('uses a nonce and strict-dynamic, never unsafe-inline, for scripts', () => {
    const d = buildPublicCspDirectives('abc123', { apiOrigin: 'https://cms.va.gov' });
    expect(d['script-src']).toEqual(["'self'", "'nonce-abc123'", "'strict-dynamic'", 'https://dap.digitalgov.gov']);
    expect(d['img-src']).toContain('https://cms.va.gov');
    expect(d['connect-src']).toContain('https://cms.va.gov');
    expect(d['frame-ancestors']).toEqual(["'none'"]);
    expect(d['object-src']).toEqual(["'none'"]);
  });

  it('serialises with the API report endpoint', () => {
    const csp = buildPublicCsp('n', { apiOrigin: 'https://cms.va.gov' });
    expect(csp).toMatch(/report-uri https:\/\/cms\.va\.gov\/api\/v1\/security\/csp-report$/);
    expect(csp).not.toMatch(/script-src[^;]*unsafe-inline/);
    expect(buildPublicCsp('n')).toMatch(/report-uri \/api\/v1\/security\/csp-report$/);
  });

  it('originOf tolerates relative and bad URLs', () => {
    expect(originOf('https://cms.va.gov/api/v1')).toBe('https://cms.va.gov');
    expect(originOf('/api/v1')).toBe('');
    expect(originOf(undefined)).toBe('');
  });
});

describe('proxy', () => {
  it('generates distinct base64 nonces', () => {
    const a = generateNonce();
    const b = generateNonce();
    expect(a).not.toBe(b);
    expect(a).toMatch(/^[A-Za-z0-9+/]+=*$/);
  });

  it('sets CSP with the request nonce and the companion headers', () => {
    const response = proxy(new NextRequest('http://localhost:3000/news/hello'));
    const csp = response.headers.get('Content-Security-Policy') ?? response.headers.get('Content-Security-Policy-Report-Only');
    expect(csp).toMatch(/script-src 'self' 'nonce-[A-Za-z0-9+/=]+' 'strict-dynamic'/);
    for (const [name, value] of Object.entries(PUBLIC_SECURITY_HEADERS)) expect(response.headers.get(name)).toBe(value);
    // The nonce reaches the render through the forwarded request headers.
    const forwardedNonce = response.headers.get('x-middleware-request-x-nonce');
    expect(forwardedNonce).toBeTruthy();
    expect(csp).toContain(`'nonce-${forwardedNonce}'`);
    expect(response.headers.get('Strict-Transport-Security')).toBeNull(); // http, not production
  });
});
