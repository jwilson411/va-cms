/**
 * error-page-templates.test.tsx
 *
 * Tests for:
 *  - NotFoundTemplate (404 error page)
 *  - ServerErrorTemplate (500 error page)
 *
 * Issue #61 — 404 and 500 error page templates
 * AC: 404 page uses USWDS Alert (info) and suggests search or home link.
 * AC: 500 page uses USWDS Alert (error) with a friendly plain-language message.
 * AC: Both include Banner and Identifier.
 * AC: Both pass axe-core with zero critical violations.
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { NotFoundTemplate } from './NotFoundTemplate';
import { ServerErrorTemplate } from './ServerErrorTemplate';

expect.extend(toHaveNoViolations);

const baseNav = [
  { label: 'Home', href: '/' },
  { label: 'News', href: '/news' },
];

// ---------------------------------------------------------------------------
// NotFoundTemplate (404)
// ---------------------------------------------------------------------------

describe('NotFoundTemplate', () => {
  it('renders the USWDS Banner', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('renders the USWDS Header', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-header')).not.toBeNull();
  });

  it('renders navigation items in header', () => {
    render(<NotFoundTemplate navigation={baseNav} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('News')).toBeTruthy();
  });

  it('renders main element with id=main-content', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    expect(container.querySelector('main#main-content')).not.toBeNull();
  });

  it('renders an H1 for the page title', () => {
    render(<NotFoundTemplate navigation={baseNav} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toContain('Page not found');
  });

  it('renders a USWDS info alert', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    const alert = container.querySelector('.usa-alert.usa-alert--info');
    expect(alert).not.toBeNull();
  });

  it('alert heading indicates page not found', () => {
    render(<NotFoundTemplate navigation={baseNav} />);
    // The alert heading is an H2
    const heading = screen.getByRole('heading', { level: 2 });
    expect(heading.textContent).toContain("can't find that page");
  });

  it('renders a link to site search', () => {
    render(<NotFoundTemplate navigation={baseNav} />);
    const searchLink = screen.getByRole('link', { name: /site search/i });
    expect(searchLink).toBeTruthy();
    expect((searchLink as HTMLAnchorElement).href).toContain('/search');
  });

  it('renders a link to the home page', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    // The alert body contains a home page link
    const alert = container.querySelector('.usa-alert__text a[href="/"]');
    expect(alert).not.toBeNull();
  });

  it('renders the USWDS Footer', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders the USWDS Identifier', () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  // AC: Tests fail CI if any critical OR serious violation found
  it('passes axe-core with zero serious violations', async () => {
    const { container } = render(<NotFoundTemplate navigation={baseNav} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// ServerErrorTemplate (500)
// ---------------------------------------------------------------------------

describe('ServerErrorTemplate', () => {
  it('renders the USWDS Banner', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('renders the USWDS Header', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-header')).not.toBeNull();
  });

  it('renders navigation items in header', () => {
    render(<ServerErrorTemplate navigation={baseNav} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('News')).toBeTruthy();
  });

  it('renders main element with id=main-content', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    expect(container.querySelector('main#main-content')).not.toBeNull();
  });

  it('renders an H1 for the page title', () => {
    render(<ServerErrorTemplate navigation={baseNav} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toContain('Something went wrong');
  });

  it('renders a USWDS error alert', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    const alert = container.querySelector('.usa-alert.usa-alert--error');
    expect(alert).not.toBeNull();
  });

  it('alert has role=alert for ARIA live announcement', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    const alert = container.querySelector('.usa-alert.usa-alert--error');
    expect(alert?.getAttribute('role')).toBe('alert');
  });

  it('alert heading conveys an error occurred', () => {
    render(<ServerErrorTemplate navigation={baseNav} />);
    const heading = screen.getByRole('heading', { level: 2 });
    expect(heading.textContent).toContain('error occurred');
  });

  it('alert body contains a friendly plain-language message', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    const alertBody = container.querySelector('.usa-alert__body');
    expect(alertBody?.textContent).toContain('technical problem');
  });

  it('renders a link back to the home page in the error body', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    const homeLink = container.querySelector('.usa-alert__text a[href="/"]');
    expect(homeLink).not.toBeNull();
    expect(homeLink?.textContent).toContain('home page');
  });

  it('renders the USWDS Footer', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders the USWDS Identifier', () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('renders with empty navigation (error boundary fallback)', () => {
    const { container } = render(<ServerErrorTemplate navigation={[]} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core with empty navigation (error boundary fallback)', async () => {
    const { container } = render(<ServerErrorTemplate navigation={[]} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  // AC: Tests fail CI if any critical OR serious violation found
  it('passes axe-core with zero serious violations', async () => {
    const { container } = render(<ServerErrorTemplate navigation={baseNav} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });
});
