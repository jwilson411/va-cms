/**
 * #172: the /theme component gallery is a development-only route.
 */
import React from 'react';
import { render } from '@testing-library/react';
import { vi } from 'vitest';

const notFound = vi.fn(() => {
  throw new Error('NEXT_NOT_FOUND');
});
vi.mock('next/navigation', () => ({ notFound: () => notFound() }));
vi.mock('@/components/templates/ThemeCheckTemplate', () => ({
  ThemeCheckTemplate: () => <div data-testid="theme-check" />,
}));

import ThemePage from './page';

describe('/theme page', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    notFound.mockClear();
  });

  it('renders the theme check in development', () => {
    vi.stubEnv('NODE_ENV', 'development');
    const { getByTestId } = render(<ThemePage />);
    expect(getByTestId('theme-check')).toBeInTheDocument();
    expect(notFound).not.toHaveBeenCalled();
  });

  it('answers 404 in any other environment', () => {
    vi.stubEnv('NODE_ENV', 'production');
    expect(() => render(<ThemePage />)).toThrow('NEXT_NOT_FOUND');
    expect(notFound).toHaveBeenCalled();
  });
});
