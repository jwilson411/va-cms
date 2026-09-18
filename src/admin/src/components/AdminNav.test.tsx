/**
 * AdminNav tests — keyboard accessibility audit (issue #64).
 *
 * Covers:
 *   - Skip-nav link is present and points to #main-content
 *   - nav has aria-label="Admin navigation"
 *   - All expected nav items are rendered as links
 *   - Active route gets aria-current="page" (NavLink default)
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect, vi } from 'vitest';
import { AdminNav, SkipNav } from './AdminNav';

// Site settings (epic #141): render with the code defaults, no QueryClient needed.
import * as siteSettings from '../features/siteSettings/useClientSettings';

// Auth (#175): role-gated items read the signed-in user's roles; default to a SystemAdmin.
const authState = { roles: ['SystemAdmin'] as string[] };
vi.mock('../context/AuthContext', () => ({
  useAuth: () => authState,
}));

vi.mock('../features/siteSettings/useClientSettings', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/siteSettings/useClientSettings')>();
  return {
    ...actual,
    useClientSettings: vi.fn(() => actual.buildClientSettings({}, { isLoading: false, isError: false })),
  };
});


function renderNav(initialPath = '/admin') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <SkipNav />
      <AdminNav />
    </MemoryRouter>,
  );
}

describe('AdminNav', () => {
  it('renders a skip-navigation link targeting #main-content', () => {
    renderNav();
    const skip = screen.getByTestId('skip-nav-link');
    expect(skip).toBeInTheDocument();
    expect(skip).toHaveAttribute('href', '#main-content');
    expect(skip.textContent).toBe('Skip to main content');
  });

  it('renders a <nav> with aria-label="Admin navigation"', () => {
    renderNav();
    const nav = screen.getByRole('navigation', { name: 'Admin navigation' });
    expect(nav).toBeInTheDocument();
  });

  it('renders all expected nav links', () => {
    renderNav();
    const expectedLabels = [
      'Dashboard',
      'Content entries',
      'Media library',
      'Content Types',
      'User directory',
      'Audit Log',
      'Search Analytics',
    ];
    // Some links use aria-label; getByRole('link', { name }) checks both text and aria-label.
    expectedLabels.forEach((label) => {
      expect(screen.getByRole('link', { name: label })).toBeInTheDocument();
    });
  });

  it('marks the active route with aria-current="page"', () => {
    renderNav('/admin/content');
    // NavLink sets aria-current="page" on the active link
    const contentLink = screen.getByRole('link', { name: 'Content entries' });
    expect(contentLink).toHaveAttribute('aria-current', 'page');
  });

  it('does not mark non-active links with aria-current', () => {
    renderNav('/admin/content');
    const mediaLink = screen.getByRole('link', { name: 'Media library' });
    expect(mediaLink).not.toHaveAttribute('aria-current');
  });

  it('hides the Search Analytics link while features.searchAnalytics is off (epic #141)', () => {
    vi.mocked(siteSettings.useClientSettings).mockReturnValueOnce(
      siteSettings.buildClientSettings({ 'features.searchAnalytics': 'false' }, { isLoading: false, isError: false }),
    );
    render(
      <MemoryRouter>
        <AdminNav />
      </MemoryRouter>,
    );
    expect(screen.queryByRole('link', { name: /search analytics/i })).toBeNull();
    expect(screen.getByRole('link', { name: /audit log/i })).toBeDefined();
  });

  it('hides the Search Analytics link for roles the API refuses (#175)', () => {
    authState.roles = ['Editor'];
    try {
      renderNav();
      expect(screen.queryByRole('link', { name: /search analytics/i })).toBeNull();
      expect(screen.getByRole('link', { name: /audit log/i })).toBeDefined();
    } finally {
      authState.roles = ['SystemAdmin'];
    }
  });

  it('shows the Search Analytics link to a SiteAdmin (#175)', () => {
    authState.roles = ['SiteAdmin'];
    try {
      renderNav();
      expect(screen.getByRole('link', { name: /search analytics/i })).toBeDefined();
    } finally {
      authState.roles = ['SystemAdmin'];
    }
  });
});
