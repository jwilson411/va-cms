/**
 * ThemePage unit tests (issue #17).
 *
 * Acceptance criteria verified:
 *   - A test page renders USWDS components (usa-button, usa-alert) that the
 *     compiled VA theme styles.
 */

import { render, screen } from '@testing-library/react';
import { ThemePage } from './ThemePage';

describe('ThemePage', () => {
  it('renders USWDS button variants', () => {
    render(<ThemePage />);
    const primary = screen.getByRole('button', { name: 'Primary (VA Blue)' });
    expect(primary).toHaveClass('usa-button');
    expect(screen.getByRole('button', { name: 'Accent warm (VA Gold)' })).toHaveClass(
      'usa-button--accent-warm',
    );
  });

  it('renders a USWDS alert and the VA colour swatches', () => {
    render(<ThemePage />);
    expect(screen.getByRole('region', { name: 'Information' })).toHaveClass('usa-alert');
    const swatches = screen.getByTestId('theme-swatches');
    expect(swatches).toHaveTextContent('#003e73');
    expect(swatches).toHaveTextContent('#f9c642');
  });
});
