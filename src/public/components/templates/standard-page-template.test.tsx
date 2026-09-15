/**
 * standard-page-template.test.tsx
 *
 * Tests for:
 *  - UswdsBreadcrumb
 *  - UswdsInPageNav
 *  - StandardPageTemplate (full render with all USWDS chrome)
 *
 * Issue #58 — Standard Page public template
 * AC: Banner, Header, Breadcrumb, H1, usa-prose body, Footer, Identifier render.
 * AC: In-page navigation renders for pages with 3+ major sections.
 * AC: axe-core zero critical violations.
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { UswdsBreadcrumb } from '../uswds/UswdsBreadcrumb';
import { UswdsInPageNav } from '../uswds/UswdsInPageNav';
import { StandardPageTemplate } from './StandardPageTemplate';

expect.extend(toHaveNoViolations);

// ---------------------------------------------------------------------------
// UswdsBreadcrumb
// ---------------------------------------------------------------------------

describe('UswdsBreadcrumb', () => {
  const items = [
    { label: 'Home', href: '/' },
    { label: 'About', href: '/about' },
    { label: 'History' },
  ];

  it('renders null when items array is empty', () => {
    const { container } = render(<UswdsBreadcrumb items={[]} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders usa-breadcrumb nav', () => {
    const { container } = render(<UswdsBreadcrumb items={items} />);
    expect(container.querySelector('nav.usa-breadcrumb')).not.toBeNull();
  });

  it('renders an ordered list with correct class', () => {
    const { container } = render(<UswdsBreadcrumb items={items} />);
    expect(container.querySelector('ol.usa-breadcrumb__list')).not.toBeNull();
  });

  it('renders linked crumbs for non-final items', () => {
    render(<UswdsBreadcrumb items={items} />);
    expect(screen.getByRole('link', { name: 'Home' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'About' })).toBeTruthy();
  });

  it('renders the last item as non-linked current page', () => {
    const { container } = render(<UswdsBreadcrumb items={items} />);
    const listItems = container.querySelectorAll('.usa-breadcrumb__list-item');
    const lastItem = listItems[listItems.length - 1];
    // Last item should have aria-current="page"
    expect(lastItem.getAttribute('aria-current')).toBe('page');
    // Last item should not have an <a> link
    expect(lastItem.querySelector('a')).toBeNull();
    expect(lastItem.textContent).toContain('History');
  });

  it('applies usa-current class to the final item', () => {
    const { container } = render(<UswdsBreadcrumb items={items} />);
    const listItems = container.querySelectorAll('.usa-breadcrumb__list-item');
    const lastItem = listItems[listItems.length - 1];
    expect(lastItem.classList.contains('usa-current')).toBe(true);
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<UswdsBreadcrumb items={items} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// UswdsInPageNav
// ---------------------------------------------------------------------------

describe('UswdsInPageNav', () => {
  const twoSections = [
    { id: 'overview', text: 'Overview' },
    { id: 'eligibility', text: 'Eligibility' },
  ];

  const threeSections = [
    { id: 'overview', text: 'Overview' },
    { id: 'eligibility', text: 'Eligibility' },
    { id: 'how-to-apply', text: 'How to Apply' },
  ];

  it('returns null for fewer than 3 sections', () => {
    const { container } = render(<UswdsInPageNav sections={twoSections} />);
    expect(container.firstChild).toBeNull();
  });

  it('returns null for empty sections', () => {
    const { container } = render(<UswdsInPageNav sections={[]} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders usa-in-page-nav nav for 3+ sections', () => {
    const { container } = render(<UswdsInPageNav sections={threeSections} />);
    expect(container.querySelector('nav.usa-in-page-nav')).not.toBeNull();
  });

  it('renders heading "On this page" by default', () => {
    render(<UswdsInPageNav sections={threeSections} />);
    expect(screen.getByText('On this page')).toBeTruthy();
  });

  it('renders custom navLabel when provided', () => {
    render(<UswdsInPageNav sections={threeSections} navLabel="Page contents" />);
    expect(screen.getByText('Page contents')).toBeTruthy();
  });

  it('renders anchor links for each section', () => {
    const { container } = render(<UswdsInPageNav sections={threeSections} />);
    const links = container.querySelectorAll('a.usa-in-page-nav__link');
    expect(links).toHaveLength(3);
    expect((links[0] as HTMLAnchorElement).href).toContain('#overview');
    expect((links[1] as HTMLAnchorElement).href).toContain('#eligibility');
    expect((links[2] as HTMLAnchorElement).href).toContain('#how-to-apply');
  });

  it('has accessible aria-label on nav', () => {
    const { container } = render(<UswdsInPageNav sections={threeSections} />);
    const nav = container.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('On this page');
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<UswdsInPageNav sections={threeSections} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// StandardPageTemplate
// ---------------------------------------------------------------------------

describe('StandardPageTemplate', () => {
  const baseProps = {
    title: 'About VA History',
    renderedBody: '<p>This is the page body.</p>',
    navigation: [
      { label: 'Home', href: '/' },
      { label: 'News', href: '/news' },
    ],
  };

  const threeSection = [
    { id: 'overview', text: 'Overview' },
    { id: 'eligibility', text: 'Eligibility' },
    { id: 'apply', text: 'How to Apply' },
  ];

  it('renders the USWDS Banner', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('renders the USWDS Header', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    expect(container.querySelector('.usa-header')).not.toBeNull();
  });

  it('renders navigation items in header', () => {
    render(<StandardPageTemplate {...baseProps} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('News')).toBeTruthy();
  });

  it('renders H1 with the page title', () => {
    render(<StandardPageTemplate {...baseProps} />);
    const h1 = screen.getByRole('heading', { level: 1 });
    expect(h1.textContent).toBe('About VA History');
  });

  it('renders body content in .usa-prose article', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    const prose = container.querySelector('article.usa-prose');
    expect(prose).not.toBeNull();
    expect(prose?.innerHTML).toContain('This is the page body.');
  });

  it('renders the USWDS Footer', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders the USWDS Identifier', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('renders breadcrumb when provided', () => {
    const breadcrumbs = [
      { label: 'Home', href: '/' },
      { label: 'About VA History' },
    ];
    const { container } = render(
      <StandardPageTemplate {...baseProps} breadcrumbs={breadcrumbs} />,
    );
    const breadcrumbNav = container.querySelector('nav.usa-breadcrumb');
    expect(breadcrumbNav).not.toBeNull();
    // Use queryAllBy to handle multiple "Home" links (header nav + breadcrumb)
    const homeLinks = screen.getAllByRole('link', { name: /home/i });
    expect(homeLinks.length).toBeGreaterThanOrEqual(1);
    // At least one should be the breadcrumb link with the specific class
    const breadcrumbHomeLink = breadcrumbNav!.querySelector('a.usa-breadcrumb__link');
    expect(breadcrumbHomeLink).not.toBeNull();
  });

  it('does not render breadcrumb when not provided', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    expect(container.querySelector('nav.usa-breadcrumb')).toBeNull();
  });

  it('does not render in-page nav when fewer than 3 sections', () => {
    const { container } = render(
      <StandardPageTemplate
        {...baseProps}
        sections={[{ id: 'one', text: 'One' }, { id: 'two', text: 'Two' }]}
      />,
    );
    expect(container.querySelector('.usa-in-page-nav')).toBeNull();
  });

  it('renders in-page nav sidebar when 3+ sections provided', () => {
    const { container } = render(
      <StandardPageTemplate {...baseProps} sections={threeSection} />,
    );
    expect(container.querySelector('.usa-in-page-nav')).not.toBeNull();
    expect(container.querySelector('aside')).not.toBeNull();
  });

  it('renders main element with id=main-content', () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    expect(container.querySelector('main#main-content')).not.toBeNull();
  });

  it('passes axe-core with zero critical violations (minimal)', async () => {
    const { container } = render(<StandardPageTemplate {...baseProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });

  it('passes axe-core with zero critical violations (with breadcrumb + in-page nav)', async () => {
    const breadcrumbs = [
      { label: 'Home', href: '/' },
      { label: 'About VA History' },
    ];
    const { container } = render(
      <StandardPageTemplate
        {...baseProps}
        breadcrumbs={breadcrumbs}
        sections={threeSection}
      />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter((v) => v.impact === 'critical');
    expect(criticalViolations).toHaveLength(0);
  });
});
