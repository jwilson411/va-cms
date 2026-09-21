/**
 * USWDS component tests — issue #18
 *
 * Acceptance criteria:
 * - UswdsBanner renders official government banner markup
 * - UswdsIdentifier renders with configurable agency name and required links
 * - UswdsHeader accepts navigation prop and renders usa-header--extended
 * - UswdsFooter renders usa-footer--big
 * - All four components pass axe-core with zero critical violations
 */

import React from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe, toHaveNoViolations } from 'jest-axe';
import { UswdsBanner } from './UswdsBanner';
import { UswdsIdentifier } from './UswdsIdentifier';
import { UswdsHeader } from './UswdsHeader';
import { UswdsFooter } from './UswdsFooter';

expect.extend(toHaveNoViolations);

// ---------------------------------------------------------------------------
// UswdsBanner
// ---------------------------------------------------------------------------

describe('UswdsBanner', () => {
  it('renders official government banner section', () => {
    const { container } = render(<UswdsBanner />);
    expect(container.querySelector('.usa-banner')).not.toBeNull();
  });

  it('has role region with accessible label', () => {
    render(<UswdsBanner />);
    expect(
      screen.getByRole('region', { name: /official government website/i }),
    ).toBeTruthy();
  });

  it('renders the "official website" text', () => {
    render(<UswdsBanner />);
    expect(
      screen.getByText(/An official website of the United States government/i),
    ).toBeTruthy();
  });

  it('toggle button expands/collapses the panel', async () => {
    const user = userEvent.setup();
    const { container } = render(<UswdsBanner />);

    const button = screen.getByRole('button', { name: /here's how you know/i });
    expect(button).toBeTruthy();
    expect(button.getAttribute('aria-expanded')).toBe('false');

    const panel = container.querySelector('#gov-banner-default');
    expect(panel).not.toBeNull();
    expect((panel as HTMLElement).hidden).toBe(true);

    await user.click(button);
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect((panel as HTMLElement).hidden).toBe(false);

    await user.click(button);
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect((panel as HTMLElement).hidden).toBe(true);
  });

  it('uses class usa-accordion inside .usa-banner', () => {
    const { container } = render(<UswdsBanner />);
    expect(container.querySelector('.usa-banner .usa-accordion')).not.toBeNull();
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<UswdsBanner />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    // Filter for critical violations only
    const criticalViolations = results.violations.filter(
      (v) => v.impact === 'critical',
    );
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// UswdsIdentifier
// ---------------------------------------------------------------------------

describe('UswdsIdentifier', () => {
  const defaultProps = {
    agencyName: 'Department of Veterans Affairs',
    agencyShortName: 'VA',
    agencyHref: 'https://www.va.gov',
  };

  it('renders .usa-identifier container', () => {
    const { container } = render(<UswdsIdentifier {...defaultProps} />);
    expect(container.querySelector('.usa-identifier')).not.toBeNull();
  });

  it('displays the agency name', () => {
    render(<UswdsIdentifier {...defaultProps} />);
    expect(screen.getByText(/Department of Veterans Affairs/)).toBeTruthy();
  });

  it('renders configurable agency name in disclaimer', () => {
    const { container } = render(
      <UswdsIdentifier agencyName="Department of Defense" agencyHref="https://www.defense.gov" />,
    );
    expect(
      container.querySelector('.usa-identifier__identity-disclaimer'),
    ).not.toBeNull();
    expect(screen.getByText(/Department of Defense/)).toBeTruthy();
  });

  it('renders required government links', () => {
    render(<UswdsIdentifier {...defaultProps} />);
    expect(screen.getByText(/Accessibility statement/i)).toBeTruthy();
    expect(screen.getByText(/FOIA requests/i)).toBeTruthy();
    expect(screen.getByText(/Privacy policy/i)).toBeTruthy();
  });

  it('renders custom identifierLinks when provided', () => {
    const customLinks = [
      { href: '/custom1', text: 'Custom Link 1' },
      { href: '/custom2', text: 'Custom Link 2' },
    ];
    render(<UswdsIdentifier {...defaultProps} identifierLinks={customLinks} />);
    expect(screen.getByText('Custom Link 1')).toBeTruthy();
    expect(screen.getByText('Custom Link 2')).toBeTruthy();
  });

  it('renders agency logo when provided', () => {
    const { container } = render(
      <UswdsIdentifier
        {...defaultProps}
        agencyLogoSrc="/img/va-logo.png"
        agencyLogoAlt="VA logo"
      />,
    );
    const img = container.querySelector('.usa-identifier__logo-img');
    expect(img).not.toBeNull();
    expect((img as HTMLImageElement).alt).toBe('VA logo');
  });

  it('links to USA.gov in the usagov section', () => {
    render(<UswdsIdentifier {...defaultProps} />);
    const usaGovLink = screen.getByRole('link', { name: /Visit USA\.gov/i });
    expect(usaGovLink.getAttribute('href')).toBe('https://www.usa.gov');
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(<UswdsIdentifier {...defaultProps} />);
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter(
      (v) => v.impact === 'critical',
    );
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// UswdsHeader
// ---------------------------------------------------------------------------

describe('UswdsHeader', () => {
  const navItems = [
    { label: 'Home', href: '/' },
    { label: 'About', href: '/about' },
    {
      label: 'Services',
      href: '/services',
      children: [
        { label: 'Health Care', href: '/services/health' },
        { label: 'Benefits', href: '/services/benefits' },
      ],
    },
  ];

  it('renders usa-header--extended', () => {
    const { container } = render(
      <UswdsHeader siteTitle="VA CMS" navigation={navItems} />,
    );
    expect(container.querySelector('.usa-header.usa-header--extended')).not.toBeNull();
  });

  it('renders site title in masthead', () => {
    render(<UswdsHeader siteTitle="Department of Veterans Affairs" navigation={[]} />);
    expect(screen.getByText('Department of Veterans Affairs')).toBeTruthy();
  });

  it('renders top-level nav links', () => {
    render(<UswdsHeader siteTitle="VA CMS" navigation={navItems} />);
    expect(screen.getByText('Home')).toBeTruthy();
    expect(screen.getByText('About')).toBeTruthy();
  });

  it('renders navigation as usa-nav with primary navigation label', () => {
    const { container } = render(
      <UswdsHeader siteTitle="VA CMS" navigation={navItems} />,
    );
    const nav = container.querySelector('nav[aria-label="Primary navigation"]');
    expect(nav).not.toBeNull();
    expect(nav?.classList.contains('usa-nav')).toBe(true);
  });

  it('renders dropdown button for items with children', () => {
    render(<UswdsHeader siteTitle="VA CMS" navigation={navItems} />);
    const dropdownBtn = screen.getByRole('button', { name: /Services/i });
    expect(dropdownBtn).toBeTruthy();
    expect(dropdownBtn.getAttribute('aria-expanded')).toBe('false');
  });

  it('expands dropdown when button is clicked', async () => {
    const user = userEvent.setup();
    render(<UswdsHeader siteTitle="VA CMS" navigation={navItems} />);
    const dropdownBtn = screen.getByRole('button', { name: /Services/i });
    await user.click(dropdownBtn);
    expect(dropdownBtn.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByText('Health Care')).toBeTruthy();
    expect(screen.getByText('Benefits')).toBeTruthy();
  });

  it('renders usa-accordion class on primary nav', () => {
    const { container } = render(
      <UswdsHeader siteTitle="VA CMS" navigation={navItems} />,
    );
    expect(
      container.querySelector('.usa-nav__primary.usa-accordion'),
    ).not.toBeNull();
  });

  it('renders accessible search form', () => {
    render(<UswdsHeader siteTitle="VA CMS" navigation={navItems} />);
    // label is sr-only but still in the DOM
    expect(screen.getByLabelText(/Search/i)).toBeTruthy();
  });

  it('puts usa-search and role=search on the <form> (USWDS 3 flex row)', () => {
    const { container } = render(<UswdsHeader siteTitle="VA CMS" navigation={navItems} />);
    // The flex rule is `.usa-search[role="search"]`; if the class and role sit on a
    // wrapper div instead, the input and submit button stack on two lines.
    const form = container.querySelector('form.usa-search.usa-search--small[role="search"]');
    expect(form).not.toBeNull();
    expect(form!.querySelector('input[type="search"]')).not.toBeNull();
    expect(form!.querySelector('button[type="submit"]')).not.toBeNull();
  });

  it('wraps search in .usa-nav__secondary (canonical extended-header markup)', () => {
    const { container } = render(<UswdsHeader siteTitle="VA CMS" navigation={navItems} />);
    // .usa-nav__secondary is what the framework CSS absolutely-positions into the
    // header row alongside the primary nav; a bare form sibling of usa-nav__primary
    // stacks as a full-width block and adds vertical whitespace above the nav.
    const secondary = container.querySelector('.usa-nav__secondary');
    expect(secondary).not.toBeNull();
    expect(secondary!.querySelector('form.usa-search')).not.toBeNull();
    expect(secondary!.nextElementSibling).toBe(container.querySelector('.usa-nav__primary'));
  });

  it('omits the search wrapper entirely when showSearch is false', () => {
    const { container } = render(
      <UswdsHeader siteTitle="VA CMS" navigation={navItems} showSearch={false} />,
    );
    expect(container.querySelector('.usa-nav__secondary')).toBeNull();
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(
      <UswdsHeader siteTitle="VA CMS" navigation={navItems} />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter(
      (v) => v.impact === 'critical',
    );
    expect(criticalViolations).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// UswdsFooter
// ---------------------------------------------------------------------------

describe('UswdsFooter', () => {
  const navColumns = [
    {
      header: 'Health Care',
      links: [
        { href: '/health/apply', text: 'Apply for Benefits' },
        { href: '/health/find', text: 'Find a VA Location' },
      ],
    },
    {
      header: 'About VA',
      links: [
        { href: '/about/history', text: 'History' },
        { href: '/about/leadership', text: 'Leadership' },
      ],
    },
  ];

  it('renders usa-footer--big', () => {
    const { container } = render(
      <UswdsFooter agencyName="Department of Veterans Affairs" />,
    );
    expect(container.querySelector('.usa-footer.usa-footer--big')).not.toBeNull();
  });

  it('renders agency name in the secondary section', () => {
    render(<UswdsFooter agencyName="Department of Veterans Affairs" />);
    expect(screen.getByText('Department of Veterans Affairs')).toBeTruthy();
  });

  it('renders nav columns with headers and links', () => {
    render(
      <UswdsFooter
        agencyName="Department of Veterans Affairs"
        navColumns={navColumns}
      />,
    );
    expect(screen.getByText('Health Care')).toBeTruthy();
    expect(screen.getByText('Apply for Benefits')).toBeTruthy();
    expect(screen.getByText('About VA')).toBeTruthy();
    expect(screen.getByText('History')).toBeTruthy();
  });

  it('renders return-to-top link', () => {
    render(<UswdsFooter agencyName="VA" />);
    expect(screen.getByText('Return to top')).toBeTruthy();
  });

  it('renders agency logo when provided', () => {
    const { container } = render(
      <UswdsFooter
        agencyName="VA"
        agencyLogoSrc="/img/va-logo.png"
        agencyLogoAlt="VA Logo"
      />,
    );
    const img = container.querySelector('.usa-footer__logo-img');
    expect(img).not.toBeNull();
    expect((img as HTMLImageElement).alt).toBe('VA Logo');
  });

  it('renders optional contactInfo slot', () => {
    render(
      <UswdsFooter
        agencyName="VA"
        contactInfo={<p>Contact us at 1-800-555-0000</p>}
      />,
    );
    expect(screen.getByText(/Contact us at/)).toBeTruthy();
  });

  it('has role=contentinfo on footer element', () => {
    const { container } = render(<UswdsFooter agencyName="VA" />);
    const footer = container.querySelector('footer');
    expect(footer?.getAttribute('role')).toBe('contentinfo');
  });

  it('passes axe-core with zero critical violations', async () => {
    const { container } = render(
      <UswdsFooter
        agencyName="Department of Veterans Affairs"
        navColumns={navColumns}
      />,
    );
    const results = await axe(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'best-practice'] },
    });
    const criticalViolations = results.violations.filter(
      (v) => v.impact === 'critical',
    );
    expect(criticalViolations).toHaveLength(0);
  });
});
