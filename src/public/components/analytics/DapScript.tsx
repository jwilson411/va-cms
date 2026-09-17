/**
 * DapScript — DAP (Digital Analytics Program) script injection.
 *
 * Issue #60 — BRD FR-ANALYTICS-02:
 *   Injects the standard government DAP / GA4 analytics script tag on all
 *   public pages. Agency and sub-agency codes are read from environment
 *   variables at build/render time.
 *
 * Environment variables (set in .env.local or deployment config):
 *   analytics.dapEnabled   — site setting; master switch
 *   analytics.dapAgency    — site setting; agency code, e.g. "VA"
 *   analytics.dapSubagency — site setting; sub-agency code, e.g. "VHA" (optional)
 * (issue #149, epic #141 — no environment variables are read)
 *
 * The script uses next/script with strategy="afterInteractive" so it loads
 * asynchronously and never blocks the initial page render.
 */

import Script from 'next/script';

const DAP_SRC = 'https://dap.digitalgov.gov/Universal-Federated-Analytics-Min.js';

export interface DapScriptProps {
  /**
   * Master switch — the analytics.dapEnabled site setting. Defaults to true so a
   * caller that only passes an agency still gets the tag.
   */
  enabled?: boolean;
  /** Agency code for DAP (e.g. "VA") — the analytics.dapAgency site setting. */
  agency?: string;
  /** Sub-agency code for DAP (e.g. "VHA") — the analytics.dapSubagency site setting. */
  subagency?: string;
  /** Per-request CSP nonce (#162) so the tag is allowed under script-src without 'unsafe-inline'. */
  nonce?: string;
}

/**
 * DapScript renders the Digital Analytics Program script tag.
 *
 * - Uses next/script strategy="afterInteractive" — equivalent to `defer`.
 *   The browser parses and executes the script only after the page is
 *   interactive, so it never blocks first paint or hydration.
 * - If agency is not configured the component renders nothing rather than
 *   emitting an unconfigured tag.
 */
export function DapScript({ enabled = true, agency, subagency, nonce }: DapScriptProps) {
  const resolvedAgency = (agency ?? '').trim();
  const resolvedSubagency = (subagency ?? '').trim();

  if (!enabled || !resolvedAgency) {
    // Do not inject if disabled or the agency is not configured — avoids unconfigured DAP hits.
    return null;
  }

  const params = new URLSearchParams({ agency: resolvedAgency });
  if (resolvedSubagency) {
    params.set('subagency', resolvedSubagency);
  }

  const src = `${DAP_SRC}?${params.toString()}`;

  return (
    <Script
      src={src}
      strategy="afterInteractive"
      id="dap-analytics"
      nonce={nonce}
    />
  );
}
