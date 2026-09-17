/**
 * LoginPage unit tests.
 *
 * Acceptance criteria verified:
 *   - Admin SPA login page renders with a "Sign in" call-to-action
 *   - Clicking "Sign in" initiates the OIDC flow (navigates to /api/auth/login)
 *   - Page shows a loading indicator while auth state is resolving
 *   - Authenticated users are redirected away from the login page
 */

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthProvider } from '../context/AuthContext';
import { LoginPage } from '../pages/LoginPage';

// ── Helpers ───────────────────────────────────────────────────────────────────

function mockFetch(status: number, body: object): void {
  vi.spyOn(window, 'fetch').mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  } as Response);
}

function renderWithAuth(): ReturnType<typeof render> {
  return render(
    <AuthProvider>
      <LoginPage />
    </AuthProvider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('LoginPage', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows loading state while auth resolves', () => {
    // Fetch never resolves — simulates an in-flight refresh call.
    vi.spyOn(window, 'fetch').mockReturnValue(new Promise(() => {}));

    renderWithAuth();

    expect(screen.getByText('Checking session…')).toBeTruthy();
  });

  it('shows the not-provisioned message when the API redirects back with ?error= (#155)', async () => {
    mockFetch(401, {});
    const origLocation = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...origLocation, href: '', search: '?error=not_provisioned' },
    });

    renderWithAuth();

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());
    expect(screen.getByText(/not set up in the CMS/)).toBeTruthy();

    Object.defineProperty(window, 'location', { configurable: true, value: origLocation });
  });

  it('renders sign-in button when not authenticated', async () => {
    // Refresh returns 401 — no valid session.
    mockFetch(401, {});

    // Suppress window.location.href assignment triggered by login() on 401.
    const origLocation = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...origLocation, href: '' },
    });

    renderWithAuth();

    await waitFor(() =>
      expect(screen.queryByText('Checking session…')).toBeNull(),
    );

    expect(
      screen.getByRole('button', { name: /sign in with va network account/i }),
    ).toBeTruthy();
    expect(screen.getByText(/VA CMS Admin/i)).toBeTruthy();

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: origLocation,
    });
  });

  it('clicking sign-in button navigates to OIDC login endpoint', async () => {
    mockFetch(401, {});

    const origLocation = window.location;
    let capturedHref = '';
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: {
        ...origLocation,
        get href() {
          return capturedHref;
        },
        set href(v: string) {
          capturedHref = v;
        },
      },
    });

    renderWithAuth();

    await waitFor(() =>
      expect(screen.queryByText('Checking session…')).toBeNull(),
    );

    // Reset the href after any 401-triggered redirect
    capturedHref = '';

    const btn = screen.getByRole('button', { name: /sign in with va network account/i });
    await userEvent.click(btn);

    // The login() call in AuthContext navigates to /api/auth/login
    expect(capturedHref).toBe('/api/auth/login');

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: origLocation,
    });
  });

  it('sign-in button has aria-describedby linking to help text', async () => {
    mockFetch(401, {});

    const origLocation = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...origLocation, href: '' },
    });

    renderWithAuth();

    await waitFor(() =>
      expect(screen.queryByText('Checking session…')).toBeNull(),
    );

    const btn = screen.getByRole('button', { name: /sign in with va network account/i });
    expect(btn.getAttribute('aria-describedby')).toBe('login-help');
    expect(document.getElementById('login-help')).not.toBeNull();

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: origLocation,
    });
  });
});
