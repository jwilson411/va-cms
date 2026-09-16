/**
 * theme-check-template.test.tsx
 *
 * Issue #17 — USWDS 3.x with VA theme tokens
 * AC: A test page renders at least one USWDS component (usa-button).
 * AC: axe-core zero critical violations.
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { ThemeCheckTemplate } from './ThemeCheckTemplate';

expect.extend(toHaveNoViolations);

describe('ThemeCheckTemplate', () => {
  it('renders USWDS button variants and the VA colour swatches', () => {
    render(<ThemeCheckTemplate />);
    expect(screen.getByRole('button', { name: 'Primary (VA Blue)' })).toHaveClass('usa-button');
    expect(screen.getByRole('button', { name: 'Accent warm (VA Gold)' })).toHaveClass(
      'usa-button--accent-warm',
    );
    const swatches = screen.getByTestId('theme-swatches');
    expect(swatches).toHaveTextContent('#003e73');
    expect(swatches).toHaveTextContent('#f9c642');
  });

  it('has no axe violations', async () => {
    const { container } = render(<ThemeCheckTemplate />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
