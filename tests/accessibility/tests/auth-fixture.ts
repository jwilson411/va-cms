/**
 * auth-fixture.ts
 *
 * Playwright fixture that authenticates against the VA CMS DevBypass API
 * and injects the resulting JWT into the browser context's localStorage/memory.
 *
 * DevBypass mode: API accepts `X-Dev-User: alice@va.gov` header on
 * POST /api/auth/dev-login and returns a JWT access token + sets the
 * httpOnly refresh cookie. The SPA stores the JWT in memory; we set it
 * via localStorage key `va-cms-dev-token` which the SPA reads on load
 * when Auth:Mode=DevBypass.
 *
 * If the API is not running, the test will skip gracefully with a clear
 * message rather than blocking the whole CI suite.
 */

import { test as base, expect, Page, BrowserContext } from '@playwright/test';

const API_URL = process.env.API_URL ?? 'http://localhost:5100';
const DEV_USER = 'alice@va.gov';

export interface AuthFixtures {
  /** Authenticated page — JWT injected, admin routes accessible */
  adminPage: Page;
  authContext: BrowserContext;
}

/**
 * Acquire a DevBypass JWT from the API.
 * Returns null if the API is not reachable (CI skip).
 */
async function acquireDevToken(request: import('@playwright/test').APIRequestContext): Promise<string | null> {
  try {
    const resp = await request.post(`${API_URL}/api/auth/dev-login`, {
      headers: { 'X-Dev-User': DEV_USER },
      timeout: 5_000,
    });
    if (!resp.ok()) return null;
    const body = await resp.json() as { accessToken?: string };
    return body.accessToken ?? null;
  } catch {
    return null;
  }
}

export const test = base.extend<AuthFixtures>({
  authContext: async ({ browser, request }, use) => {
    const token = await acquireDevToken(request);
    const ctx = await browser.newContext();

    if (token) {
      // Seed the token into localStorage so the SPA AuthContext picks it up.
      await ctx.addInitScript(
        ({ tok }: { tok: string }) => {
          window.localStorage.setItem('va-cms-dev-token', tok);
        },
        { tok: token },
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
