/**
 * auth-fixture.ts
 *
 * Playwright fixture that authenticates against the VA CMS DevBypass API by
 * signing in through the same browser context every test page uses.
 *
 * AuthContext (src/admin/src/context/AuthContext.tsx) keeps the JWT in React
 * state ONLY — never localStorage or sessionStorage (that's an explicit
 * acceptance criterion, #163/#164). On mount it authenticates itself purely by
 * POSTing to /api/auth/refresh with `credentials: 'include'`, relying on the
 * httpOnly `cms_rt` refresh cookie DevBypass login sets. So the only way to
 * pre-authenticate a test page is to make sure that cookie is already sitting
 * in the page's own browser context before it loads.
 *
 * We do that with `context.request` (not the top-level `request` fixture):
 * `context.request` is a BrowserContext's own APIRequestContext, so any
 * Set-Cookie it receives lands in the *same cookie jar* every `page.goto()`
 * in that context reads from. The top-level `request` fixture is a separate
 * APIRequestContext with its own cookie jar — a token acquired there never
 * reaches the browser at all, which is what silently broke this fixture: it
 * called `request.post()` for the token and then tried to hand the SPA a JWT
 * via `localStorage`, a mechanism AuthContext stopped reading from once the
 * memory-only/cookie-refresh design landed. Every test page was therefore
 * unauthenticated, and every `<ProtectedRoute>` hard-navigated to
 * /api/auth/login (which 302s to /login) — axe just caught that near-empty
 * redirect at different points per route, passing on some pages and failing
 * `document-title`/`html-has-lang` on others, depending on timing.
 *
 * The dev-login call goes through `baseURL` (the admin dev server, e.g.
 * :5173), the same origin every page navigates to — not directly at the API
 * — so the cookie's scope matches what the SPA's own `fetch('/api/...')`
 * calls see. Vite's dev proxy forwards it to the API (VITE_API_PROXY).
 *
 * If the API is not running, dev-login fails and tests fall through to the
 * real (accessible) /login page via the ProtectedRoute redirect, rather than
 * blocking the whole suite.
 */

import { test as base, expect, Page, BrowserContext } from '@playwright/test';

const DEV_USER = 'alice@va.gov';

export interface AuthFixtures {
  /** Authenticated page — the browser context already holds a valid refresh cookie. */
  adminPage: Page;
  authContext: BrowserContext;
}

export const test = base.extend<AuthFixtures>({
  authContext: async ({ browser, baseURL }, use) => {
    const ctx = await browser.newContext({ baseURL });

    const resp = await ctx.request
      .post('/api/auth/dev-login', { headers: { 'X-Dev-User': DEV_USER } })
      .catch(() => null);
    if (!resp || !resp.ok()) {
      // eslint-disable-next-line no-console
      console.warn(
        `[auth-fixture] DevBypass login ${resp ? `returned ${resp.status()}` : 'was unreachable'} — ` +
          'tests will see the ProtectedRoute redirect to /login instead of the authenticated page.',
      );
    }

    await use(ctx);
    await ctx.close();
  },

  adminPage: async ({ authContext }, use) => {
    const page = await authContext.newPage();
    await use(page);
    await page.close();
  },
});

export { expect };
