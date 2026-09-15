/**
 * news-article-template.test.tsx
 *
 * Tests for the NewsArticleTemplate component.
 *
 * Issue #59 — Build News Article public template
 * AC: Template renders: Banner, Header, Breadcrumb, article header (title,
 *     author, date, featured image), usa-prose body, tags, Footer, Identifier.
 * AC: Featured image has alt text from media asset.
 * AC: Structured data (JSON-LD Article) in page <head>.
 * AC: axe-core zero critical violations.
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { NewsArticleTemplate } from './NewsArticleTemplate';

expect.extend(toHaveNoViolations);

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const baseProps = {
  title: 'VA Expands Mental Health Services Nationwide',
  renderedBody: '<p>Veterans across the country will now have access to expanded services.</p>',
  navigation: [
    { label: 'Home', href: '/' },
    { label: 'News', href: '/news' },
  ],
};

const fullProps = {
  ...baseProps,
  author: 'Jane Smith',
  publishedAt: '2026-09-15T00:00:00Z',
  featuredImage: {
    storageUrl: '/media/hero.jpg',
    altText: 'A VA medical facility exterior on a sunny day',
    width: 1200,
    height: 630,
  },
  tags: [
    { slug: 'mental-health', name: 'Mental Health' },
    { slug: 'veterans-benefits', name: 'Veterans Benefits' },
  ],
  breadcrumbs: [
    { label: 'Home', href: '/' },
    { label: 'News', href: '/news' },
    { label: 'VA Expands Mental Health Services Nationwide' },
  ],
  canonicalUrl: 'https://www.va.gov/news/va-expands-mental-health-services-nationwide',
};

// ---------------------------------------------------------------------------
// Structure: mandatory USWDS chrome
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — mandatory USWDS chrome', () => {
  it('renders the USWDS Banner (.usa-banner)', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('renders the USWDS Header (.usa-header)', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('.usa-header')).not.toBeNull();
  });

  it('renders navigation items in the header', () => {
    render(<NewsArticleTemplate {...baseProps} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('News')).toBeTruthy();
  });

  it('renders the USWDS Footer (.usa-footer--big)', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders the USWDS Identifier (.usa-identifier)', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('renders main element with id=main-content', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('main#main-content')).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Article header: title
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — article title', () => {
  it('renders H1 with the article title', () => {
    render(<NewsArticleTemplate {...baseProps} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toBe('VA Expands Mental Health Services Nationwide');
  });

  it('renders title inside an <article> element', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const article = container.querySelector('article');
    expect(article).not.toBeNull();
    expect(article!.querySelector('h1')!.textContent).toBe(
      'VA Expands Mental Health Services Nationwide',
    );
  });
});

// ---------------------------------------------------------------------------
// Article header: author and date
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — author and date', () => {
  it('renders the author name when provided', () => {
    render(<NewsArticleTemplate {...baseProps} author="Jane Smith" />);
    expect(screen.getByText('Jane Smith')).toBeTruthy();
  });

  it('does not render byline when author and publishedAt are absent', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('.usa-article-byline')).toBeNull();
  });

  it('renders the publishedAt in a <time> element with dateTime attr', () => {
    const { container } = render(
      <NewsArticleTemplate {...baseProps} publishedAt="2026-09-15T00:00:00Z" />,
    );
    const timeEl = container.querySelector('time.usa-article-byline__date');
    expect(timeEl).not.toBeNull();
    expect(timeEl!.getAttribute('dateTime')).toBe('2026-09-15T00:00:00Z');
  });

  it('renders a human-readable formatted date in the <time> element', () => {
    const { container } = render(
      <NewsArticleTemplate {...baseProps} publishedAt="2026-09-15T00:00:00Z" />,
    );
    const timeEl = container.querySelector('time');
    expect(timeEl!.textContent).toContain('September');
    expect(timeEl!.textContent).toContain('2026');
  });

  it('renders byline section when only author is provided', () => {
    const { container } = render(
      <NewsArticleTemplate {...baseProps} author="Jane Smith" />,
    );
    expect(container.querySelector('.usa-article-byline')).not.toBeNull();
    expect(container.querySelector('time')).toBeNull();
  });

  it('renders byline section when only publishedAt is provided', () => {
    const { container } = render(
      <NewsArticleTemplate {...baseProps} publishedAt="2026-09-15T00:00:00Z" />,
    );
    expect(container.querySelector('.usa-article-byline')).not.toBeNull();
    expect(screen.queryByText(/By /)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Article header: featured image with alt text
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — featured image', () => {
  it('renders the featured image when provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const img = container.querySelector('img.usa-article-figure__image');
    expect(img).not.toBeNull();
  });

  it('renders the image src from storageUrl', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const img = container.querySelector('img.usa-article-figure__image') as HTMLImageElement | null;
    expect(img!.getAttribute('src')).toBe('/media/hero.jpg');
  });

  it('renders the image alt text from the media asset', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const img = container.querySelector('img.usa-article-figure__image') as HTMLImageElement | null;
    expect(img!.getAttribute('alt')).toBe(
      'A VA medical facility exterior on a sunny day',
    );
  });

  it('does not render a figure when featuredImage is null', () => {
    const { container } = render(
      <NewsArticleTemplate {...baseProps} featuredImage={null} />,
    );
    expect(container.querySelector('figure')).toBeNull();
  });

  it('does not render a figure when featuredImage is undefined', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('figure')).toBeNull();
  });

  it('renders width and height attributes when provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const img = container.querySelector('img.usa-article-figure__image') as HTMLImageElement | null;
    expect(img!.getAttribute('width')).toBe('1200');
    expect(img!.getAttribute('height')).toBe('630');
  });
});

// ---------------------------------------------------------------------------
// Article body: usa-prose
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — article body', () => {
  it('renders the body content in a .usa-prose element', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const prose = container.querySelector('.usa-prose');
    expect(prose).not.toBeNull();
    expect(prose!.innerHTML).toContain(
      'Veterans across the country will now have access to expanded services.',
    );
  });

  it('body is rendered inside the <article> element', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const article = container.querySelector('article');
    expect(article!.querySelector('.usa-prose')).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Tags
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — tags', () => {
  it('renders taxonomy tags as usa-tag elements', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const tags = container.querySelectorAll('.usa-tag');
    expect(tags.length).toBe(2);
  });

  it('renders tag text for each term', () => {
    render(<NewsArticleTemplate {...fullProps} />);
    expect(screen.getByText('Mental Health')).toBeTruthy();
    expect(screen.getByText('Veterans Benefits')).toBeTruthy();
  });

  it('renders tag links pointing to the topic slug', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const tagLinks = container.querySelectorAll('.usa-tag');
    const hrefs = Array.from(tagLinks).map((a) => (a as HTMLAnchorElement).href);
    expect(hrefs.some((h) => h.includes('/topics/mental-health'))).toBe(true);
    expect(hrefs.some((h) => h.includes('/topics/veterans-benefits'))).toBe(true);
  });

  it('does not render a tags footer when tags array is empty', () => {
    const { container } = render(
      <NewsArticleTemplate {...baseProps} tags={[]} />,
    );
    expect(container.querySelector('.usa-article-tags')).toBeNull();
  });

  it('does not render a tags footer when tags prop is omitted', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('.usa-article-tags')).toBeNull();
  });

  it('tags list has accessible aria-label', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const ul = container.querySelector('.usa-article-tags');
    expect(ul!.getAttribute('aria-label')).toBe('Article tags');
  });
});

// ---------------------------------------------------------------------------
// Breadcrumb
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — breadcrumb', () => {
  it('renders breadcrumb when provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    expect(container.querySelector('nav.usa-breadcrumb')).not.toBeNull();
  });

  it('does not render breadcrumb when not provided', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    expect(container.querySelector('nav.usa-breadcrumb')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// JSON-LD structured data
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — JSON-LD structured data', () => {
  it('renders a <script type="application/ld+json"> element', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const script = container.querySelector('script[type="application/ld+json"]');
    expect(script).not.toBeNull();
  });

  it('JSON-LD contains @type NewsArticle', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json['@type']).toBe('NewsArticle');
  });

  it('JSON-LD contains the article headline', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.headline).toBe('VA Expands Mental Health Services Nationwide');
  });

  it('JSON-LD contains author name when provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.author?.name).toBe('Jane Smith');
  });

  it('JSON-LD contains datePublished when publishedAt is provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.datePublished).toBe('2026-09-15T00:00:00Z');
  });

  it('JSON-LD contains image URL when featuredImage is provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.image).toBe('/media/hero.jpg');
  });

  it('JSON-LD contains canonical URL when provided', () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.url).toBe(
      'https://www.va.gov/news/va-expands-mental-health-services-nationwide',
    );
  });

  it('JSON-LD contains VA publisher org', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.publisher?.name).toBe('Department of Veterans Affairs');
  });

  it('JSON-LD does not include author when omitted', () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const script = container.querySelector('script[type="application/ld+json"]')!;
    const json = JSON.parse(script.innerHTML);
    expect(json.author).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Accessibility: axe-core
// ---------------------------------------------------------------------------

describe('NewsArticleTemplate — accessibility', () => {
  it('passes axe-core with zero critical violations (minimal props)', async () => {
    const { container } = render(<NewsArticleTemplate {...baseProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core with zero critical violations (full props)', async () => {
    const { container } = render(<NewsArticleTemplate {...fullProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});
