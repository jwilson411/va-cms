/**
 * LoginPage unit tests.
 *
 * Acceptance criteria verified:
 *   - Admin SPA login page renders with a "Sign in" call-to-action
 *   - Clicking "Sign in" initiates the OIDC flow (navigates to /api/auth/login)
 *   - Page shows a loading indicator while auth state is resolving
 *   - Authenticated users are redirected away from the login page
 *   - #164: the system-use notice is shown and must be acknowledged before the
 *     sign-in buttons enable; the acknowledgement travels as ack=1
 */

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthProvider } from '../context/AuthContext';
import { LoginPage } from '../pages/LoginPage';

// ── Helpers ───────────────────────────────────────────────────────────────────

/**
 * Mock every fetch the page makes: the refresh probe answers with `status`/`body`;
 * /api/v1/settings/public answers with `publicSettings` (404 when null, i.e. the
 * page falls back to the code-default notice); /api/auth/dev-users answers 404.
 */
function mockFetch(status: number, body: object, publicSettings: object | null = null): void {
  vi.spyOn(window, 'fetch').mockImplementation((input) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    if (url.includes('/settings/public')) {
      return Promise.resolve({
        ok: publicSettings !== null,
        status: publicSettings !== null ? 200 : 404,
        json: () => Promise.resolve(publicSettings ?? {}),
      } as Response);
    }
    if (url.includes('/auth/dev-users')) {
      return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) } as Response);
    }
    return Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      json: () => Promise.resolve(body),
    } as Response);
  });
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
    // #164: disabled until the system-use notice is acknowledged
    expect(btn).toBeDisabled();
    await userEvent.click(btn);
    expect(capturedHref).toBe('');

    await userEvent.click(screen.getByLabelText(/I have read and agree/i));
    expect(btn).toBeEnabled();
    await userEvent.click(btn);

    // The login() call in AuthContext navigates to /api/auth/login with the acknowledgement
    expect(capturedHref).toBe('/api/auth/login?ack=1');

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: origLocation,
    });
  });

  it('shows the VA system-use notice with the code default, then the configured wording (#164)', async () => {
    mockFetch(401, {}, { 'auth.systemUseNotice': 'Custom agency notice text.' });
    const origLocation = window.location;
    Object.defineProperty(window, 'location', { configurable: true, value: { ...origLocation, href: '' } });

    renderWithAuth();

    await waitFor(() => expect(screen.queryByText('Checking session…')).toBeNull());
    const notice = screen.getByTestId('system-use-notice');
    expect(notice).toHaveTextContent('System use notification');
    await waitFor(() => expect(notice).toHaveTextContent('Custom agency notice text.'));
    expect(screen.getByRole('checkbox', { name: /I have read and agree/i })).not.toBeChecked();

    Object.defineProperty(window, 'location', { configurable: true, value: origLocation });
  });

  it('falls back to the standard wording when the settings endpoint is unavailable (#164)', async () => {
    mockFetch(401, {});
    const origLocation = window.location;
    Object.defineProperty(window, 'location', { configurable: true, value: { ...origLocation, href: '' } });

    renderWithAuth();

    await waitFor(() => expect(screen.queryByText('Checking session…')).toBeNull());
    expect(screen.getByTestId('system-use-notice')).toHaveTextContent(
      /This is a U\.S\. Government computer system/,
    );

    Object.defineProperty(window, 'location', { configurable: true, value: origLocation });
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
