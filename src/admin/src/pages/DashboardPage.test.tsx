/**
 * DashboardPage — the search analytics card is role-gated (issue #175).
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { DashboardPage } from './DashboardPage';

const authState = { roles: [] as string[] };
vi.mock('../context/AuthContext', () => ({
  useAuth: () => authState,
}));

vi.mock('../features/searchAnalytics', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/searchAnalytics')>();
  return {
    ...actual,
    SearchAnalyticsWidget: () => <div data-testid="search-analytics-widget" />,
  };
});

describe('DashboardPage', () => {
  it('shows the search analytics card to a SiteAdmin', () => {
    authState.roles = ['SiteAdmin'];
    render(<DashboardPage />);
    expect(screen.getByRole('heading', { name: 'Search Analytics' })).toBeInTheDocument();
    expect(screen.getByTestId('search-analytics-widget')).toBeInTheDocument();
  });

  it('shows the search analytics card to a SystemAdmin', () => {
    authState.roles = ['ContentOwner', 'SystemAdmin'];
    render(<DashboardPage />);
    expect(screen.getByTestId('search-analytics-widget')).toBeInTheDocument();
  });

  it('omits the card for an Editor instead of rendering one that 403s', () => {
    authState.roles = ['Editor'];
    render(<DashboardPage />);
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Search Analytics' })).toBeNull();
    expect(screen.queryByTestId('search-analytics-widget')).toBeNull();
  });
});
