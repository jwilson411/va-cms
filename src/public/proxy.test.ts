/**
 * proxy.test.ts — the redirect half of the proxy (#169). The header half is covered by
 * lib/security/csp.test.ts; here the CMS lookup is stubbed.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NextRequest } from 'next/server';

const lookupRedirect = vi.fn();
vi.mock('@/lib/cms/redirects', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/cms/redirects')>()),
  lookupRedirect: (...args: unknown[]) => lookupRedirect(...args),
}));

import { cmsRedirectFor, proxy } from './proxy';

const req = (url: string, method = 'GET') => new NextRequest(url, { method });

describe('proxy redirects', () => {
  beforeEach(() => lookupRedirect.mockReset());

  it('answers a matching GET with the configured status and a Location on the request origin', async () => {
    lookupRedirect.mockResolvedValue({ fromPath: '/pages/old', toPath: '/pages/new', statusCode: 301 });

    const res = await proxy(req('http://localhost:3000/pages/old?utm=1'));

    expect(res.status).toBe(301);
    expect(res.headers.get('location')).toBe('http://localhost:3000/pages/new?utm=1');
    expect(res.headers.get('x-content-type-options')).toBe('nosniff');
    expect(res.headers.get('content-security-policy')).toBeNull();
    expect(lookupRedirect).toHaveBeenCalledWith('/pages/old', expect.objectContaining({ cache: expect.anything() }));
  });

  it('uses 302 when the rule says so and sends external targets as stored', async () => {
    lookupRedirect.mockResolvedValue({ fromPath: '/x', toPath: 'https://www.va.gov/x', statusCode: 302 });

    const res = await cmsRedirectFor(req('http://localhost:3000/x?y=1'));

    expect(res?.status).toBe(302);
    expect(res?.headers.get('location')).toBe('https://www.va.gov/x');
  });

  it('renders normally (with CSP) when there is no rule', async () => {
    lookupRedirect.mockResolvedValue(null);

    const res = await proxy(req('http://localhost:3000/pages/live'));

    expect(res.status).toBe(200);
    expect(res.headers.get('location')).toBeNull();
    expect(res.headers.get('content-security-policy')).toContain("default-src 'self'");
  });

  it('does not consult the table for non-GET requests', async () => {
    const res = await cmsRedirectFor(req('http://localhost:3000/pages/old', 'POST'));
    expect(res).toBeNull();
    expect(lookupRedirect).not.toHaveBeenCalled();
  });
});
