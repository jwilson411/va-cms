/**
 * admin-screens.a11y.spec.ts
 *
 * axe-core Playwright accessibility tests for all VA CMS admin screens.
 *
 * Acceptance criteria (issue #62):
 *   - Playwright + axe-core test exists for each admin route:
 *     dashboard, content list, content editor, media library,
 *     nav editor, users, audit, settings
 *   - Tests fail CI if any critical or serious violation found
 *   - Test results written to tests/accessibility/results/
 *
 * Related: Epic #14 — Section 508 & Accessibility Hardening
 * BRD ref: NFR-A11Y-03
 * Standard: WCAG 2.1 AA / Section 508 (2017 refresh)
 */

import { test, expect } from './auth-fixture';
import { runAxe, assertNoViolations } from './axe-helper';

// ── Admin routes under test ──────────────────────────────────────────────────
// Derived from src/admin/src/main.tsx route definitions (issue #62 scope).
const ADMIN_ROUTES = [
  { route: 'admin-dashboard',      path: '/admin' },
  { route: 'admin-content-list',   path: '/admin/content' },
  { route: 'admin-content-editor', path: '/admin/content/new' },
  { route: 'admin-media-library',  path: '/admin/media' },
  { route: 'admin-nav-editor',     path: '/admin/navigation' },
  { route: 'admin-users',          path: '/admin/users' },
  { route: 'admin-audit-log',      path: '/admin/audit' },
  { route: 'admin-settings',       path: '/admin/settings' },
] as const;

// ── Dashboard ────────────────────────────────────────────────────────────────

test.describe('Admin Dashboard', () => {
  test('has zero critical or serious axe violations', async ({ adminPage }) => {
    await adminPage.goto('/admin');
    const result = await runAxe(adminPage, 'admin-dashboard');
    assertNoViolations(result);
  });
});

// ── Content List ─────────────────────────────────────────────────────────────

test.describe('Admin Content List', () => {
  test('has zero critical or serious axe violations', async ({ adminPage }) => {
    await adminPage.goto('/admin/content');
    const result = await runAxe(adminPage, 'admin-content-list');
    assertNoViolations(result);
  });

  test('filter controls are labelled and accessible', async ({ adminPage }) => {
    await adminPage.goto('/admin/content');

    // Verify filter fieldset has accessible legend
    const fieldset = adminPage.locator('fieldset.usa-fieldset');
    await expect(fieldset).toBeVisible();

    // All visible form inputs must have associated labels (axe checks this,
    // but an explicit assertion pinpoints the failure faster in CI)
    const inputs = adminPage.locator('input.usa-input, select.usa-select');
    for (const input of await inputs.all()) {
      const id = await input.getAttribute('id');
      if (id) {
        const label = adminPage.locator(`label[for="${id}"]`);
        await expect(label, `Input #${id} must have a <label for>`).toBeVisible();
      }
    }

    const result = await runAxe(adminPage, 'admin-content-list-filters');
    assertNoViolations(result);
  });
});

// ── Content Editor ───────────────────────────────────────────────────────────

test.describe('Admin Content Editor', () => {
  test('has zero critical or serious axe violations on new-entry form', async ({ adminPage }) => {
    // The editor loads without a content type; we navigate to the form route
    // and let it render whatever initial state exists (loading state included).
    await adminPage.goto('/admin/content/new');
    const result = await runAxe(adminPage, 'admin-content-editor');
    assertNoViolations(result);
  });
});

// ── Media Library ────────────────────────────────────────────────────────────

test.describe('Admin Media Library', () => {
  test('has zero critical or serious axe violations', async ({ adminPage }) => {
    await adminPage.goto('/admin/media');
    const result = await runAxe(adminPage, 'admin-media-library');
    assertNoViolations(result);
  });
});

// ── Navigation Editor ────────────────────────────────────────────────────────

test.describe('Admin Navigation Editor', () => {
  test('has zero critical or serious axe violations', async ({ adminPage }) => {
    await adminPage.goto('/admin/navigation');
    const result = await runAxe(adminPage, 'admin-nav-editor');
    assertNoViolations(result);
  });
});

// ── Users ────────────────────────────────────────────────────────────────────

test.describe('Admin Users', () => {
  test('has zero critical or serious axe violations on user list', async ({ adminPage }) => {
    await adminPage.goto('/admin/users');
    const result = await runAxe(adminPage, 'admin-users');
    assertNoViolations(result);
  });
});

// ── Audit Log ────────────────────────────────────────────────────────────────

test.describe('Admin Audit Log', () => {
  test('has zero critical or serious axe violations', async ({ adminPage }) => {
    await adminPage.goto('/admin/audit');
    const result = await runAxe(adminPage, 'admin-audit-log');
    assertNoViolations(result);
  });
});

// ── Settings ─────────────────────────────────────────────────────────────────

test.describe('Admin Settings', () => {
  test('has zero critical or serious axe violations', async ({ adminPage }) => {
    await adminPage.goto('/admin/settings');
    const result = await runAxe(adminPage, 'admin-settings');
    assertNoViolations(result);
  });
});

// ── Login Page ───────────────────────────────────────────────────────────────
// Included because it's an admin entry screen (WCAG requirement extends to auth pages)

test.describe('Login Page', () => {
  test('has zero critical or serious axe violations', async ({ page }) => {
    // Use base page (not authenticated) to test the real login form
    await page.goto('/login');
    const result = await runAxe(page, 'admin-login');
    assertNoViolations(result);
  });
});

// ── Smoke: all routes reachable and produce results ──────────────────────────

test.describe('All admin routes produce axe result files', () => {
  for (const { route, path } of ADMIN_ROUTES) {
    test(`${route} produces a results file`, async ({ adminPage }) => {
      await adminPage.goto(path);
      const result = await runAxe(adminPage, route);

      // Always write results — pass or fail. CI gate is assertNoViolations above.
      expect(result.route).toBe(route);
      expect(result.timestamp).toBeTruthy();
    });
  }
});
