/**
 * DapScript — DAP (Digital Analytics Program) script injection.
 *
 * Issue #60 — BRD FR-ANALYTICS-02:
 *   Injects the standard government DAP / GA4 analytics script tag on all
 *   public pages. Agency and sub-agency codes are read from environment
 *   variables at build/render time.
 *
 * Environment variables (set in .env.local or deployment config):
 *   NEXT_PUBLIC_DAP_AGENCY    — Agency code, e.g. "VA"
 *   NEXT_PUBLIC_DAP_SUBAGENCY — Sub-agency code, e.g. "VHA" (optional)
 *
 * The script uses next/script with strategy="afterInteractive" so it loads
 * asynchronously and never blocks the initial page render.
 */

import Script from 'next/script';

const DAP_SRC = 'https://dap.digitalgov.gov/Universal-Federated-Analytics-Min.js';

export interface DapScriptProps {
  /** Agency code for DAP (e.g. "VA"). Defaults to NEXT_PUBLIC_DAP_AGENCY env var. */
  agency?: string;
  /** Sub-agency code for DAP (e.g. "VHA"). Defaults to NEXT_PUBLIC_DAP_SUBAGENCY env var. */
  subagency?: string;
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
export function DapScript({ agency, subagency }: DapScriptProps) {
  const resolvedAgency = agency ?? process.env.NEXT_PUBLIC_DAP_AGENCY ?? '';
  const resolvedSubagency = subagency ?? process.env.NEXT_PUBLIC_DAP_SUBAGENCY ?? '';

  if (!resolvedAgency) {
    // Do not inject if agency is not configured — avoids unconfigured DAP hits.
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
    />
  );
}
