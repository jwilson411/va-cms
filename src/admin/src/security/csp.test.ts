/**
 * #162: the admin CSP never allows inline or remote scripts. The IIS web.config
 * and the Vite dev headers are generated from these same directives
 * (vite.config.ts), so this is the single place to assert the policy.
 */
import { ADMIN_CSP_DIRECTIVES, ADMIN_SECURITY_HEADERS, buildAdminCsp } from './csp';

describe('admin CSP', () => {
  it('has no unsafe-inline or remote sources for scripts', () => {
    expect(ADMIN_CSP_DIRECTIVES['script-src']).toEqual(["'self'"]);
    expect(ADMIN_CSP_DIRECTIVES['object-src']).toEqual(["'none'"]);
    expect(ADMIN_CSP_DIRECTIVES['frame-ancestors']).toEqual(["'none'"]);
    expect(ADMIN_CSP_DIRECTIVES['base-uri']).toEqual(["'self'"]);
  });

  it('serialises with a report-uri and blob: for authenticated image previews', () => {
    const csp = buildAdminCsp();
    expect(csp).toContain("script-src 'self'");
    expect(csp).toContain("img-src 'self' data: blob:");
    expect(csp).toMatch(/report-uri \/api\/v1\/security\/csp-report$/);
    expect(csp).not.toMatch(/script-src[^;]*unsafe-inline/);
  });

  it('ships the same companion headers as the API middleware', () => {
    expect(ADMIN_SECURITY_HEADERS['X-Content-Type-Options']).toBe('nosniff');
    expect(ADMIN_SECURITY_HEADERS['X-Frame-Options']).toBe('DENY');
    expect(ADMIN_SECURITY_HEADERS['Referrer-Policy']).toBe('strict-origin-when-cross-origin');
    expect(ADMIN_SECURITY_HEADERS['Permissions-Policy']).toContain('camera=()');
  });
});
