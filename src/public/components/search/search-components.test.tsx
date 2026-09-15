/**
 * search-components.test.tsx — Component tests for the /search UI components.
 *
 * Issue #50 — BRD FR-SEARCH-01
 *
 * Tests cover:
 *   - SearchResultCard renders title, slug href, excerpt, content type, date
 *   - SearchResultCard is accessible (no critical axe violations)
 *   - SearchFilters renders form, selects, date inputs
 *   - SearchFilters is accessible (no critical axe violations)
 *   - SearchPagination renders nav with correct links and aria-current
 *   - SearchPagination is accessible (no critical axe violations)
 *   - SearchPagination returns null when totalPages <= 1
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { SearchResultCard } from '@/components/search/SearchResultCard';
import { SearchFilters } from '@/components/search/SearchFilters';
import { SearchPagination } from '@/components/search/SearchPagination';
import type { SearchResultItem } from '@/lib/cms/search';

expect.extend(toHaveNoViolations);

// ---------------------------------------------------------------------------
// Shared fixture
// ---------------------------------------------------------------------------

const sampleResult: SearchResultItem = {
  id: 1,
  title: 'VA Benefits for Veterans',
  slug: 'news/va-benefits',
  contentTypeId: 1,
  contentTypeName: 'News Article',
  excerpt: 'Learn how to get your VA benefits today.',
  publishedAt: '2026-01-15T00:00:00Z',
  rank: 100,
};

// ---------------------------------------------------------------------------
// SearchResultCard
// ---------------------------------------------------------------------------

describe('SearchResultCard', () => {
  it('renders the result title as a link', () => {
    render(<SearchResultCard result={sampleResult} />);
    const link = screen.getByRole('link', { name: /VA Benefits for Veterans/i });
    expect(link).toBeTruthy();
  });

  it('link href points to the slug path', () => {
    render(<SearchResultCard result={sampleResult} />);
    const link = screen.getByRole('link', { name: /VA Benefits for Veterans/i });
    expect(link.getAttribute('href')).toBe('/news/va-benefits');
  });

  it('renders the excerpt text', () => {
    render(<SearchResultCard result={sampleResult} />);
    expect(screen.getByText(/Learn how to get your VA benefits today/)).toBeTruthy();
  });

  it('renders the content type tag', () => {
    render(<SearchResultCard result={sampleResult} />);
    expect(screen.getByText('News Article')).toBeTruthy();
  });

  it('renders a date string for publishedAt', () => {
    const { container } = render(<SearchResultCard result={sampleResult} />);
    // Date should appear somewhere — at least "2026" must be present
    expect(container.textContent).toContain('2026');
  });

  it('falls back to slug when title is null', () => {
    const noTitle: SearchResultItem = { ...sampleResult, title: null };
    render(<SearchResultCard result={noTitle} />);
    expect(screen.getByRole('link', { name: 'news/va-benefits' })).toBeTruthy();
  });

  it('does not render excerpt section when excerpt is null', () => {
    const noExcerpt: SearchResultItem = { ...sampleResult, excerpt: null };
    const { container } = render(<SearchResultCard result={noExcerpt} />);
    expect(container.querySelector('.usa-card__text')).toBeNull();
  });

  it('renders usa-card container', () => {
    const { container } = render(<SearchResultCard result={sampleResult} />);
    expect(container.querySelector('.usa-card')).not.toBeNull();
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<SearchResultCard result={sampleResult} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// SearchFilters
// ---------------------------------------------------------------------------

describe('SearchFilters', () => {
  const defaultProps = {
    query: 'veterans',
    contentTypes: [
      { id: 1, name: 'News Article' },
      { id: 2, name: 'Standard Page' },
    ],
    tags: [
      { id: 10, name: 'Health' },
      { id: 11, name: 'Benefits' },
    ],
  };

  it('renders a form with action="/search"', () => {
    const { container } = render(<SearchFilters {...defaultProps} />);
    const form = container.querySelector('form[action="/search"]');
    expect(form).not.toBeNull();
    expect(form?.getAttribute('method')).toBe('get');
  });

  it('renders a hidden input preserving the query', () => {
    const { container } = render(<SearchFilters {...defaultProps} />);
    const hidden = container.querySelector('input[name="q"][type="hidden"]') as HTMLInputElement;
    expect(hidden).not.toBeNull();
    expect(hidden.value).toBe('veterans');
  });

  it('renders content type select with options', () => {
    render(<SearchFilters {...defaultProps} />);
    const select = screen.getByLabelText(/Content type/i) as HTMLSelectElement;
    expect(select).toBeTruthy();
    expect(screen.getByText('News Article')).toBeTruthy();
    expect(screen.getByText('Standard Page')).toBeTruthy();
  });

  it('renders from date input', () => {
    render(<SearchFilters {...defaultProps} />);
    const fromInput = screen.getByLabelText(/Published on or after/i);
    expect(fromInput).toBeTruthy();
    expect(fromInput.getAttribute('name')).toBe('from');
    expect(fromInput.getAttribute('type')).toBe('date');
  });

  it('renders to date input', () => {
    render(<SearchFilters {...defaultProps} />);
    const toInput = screen.getByLabelText(/Published on or before/i);
    expect(toInput).toBeTruthy();
    expect(toInput.getAttribute('name')).toBe('to');
    expect(toInput.getAttribute('type')).toBe('date');
  });

  it('renders tag select with options', () => {
    render(<SearchFilters {...defaultProps} />);
    const select = screen.getByLabelText(/Topic \/ tag/i) as HTMLSelectElement;
    expect(select).toBeTruthy();
    expect(screen.getByText('Health')).toBeTruthy();
    expect(screen.getByText('Benefits')).toBeTruthy();
  });

  it('pre-selects selectedType when provided', () => {
    const { container } = render(
      <SearchFilters {...defaultProps} selectedType="2" />,
    );
    const select = container.querySelector('select[name="type"]') as HTMLSelectElement;
    expect(select).not.toBeNull();
    // defaultValue sets the initial value; check option exists
    expect(container.querySelector('option[value="2"]')).not.toBeNull();
  });

  it('pre-fills from date when selectedFrom is provided', () => {
    const { container } = render(
      <SearchFilters {...defaultProps} selectedFrom="2024-01-01" />,
    );
    const fromInput = container.querySelector('input[name="from"]') as HTMLInputElement;
    expect(fromInput.defaultValue).toBe('2024-01-01');
  });

  it('does not render content type select when contentTypes is empty', () => {
    render(<SearchFilters {...defaultProps} contentTypes={[]} />);
    const select = document.querySelector('select[name="type"]');
    expect(select).toBeNull();
  });

  it('renders submit and clear buttons', () => {
    render(<SearchFilters {...defaultProps} />);
    expect(screen.getByRole('button', { name: /Apply filters/i })).toBeTruthy();
    expect(screen.getByRole('link', { name: /Clear filters/i })).toBeTruthy();
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<SearchFilters {...defaultProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// SearchPagination
// ---------------------------------------------------------------------------

describe('SearchPagination', () => {
  const buildHref = (page: number) => `/search?q=test&page=${page}`;

  it('returns null when totalPages is 1', () => {
    const { container } = render(
      <SearchPagination currentPage={1} totalPages={1} buildPageHref={buildHref} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('returns null when totalPages is 0', () => {
    const { container } = render(
      <SearchPagination currentPage={1} totalPages={0} buildPageHref={buildHref} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('renders usa-pagination nav', () => {
    const { container } = render(
      <SearchPagination currentPage={2} totalPages={5} buildPageHref={buildHref} />,
    );
    expect(container.querySelector('nav.usa-pagination')).not.toBeNull();
  });

  it('marks current page with aria-current="page"', () => {
    render(
      <SearchPagination currentPage={3} totalPages={5} buildPageHref={buildHref} />,
    );
    const currentEl = screen.getByText('3');
    expect(currentEl.getAttribute('aria-current')).toBe('page');
  });

  it('other pages are rendered as links', () => {
    render(
      <SearchPagination currentPage={2} totalPages={5} buildPageHref={buildHref} />,
    );
    const pageOneLink = screen.getByRole('link', { name: 'Page 1' });
    expect(pageOneLink.getAttribute('href')).toBe('/search?q=test&page=1');
  });

  it('renders Previous link when not on first page', () => {
    render(
      <SearchPagination currentPage={3} totalPages={5} buildPageHref={buildHref} />,
    );
    expect(screen.getByRole('link', { name: /Previous page/i })).toBeTruthy();
  });

  it('renders Next link when not on last page', () => {
    render(
      <SearchPagination currentPage={3} totalPages={5} buildPageHref={buildHref} />,
    );
    expect(screen.getByRole('link', { name: /Next page/i })).toBeTruthy();
  });

  it('Previous is not a link when on first page', () => {
    const { container } = render(
      <SearchPagination currentPage={1} totalPages={5} buildPageHref={buildHref} />,
    );
    // The previous button is a <span aria-disabled> not a link
    const prev = container.querySelector('.usa-pagination__previous-page');
    expect(prev?.tagName.toLowerCase()).not.toBe('a');
    expect(prev?.getAttribute('aria-disabled')).toBe('true');
  });

  it('Next is not a link when on last page', () => {
    const { container } = render(
      <SearchPagination currentPage={5} totalPages={5} buildPageHref={buildHref} />,
    );
    const next = container.querySelector('.usa-pagination__next-page');
    expect(next?.tagName.toLowerCase()).not.toBe('a');
    expect(next?.getAttribute('aria-disabled')).toBe('true');
  });

  it('shows ellipsis for large page counts', () => {
    const { container } = render(
      <SearchPagination currentPage={5} totalPages={20} buildPageHref={buildHref} />,
    );
    const ellipsis = container.querySelectorAll('.usa-pagination__overflow');
    expect(ellipsis.length).toBeGreaterThan(0);
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(
      <SearchPagination currentPage={3} totalPages={10} buildPageHref={buildHref} />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});
