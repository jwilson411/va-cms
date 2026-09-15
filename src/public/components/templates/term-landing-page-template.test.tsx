/**
 * term-landing-page-template.test.tsx
 *
 * Tests for the TermLandingPageTemplate component.
 *
 * Issue #63 — Wire axe-core Playwright a11y tests for all public templates
 * Epic #14 — Section 508 & Accessibility Hardening
 *
 * AC: Playwright + axe-core test exists for: Term Landing Page
 * AC: Tests fail CI if any critical or serious violation found
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import {
  TermLandingPageTemplate,
  TermEntryItem,
} from './TermLandingPageTemplate';

expect.extend(toHaveNoViolations);

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const baseNav = [
  { label: 'Home', href: '/' },
  { label: 'News', href: '/news' },
];

const baseProps = {
  taxonomyName: 'Topics',
  termName: 'Mental Health',
  navigation: baseNav,
};

const sampleEntries: TermEntryItem[] = [
  {
    id: 1,
    title: 'VA Expands Mental Health Services',
    slug: 'news/va-expands-mental-health-services',
    excerpt: 'Veterans now have access to more mental health resources nationwide.',
    contentTypeName: 'News Article',
    publishedAt: '2026-09-15T00:00:00Z',
  },
  {
    id: 2,
    title: 'Mental Health Resources for Veterans',
    slug: 'pages/mental-health-resources',
    excerpt: 'A guide to mental health resources available to all veterans.',
    contentTypeName: 'Standard Page',
    publishedAt: '2026-08-01T00:00:00Z',
  },
  {
    id: 3,
    title: 'Crisis Hotline Information',
    slug: 'pages/crisis-hotline',
    excerpt: null,
    contentTypeName: 'Standard Page',
    publishedAt: null,
  },
];

const fullProps = {
  ...baseProps,
  description: 'Mental health content and resources for veterans and their families.',
  entries: sampleEntries,
  totalEntries: 3,
  breadcrumbs: [
    { label: 'Home', href: '/' },
    { label: 'Topics', href: '/topics' },
    { label: 'Mental Health' },
  ],
};

// ---------------------------------------------------------------------------
// Mandatory USWDS chrome
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — mandatory USWDS chrome', () => {
  it('renders the USWDS Banner (.usa-banner)', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('renders the USWDS Header (.usa-header)', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('.usa-header')).not.toBeNull();
  });

  it('renders navigation items in the header', () => {
    render(<TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('News')).toBeTruthy();
  });

  it('renders the USWDS Footer (.usa-footer--big)', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders the USWDS Identifier (.usa-identifier)', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('renders main element with id=main-content', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('main#main-content')).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Term heading and taxonomy badge
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — heading and badge', () => {
  it('renders H1 with the term name', () => {
    render(<TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toBe('Mental Health');
  });

  it('renders a usa-tag badge with the taxonomy name', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    const tag = container.querySelector('span.usa-tag');
    expect(tag).not.toBeNull();
    expect(tag!.textContent).toBe('Topics');
  });

  it('tag has aria-label indicating taxonomy', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    const tag = container.querySelector('span.usa-tag');
    expect(tag!.getAttribute('aria-label')).toBe('Taxonomy: Topics');
  });
});

// ---------------------------------------------------------------------------
// Description
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — description', () => {
  it('renders description when provided', () => {
    const { container } = render(
      <TermLandingPageTemplate
        {...baseProps}
        description="Mental health content for veterans."
        entries={[]}
        totalEntries={0}
      />,
    );
    expect(container.querySelector('p.usa-intro')).not.toBeNull();
    expect(container.querySelector('p.usa-intro')!.textContent).toContain(
      'Mental health content for veterans.',
    );
  });

  it('does not render intro paragraph when description is absent', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('p.usa-intro')).toBeNull();
  });

  it('does not render intro paragraph when description is null', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} description={null} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('p.usa-intro')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Result count
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — result count', () => {
  it('renders a status region with result count', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    const status = container.querySelector('[role="status"]');
    expect(status).not.toBeNull();
    expect(status!.textContent).toContain('3');
    expect(status!.textContent).toContain('Mental Health');
  });

  it('renders zero-results message when totalEntries is 0', () => {
    render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    const status = screen.getByRole('status');
    expect(status.textContent).toContain('No content tagged with');
    expect(status.textContent).toContain('Mental Health');
  });
});

// ---------------------------------------------------------------------------
// Content entry cards
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — entry cards', () => {
  it('renders a usa-card-group list for entries', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    expect(container.querySelector('ul.usa-card-group')).not.toBeNull();
  });

  it('renders correct number of list items', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    const items = container.querySelectorAll('ul.usa-card-group > li');
    expect(items).toHaveLength(3);
  });

  it('renders entry title as a link', () => {
    render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    const link = screen.getByRole('link', { name: 'VA Expands Mental Health Services' });
    expect(link).toBeTruthy();
    expect(link.getAttribute('href')).toBe('/news/va-expands-mental-health-services');
  });

  it('falls back to slug when title is null', () => {
    const noTitle: TermEntryItem = {
      id: 99,
      title: null,
      slug: 'pages/untitled-entry',
      excerpt: null,
      contentTypeName: null,
      publishedAt: null,
    };
    render(
      <TermLandingPageTemplate {...baseProps} entries={[noTitle]} totalEntries={1} />,
    );
    expect(screen.getByRole('link', { name: 'pages/untitled-entry' })).toBeTruthy();
  });

  it('renders excerpt when provided', () => {
    render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    expect(
      screen.getByText('Veterans now have access to more mental health resources nationwide.'),
    ).toBeTruthy();
  });

  it('does not render card body when excerpt is null', () => {
    const noExcerpt: TermEntryItem = {
      id: 3,
      title: 'Crisis Hotline',
      slug: 'pages/crisis-hotline',
      excerpt: null,
      contentTypeName: null,
      publishedAt: null,
    };
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[noExcerpt]} totalEntries={1} />,
    );
    expect(container.querySelector('.usa-card__body')).toBeNull();
  });

  it('renders content type tag when provided', () => {
    render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    expect(screen.getByText('News Article')).toBeTruthy();
  });

  it('card list has accessible aria-label', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={sampleEntries} totalEntries={3} />,
    );
    const list = container.querySelector('ul.usa-card-group');
    expect(list!.getAttribute('aria-label')).toBe('Content tagged Mental Health');
  });
});

// ---------------------------------------------------------------------------
// Empty state
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — empty state', () => {
  it('renders empty-state alert when entries array is empty', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('.usa-alert.usa-alert--info')).not.toBeNull();
  });

  it('empty state contains a link to home page', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    const alert = container.querySelector('.usa-alert--info');
    const homeLink = alert!.querySelector('a[href="/"]');
    expect(homeLink).not.toBeNull();
    expect(homeLink!.textContent).toContain('Return to the home page');
  });

  it('does not render card list when entries is empty', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('ul.usa-card-group')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Breadcrumb
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — breadcrumb', () => {
  it('renders breadcrumb when provided', () => {
    const { container } = render(<TermLandingPageTemplate {...fullProps} />);
    expect(container.querySelector('nav.usa-breadcrumb')).not.toBeNull();
  });

  it('does not render breadcrumb when not provided', () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    expect(container.querySelector('nav.usa-breadcrumb')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Accessibility: axe-core (critical AND serious — per AC)
// ---------------------------------------------------------------------------

describe('TermLandingPageTemplate — accessibility (axe-core)', () => {
  it('passes axe-core: zero critical violations (empty state)', async () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core: zero serious violations (empty state)', async () => {
    const { container } = render(
      <TermLandingPageTemplate {...baseProps} entries={[]} totalEntries={0} />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });

  it('passes axe-core: zero critical violations (with entries)', async () => {
    const { container } = render(<TermLandingPageTemplate {...fullProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core: zero serious violations (with entries)', async () => {
    const { container } = render(<TermLandingPageTemplate {...fullProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });

  it('passes axe-core: zero critical violations (with description and breadcrumbs)', async () => {
    const { container } = render(
      <TermLandingPageTemplate
        {...baseProps}
        description="Mental health content for veterans and their families."
        entries={sampleEntries}
        totalEntries={sampleEntries.length}
        breadcrumbs={[
          { label: 'Home', href: '/' },
          { label: 'Topics', href: '/topics' },
          { label: 'Mental Health' },
        ]}
      />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});
