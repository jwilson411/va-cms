/**
 * DapScript component tests — issue #60
 *
 * Acceptance criteria:
 * - DAP script tag in <head> on all public pages (verified via Script component render)
 * - Agency and sub-agency parameters configurable via environment variable
 * - Script does not block rendering (strategy="afterInteractive")
 */

import React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { DapScript } from './DapScript';

// next/script is a server-side Next.js component; stub it so Vitest/jsdom can
// render it and we can assert on the src and id props it receives.
vi.mock('next/script', () => ({
  default: ({ src, strategy, id }: { src: string; strategy: string; id: string }) => (
    // Render as a plain <script> tag so we can query it with the DOM
    <script data-testid="dap-script" data-src={src} data-strategy={strategy} id={id} />
  ),
}));

const DAP_BASE = 'https://dap.digitalgov.gov/Universal-Federated-Analytics-Min.js';

describe('DapScript', () => {
  const originalEnv = process.env;

  beforeEach(() => {
    // Reset env before each test
    process.env = { ...originalEnv };
    delete process.env.NEXT_PUBLIC_DAP_AGENCY;
    delete process.env.NEXT_PUBLIC_DAP_SUBAGENCY;
  });

  afterEach(() => {
    process.env = originalEnv;
  });

  // ---------------------------------------------------------------------------
  // Script presence
  // ---------------------------------------------------------------------------

  it('renders script tag when agency prop is provided', () => {
    const { container } = render(<DapScript agency="VA" />);
    const script = container.querySelector('[data-testid="dap-script"]');
    expect(script).not.toBeNull();
  });

  it('renders nothing when agency is not configured (no prop, no env var)', () => {
    const { container } = render(<DapScript />);
    const script = container.querySelector('[data-testid="dap-script"]');
    expect(script).toBeNull();
  });

  it('ignores NEXT_PUBLIC_DAP_AGENCY — configuration comes from site settings (epic #141)', () => {
    process.env.NEXT_PUBLIC_DAP_AGENCY = 'VA';
    const { container } = render(<DapScript />);
    const script = container.querySelector('[data-testid="dap-script"]');
    expect(script).toBeNull();
  });

  it('renders nothing when enabled=false even if an agency is configured', () => {
    const { container } = render(<DapScript enabled={false} agency="VA" />);
    expect(container.querySelector('[data-testid="dap-script"]')).toBeNull();
  });

  it('renders nothing when the agency is blank', () => {
    const { container } = render(<DapScript enabled agency="   " />);
    expect(container.querySelector('[data-testid="dap-script"]')).toBeNull();
  });

  // ---------------------------------------------------------------------------
  // Agency parameter
  // ---------------------------------------------------------------------------

  it('includes agency param in script src from prop', () => {
    const { container } = render(<DapScript agency="VA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).toContain(`${DAP_BASE}?`);
    expect(src).toContain('agency=VA');
  });

  it('env var never leaks into the src', () => {
    process.env.NEXT_PUBLIC_DAP_AGENCY = 'DOD';
    const { container } = render(<DapScript agency="VA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).toContain('agency=VA');
    expect(src).not.toContain('agency=DOD');
  });

  // ---------------------------------------------------------------------------
  // Sub-agency parameter
  // ---------------------------------------------------------------------------

  it('includes subagency param in src when subagency prop is provided', () => {
    const { container } = render(<DapScript agency="VA" subagency="VHA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).toContain('subagency=VHA');
  });

  it('does not include subagency param when not configured', () => {
    const { container } = render(<DapScript agency="VA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).not.toContain('subagency');
  });

  it('env var subagency never leaks into the src', () => {
    process.env.NEXT_PUBLIC_DAP_AGENCY = 'VA';
    process.env.NEXT_PUBLIC_DAP_SUBAGENCY = 'VBA';
    const { container } = render(<DapScript agency="VA" subagency="VHA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).toContain('subagency=VHA');
    expect(src).not.toContain('subagency=VBA');
  });

  // ---------------------------------------------------------------------------
  // Non-blocking rendering (strategy)
  // ---------------------------------------------------------------------------

  it('uses afterInteractive strategy so script does not block rendering', () => {
    const { container } = render(<DapScript agency="VA" />);
    const strategy = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-strategy');
    expect(strategy).toBe('afterInteractive');
  });

  // ---------------------------------------------------------------------------
  // Script ID
  // ---------------------------------------------------------------------------

  it('assigns id="dap-analytics" to prevent duplicate injection', () => {
    const { container } = render(<DapScript agency="VA" />);
    const id = container.querySelector('[data-testid="dap-script"]')?.getAttribute('id');
    expect(id).toBe('dap-analytics');
  });

  // ---------------------------------------------------------------------------
  // URL format
  // ---------------------------------------------------------------------------

  it('builds correct src URL with agency and subagency', () => {
    const { container } = render(<DapScript agency="VA" subagency="VHA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).toBe(`${DAP_BASE}?agency=VA&subagency=VHA`);
  });

  it('builds correct src URL with agency only', () => {
    const { container } = render(<DapScript agency="VA" />);
    const src = container.querySelector('[data-testid="dap-script"]')?.getAttribute('data-src') ?? '';
    expect(src).toBe(`${DAP_BASE}?agency=VA`);
  });
});
