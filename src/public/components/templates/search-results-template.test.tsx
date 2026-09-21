/**
 * search-results-template.test.tsx
 *
 * Tests for the SearchResultsTemplate component.
 *
 * Issue #63 — Wire axe-core Playwright a11y tests for all public templates
 * Epic #14 — Section 508 & Accessibility Hardening
 *
 * AC: Playwright + axe-core test exists for: Search Results
 * AC: Tests fail CI if any critical or serious violation found
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { SearchResultsTemplate } from './SearchResultsTemplate';
import type { SearchResultItem } from '@/lib/cms/search';

expect.extend(toHaveNoViolations);

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const baseNav = [
  { label: 'Home', href: '/' },
  { label: 'News', href: '/news' },
];

const buildHref = (page: number) => `/search?q=veteran&page=${page}`;

const emptyProps = {
  query: '',
  items: [],
  totalItems: 0,
  currentPage: 1,
  totalPages: 1,
  buildPageHref: buildHref,
  contentTypes: [],
  tags: [],
  navigation: baseNav,
};

const sampleItems: SearchResultItem[] = [
  {
    id: 1,
    title: 'VA Health Care Eligibility',
    slug: 'pages/va-health-care-eligibility',
    contentTypeId: 1,
    contentTypeName: 'Standard Page',
    excerpt: 'Learn about your eligibility for VA health care.',
    publishedAt: '2026-09-01T00:00:00Z',
    rank: 200,
  },
  {
    id: 2,
    title: 'Veteran Benefits Overview',
    slug: 'news/veteran-benefits-overview',
    contentTypeId: 2,
    contentTypeName: 'News Article',
    excerpt: 'An overview of all available veteran benefits programs.',
    publishedAt: '2026-08-15T00:00:00Z',
    rank: 150,
  },
];

const withResultsProps = {
  query: 'veteran',
  items: sampleItems,
  totalItems: 2,
  currentPage: 1,
  totalPages: 1,
  buildPageHref: buildHref,
  contentTypes: [
    { id: 1, name: 'Standard Page' },
    { id: 2, name: 'News Article' },
  ],
  tags: [
    { id: 10, name: 'Health' },
    { id: 11, name: 'Benefits' },
  ],
  navigation: baseNav,
};

const noResultsProps = {
  ...withResultsProps,
  items: [],
  totalItems: 0,
};

// ---------------------------------------------------------------------------
// Mandatory USWDS chrome
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — mandatory USWDS chrome', () => {
  it('renders the USWDS Banner (.usa-banner)', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('renders the USWDS Header (.usa-header)', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    expect(container.querySelector('.usa-header')).not.toBeNull();
  });

  it('renders navigation items in the header', () => {
    render(<SearchResultsTemplate {...emptyProps} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('News')).toBeTruthy();
  });

  it('renders the USWDS Footer (.usa-footer--big)', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders the USWDS Identifier (.usa-identifier)', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('renders main element with id=main-content', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    expect(container.querySelector('main#main-content')).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Heading and search form
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — heading and search form', () => {
  it('renders H1 "Search" when query is empty', () => {
    render(<SearchResultsTemplate {...emptyProps} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toBe('Search');
  });

  it('renders H1 with query when query is present', () => {
    render(<SearchResultsTemplate {...withResultsProps} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toContain('Search results for');
    expect(h1.textContent).toContain('veteran');
  });

  // The header also carries a form[role="search"]; the page-level one is the
  // usa-search--big form that owns #search-page-field.
  it('renders a page-level search form with role=search and USWDS markup', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    const form = container.querySelector('#search-page-field')!.closest('form');
    expect(form).not.toBeNull();
    expect(form!.getAttribute('role')).toBe('search');
    // USWDS 3 puts usa-search on the <form> itself — that is the flex row that
    // keeps the input and button on one line.
    expect(form!.classList.contains('usa-search')).toBe(true);
    expect(form!.classList.contains('usa-search--big')).toBe(true);
  });

  it('search form has accessible aria-label', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    const form = container.querySelector('#search-page-field')!.closest('form');
    expect(form!.getAttribute('aria-label')).toBe('Site search');
  });

  it('search input has associated label', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    const label = container.querySelector('label[for="search-page-field"]');
    const input = container.querySelector('#search-page-field');
    expect(label).not.toBeNull();
    expect(input).not.toBeNull();
  });

  it('search input pre-fills with current query', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    const input = container.querySelector('#search-page-field') as HTMLInputElement;
    expect(input.defaultValue).toBe('veteran');
  });
});

// ---------------------------------------------------------------------------
// Empty state (no query)
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — empty state (no query)', () => {
  it('renders a prompt to enter a search term', () => {
    render(<SearchResultsTemplate {...emptyProps} />);
    expect(screen.getByText(/Enter a search term above/)).toBeTruthy();
  });

  it('does not render filter sidebar when no query', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    // The SearchFilters form only renders when query is present
    expect(container.querySelector('aside[aria-label="Filter search results"]')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// With results
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — with results', () => {
  it('renders filter sidebar when query is present', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    expect(
      container.querySelector('aside[aria-label="Filter search results"]'),
    ).not.toBeNull();
  });

  it('renders results section when query is present', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    expect(
      container.querySelector('section[aria-label="Search results"]'),
    ).not.toBeNull();
  });

  it('renders result count in a live region', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    const status = container.querySelector('[role="status"]');
    expect(status).not.toBeNull();
    expect(status!.textContent).toContain('2');
    expect(status!.textContent).toContain('veteran');
  });

  it('renders result cards', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    const cards = container.querySelectorAll('.usa-card');
    expect(cards.length).toBe(2);
  });

  it('renders result titles as links', () => {
    render(<SearchResultsTemplate {...withResultsProps} />);
    expect(
      screen.getByRole('link', { name: 'VA Health Care Eligibility' }),
    ).toBeTruthy();
    expect(
      screen.getByRole('link', { name: 'Veteran Benefits Overview' }),
    ).toBeTruthy();
  });
});

// ---------------------------------------------------------------------------
// No results (query present but zero items)
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — no results', () => {
  it('renders zero-results status message', () => {
    const { container } = render(<SearchResultsTemplate {...noResultsProps} />);
    const status = container.querySelector('[role="status"]');
    expect(status!.textContent).toContain('No results found for');
  });

  it('renders a USWDS info alert with a suggestion link', () => {
    const { container } = render(<SearchResultsTemplate {...noResultsProps} />);
    const alert = container.querySelector('.usa-alert.usa-alert--info');
    expect(alert).not.toBeNull();
    const homeLink = alert!.querySelector('a[href="/"]');
    expect(homeLink).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Pagination
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — pagination', () => {
  it('does not render pagination when only one page', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    expect(container.querySelector('nav.usa-pagination')).toBeNull();
  });

  it('renders pagination when more than one page', () => {
    const { container } = render(
      <SearchResultsTemplate {...withResultsProps} totalPages={3} />,
    );
    expect(container.querySelector('nav.usa-pagination')).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Settings-driven behaviour (#149)
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — pageSize', () => {
  it('uses pageSize from the site setting for the "Showing x–y" count', () => {
    const { container } = render(
      <SearchResultsTemplate {...withResultsProps} currentPage={2} totalPages={2} totalItems={30} pageSize={25} />,
    );
    expect(container.querySelector('[role="status"]')!.textContent).toContain('Showing 26–30 of 30');
  });

  it('defaults pageSize to the DEFAULT_SITE_SETTINGS value (10)', () => {
    const { container } = render(
      <SearchResultsTemplate {...withResultsProps} currentPage={2} totalPages={2} totalItems={30} />,
    );
    expect(container.querySelector('[role="status"]')!.textContent).toContain('Showing 11–20 of 30');
  });
});

describe('SearchResultsTemplate — searchEnabled=false', () => {
  it('keeps the mandatory chrome and main landmark', () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} searchEnabled={false} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
    expect(container.querySelector('.usa-header')).not.toBeNull();
    expect(container.querySelector('main#main-content')).not.toBeNull();
    expect(container.querySelector('.usa-footer')).not.toBeNull();
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('renders an unavailable notice instead of the search form', () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} searchEnabled={false} />);
    expect(screen.getByText('Search is currently unavailable')).toBeTruthy();
    expect(container.querySelector('#search-page-field')).toBeNull();
    expect(container.querySelector('.usa-search--big')).toBeNull();
    expect(container.querySelector('.usa-card-group')).toBeNull();
  });

  it('passes axe-core: zero critical or serious violations', async () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} searchEnabled={false} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    expect(results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious')).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// Accessibility: axe-core (critical AND serious — per AC)
// ---------------------------------------------------------------------------

describe('SearchResultsTemplate — accessibility (axe-core)', () => {
  it('passes axe-core: zero critical violations (empty state, no query)', async () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core: zero serious violations (empty state, no query)', async () => {
    const { container } = render(<SearchResultsTemplate {...emptyProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });

  it('passes axe-core: zero critical violations (with results)', async () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core: zero serious violations (with results)', async () => {
    const { container } = render(<SearchResultsTemplate {...withResultsProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });

  it('passes axe-core: zero critical violations (no results, query present)', async () => {
    const { container } = render(<SearchResultsTemplate {...noResultsProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core: zero critical violations (with pagination)', async () => {
    const { container } = render(
      <SearchResultsTemplate {...withResultsProps} currentPage={2} totalPages={5} />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core: zero serious violations (with pagination)', async () => {
    const { container } = render(
      <SearchResultsTemplate {...withResultsProps} currentPage={2} totalPages={5} />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const seriousViolations = results.violations.filter((v) => v.impact === 'serious');
    expect(seriousViolations).toHaveLength(0);
  });
});
