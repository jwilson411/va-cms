/**
 * axe-helper.ts
 *
 * Shared helper: runs axe-core on the current page, filters to
 * critical + serious violations, writes results to the results dir,
 * and returns a summary for assertion.
 *
 * CI gate: any critical or serious violation fails the test.
 * Impact levels: critical > serious > moderate > minor
 */

import { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';

export interface A11yResult {
  url: string;
  route: string;
  violations: AxeViolation[];
  criticalOrSerious: AxeViolation[];
  passCount: number;
  incompleteCount: number;
  timestamp: string;
}

export interface AxeViolation {
  id: string;
  impact: string | undefined;
  description: string;
  helpUrl: string;
  nodeCount: number;
  nodes: { html: string; target: string[] }[];
}

// Results directory relative to this package root (../../results)
const RESULTS_DIR = path.resolve(__dirname, '..', 'results');

/**
 * Run axe-core on the current page state.
 *
 * @param page    - Playwright page (must be navigated to target URL first)
 * @param route   - Human-readable route name for the results file
 * @param options - Optional axe builder customisations
 */
export async function runAxe(
  page: Page,
  route: string,
  options?: {
    /** Disable specific axe rules by ID (use sparingly — document why) */
    disableRules?: string[];
    /** Include only these axe rules */
    includeRules?: string[];
    /** Restrict to a specific selector within the page */
    withinSelector?: string;
  },
): Promise<A11yResult> {
  // Allow the page to fully render (animations, async data)
  await page.waitForLoadState('networkidle').catch(() => {
    // networkidle may not resolve on pages with long-polling; fall back
    // to domcontentloaded
  });

  let builder = new AxeBuilder({ page })
    // WCAG 2.1 Level AA — the standard required by Section 508 (2017 refresh)
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'best-practice']);

  if (options?.disableRules?.length) {
    builder = builder.disableRules(options.disableRules);
  }
  if (options?.includeRules?.length) {
    builder = builder.withRules(options.includeRules);
  }
  if (options?.withinSelector) {
    builder = builder.include(options.withinSelector);
  }

  const results = await builder.analyze();

  const violations: AxeViolation[] = results.violations.map((v) => ({
    id: v.id,
    impact: v.impact ?? undefined,
    description: v.description,
    helpUrl: v.helpUrl,
    nodeCount: v.nodes.length,
    nodes: v.nodes.slice(0, 5).map((n) => ({
      html: n.html,
      target: n.target.map(String),
    })),
  }));

  const criticalOrSerious = violations.filter(
    (v) => v.impact === 'critical' || v.impact === 'serious',
  );

  const result: A11yResult = {
    url: page.url(),
    route,
    violations,
    criticalOrSerious,
    passCount: results.passes.length,
    incompleteCount: results.incomplete.length,
    timestamp: new Date().toISOString(),
  };

  // Write per-route JSON to results dir
  fs.mkdirSync(RESULTS_DIR, { recursive: true });
  const safeRoute = route.replace(/[^a-zA-Z0-9-_]/g, '_');
  const outPath = path.join(RESULTS_DIR, `${safeRoute}.json`);
  fs.writeFileSync(outPath, JSON.stringify(result, null, 2), 'utf-8');

  return result;
}

/**
 * Assert zero critical or serious violations.
 * Produces a readable failure message listing each violation.
 */
export function assertNoViolations(result: A11yResult): void {
  if (result.criticalOrSerious.length === 0) return;

  const details = result.criticalOrSerious
    .map(
      (v) =>
        `  [${v.impact?.toUpperCase()}] ${v.id}: ${v.description}\n` +
        `    Help: ${v.helpUrl}\n` +
        `    Affected nodes (${v.nodeCount}):\n` +
        v.nodes
          .map((n) => `      - ${n.target.join(' > ')}\n        ${n.html.slice(0, 120)}`)
          .join('\n'),
    )
    .join('\n\n');

  throw new Error(
    `axe-core found ${result.criticalOrSerious.length} critical/serious violation(s) on "${result.route}" (${result.url}):\n\n${details}`,
  );
}
