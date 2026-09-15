import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for VA CMS axe-core accessibility tests.
 *
 * Tests target the running admin SPA (http://localhost:5173 in dev, or
 * whatever BASE_URL is set in the environment).
 *
 * CI gate: zero critical or serious axe-core violations on any page.
 * Results written to tests/accessibility/results/ (JSON + HTML reports).
 */

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:5173';

export default defineConfig({
  testDir: './tests',
  outputDir: './results/test-artifacts',
  reporter: [
    ['list'],
    ['json', { outputFile: './results/a11y-results.json' }],
    ['html', { outputFolder: './results/html-report', open: 'never' }],
  ],
  use: {
    baseURL: BASE_URL,
    // DevBypass auth: API accepts X-Dev-User header in development mode.
    // Tests inject a JWT by calling the DevBypass endpoint before each suite.
    extraHTTPHeaders: {
      'X-Dev-User': 'alice@va.gov',
    },
    // Capture screenshots on failure for evidence
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // No built-in webServer: tests assume the app is running.
  // CI starts the dev server before running this suite.
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 30_000,
});
