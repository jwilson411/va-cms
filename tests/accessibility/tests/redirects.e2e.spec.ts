/**
 * redirects.e2e.spec.ts — issue #169: redirects are served.
 *
 * Publishes a page through the API, changes its slug, and checks the public site:
 *   - the old URL answers 301 with a relative Location for the new URL
 *   - following it renders the page at the new URL
 *   - an admin-created rule (with a query string) is served with its own status
 *   - a rule that would loop is rejected by the API
 *
 * Needs three things running (CI's "e2e" job starts them):
 *   API_URL    — the API in DevBypass mode (default http://localhost:5100)
 *   PUBLIC_URL — the Next.js public site pointed at that API (default http://localhost:3000)
 * The suite skips itself when either is not reachable, so it never blocks a local axe run.
 */

import { test, expect, type APIRequestContext } from '@playwright/test';

const API_URL = process.env.API_URL ?? 'http://localhost:5100';
const PUBLIC_URL = process.env.PUBLIC_URL ?? 'http://localhost:3000';
const DEV_USER = 'alice@va.gov';

async function reachable(request: APIRequestContext, url: string): Promise<boolean> {
  try {
    const resp = await request.get(url, { timeout: 5_000, maxRedirects: 0 });
    return resp.status() < 500;
  } catch {
    return false;
  }
}

async function devToken(request: APIRequestContext): Promise<string> {
  const resp = await request.post(`${API_URL}/api/auth/dev-login`, { headers: { 'X-Dev-User': DEV_USER } });
  expect(resp.ok(), `dev-login: ${resp.status()}`).toBeTruthy();
  return ((await resp.json()) as { accessToken: string }).accessToken;
}

/** Retry a public-site request until it answers with one of the wanted statuses (webhook/ISR settle). */
async function waitForStatus(
  request: APIRequestContext,
  url: string,
  wanted: number[],
  attempts = 20,
): Promise<import('@playwright/test').APIResponse> {
  let last: import('@playwright/test').APIResponse | undefined;
  for (let i = 0; i < attempts; i++) {
    last = await request.get(url, { maxRedirects: 0 });
    if (wanted.includes(last.status())) return last;
    await new Promise((r) => setTimeout(r, 500));
  }
  return last!;
}

test.describe('Redirects are served (#169)', () => {
  let api: APIRequestContext;
  let token: string;
  const auth = () => ({ Authorization: `Bearer ${token}` });

  test.beforeAll(async ({ playwright, request }) => {
    const apiUp = await reachable(request, `${API_URL}/health`);
    const siteUp = await reachable(request, `${PUBLIC_URL}/`);
    test.skip(!apiUp || !siteUp, `API (${apiUp}) or public site (${siteUp}) not reachable`);

    token = await devToken(request);
    api = await playwright.request.newContext({ baseURL: API_URL, extraHTTPHeaders: auth() });

    // The site drops its ISR cache on CMS webhooks (README step 5); register the
    // receiver once so a slug change invalidates the old slug's cached page.
    const hooks = (await (await api.get('/api/v1/webhooks')).json()) as { items?: { url: string }[] } | { url: string }[];
    const list = Array.isArray(hooks) ? hooks : (hooks.items ?? []);
    if (!list.some((h) => h.url === `${PUBLIC_URL}/api/revalidate`)) {
      const reg = await api.post('/api/v1/webhooks', {
        data: {
          name: 'e2e-public-site',
          url: `${PUBLIC_URL}/api/revalidate`,
          secret: process.env.REVALIDATE_SECRET ?? 'e2e-secret',
          events: ['content.published', 'content.unpublished', 'content.archived', 'navigation.updated', 'settings.updated', 'redirects.updated'],
        },
      });
      expect(reg.status(), await reg.text()).toBe(201);
    }

    // Keep the proxy's TTL cache short so the assertions below do not wait a minute
    // for a cached miss to expire; the default is restored afterwards.
    const ttl = await api.put('/api/v1/admin/settings/redirects.cacheSeconds', { data: { value: '1' } });
    expect(ttl.ok(), await ttl.text()).toBeTruthy();
  });

  test.afterAll(async () => {
    if (api) {
      await api.post('/api/v1/admin/settings/redirects.cacheSeconds/reset');
      await api.dispose();
    }
  });

  test('a slug change on a published page 301s the old URL to the new one', async ({ request }) => {
    const stamp = Date.now().toString(36);
    const oldSlug = `e2e/redirect-${stamp}`;
    const newSlug = `e2e/moved-${stamp}`;

    // Publish a standard page.
    const create = await api.post('/api/v1/content', {
      data: {
        slug: oldSlug,
        contentTypeName: 'standard_page',
        fieldsJson: JSON.stringify({ title: `Redirect test ${stamp}`, body: 'Body for the redirect test.' }),
      },
    });
    expect(create.status(), await create.text()).toBe(201);
    const { id } = (await create.json()) as { id: number };

    const publish = await api.post(`/api/v1/content/${id}/publish`);
    expect(publish.status(), await publish.text()).toBe(204);

    const live = await waitForStatus(request, `${PUBLIC_URL}/pages/${oldSlug}`, [200]);
    expect(live.status()).toBe(200);

    // Change the slug.
    const rename = await api.patch(`/api/v1/content/${id}/slug`, { data: { slug: newSlug } });
    expect(rename.status(), await rename.text()).toBe(204);

    // The API resolves the old public path…
    const resolved = await request.get(`${API_URL}/api/v1/redirects/resolve?path=/pages/${oldSlug}`);
    expect(resolved.status()).toBe(200);
    expect(await resolved.json()).toMatchObject({ toPath: `/pages/${newSlug}`, statusCode: 301 });
    expect(resolved.headers()['cache-control']).toMatch(/max-age=\d+/);

    // …and the site 301s it (the proxy may hold a cached miss briefly; the page-route
    // fallback answers 308 in that window — both are "moved permanently").
    const moved = await waitForStatus(request, `${PUBLIC_URL}/pages/${oldSlug}?utm=e2e`, [301, 308]);
    expect([301, 308]).toContain(moved.status());
    expect(moved.headers()['location']).toMatch(new RegExp(`/pages/${newSlug.replace('/', '\\/')}(\\?utm=e2e)?$`));

    // Following the redirect lands on the page at its new URL.
    const followed = await request.get(`${PUBLIC_URL}/pages/${oldSlug}`);
    expect(followed.status()).toBe(200);
    expect(followed.url()).toContain(`/pages/${newSlug}`);
    expect(await followed.text()).toContain(`Redirect test ${stamp}`);
  });

  test('an admin-created rule is served with its status and keeps the query string', async ({ request }) => {
    const stamp = Date.now().toString(36);
    const from = `/legacy/${stamp}`;
    const to = `/pages/e2e/target-${stamp}`;

    const create = await api.post('/api/v1/redirects', { data: { fromPath: from, toPath: to, statusCode: 302 } });
    expect(create.status(), await create.text()).toBe(201);

    const resp = await waitForStatus(request, `${PUBLIC_URL}${from}?q=1`, [302]);
    expect(resp.status()).toBe(302);
    expect(resp.headers()['location']).toBe(`${to}?q=1`);
  });

  test('the API refuses a rule that would loop and flattens a chain', async () => {
    const stamp = Date.now().toString(36);
    const a = `/loop/a-${stamp}`;
    const b = `/loop/b-${stamp}`;
    const c = `/loop/c-${stamp}`;

    expect((await api.post('/api/v1/redirects', { data: { fromPath: b, toPath: c, statusCode: 301 } })).status()).toBe(201);

    const chained = await api.post('/api/v1/redirects', { data: { fromPath: a, toPath: b, statusCode: 301 } });
    expect(chained.status()).toBe(201);
    expect(((await chained.json()) as { toPath: string }).toPath).toBe(c); // a → c, not a → b → c

    const loop = await api.post('/api/v1/redirects', { data: { fromPath: c, toPath: a, statusCode: 301 } });
    expect(loop.status()).toBe(400);
    expect(await loop.text()).toMatch(/loop/i);
  });
});
