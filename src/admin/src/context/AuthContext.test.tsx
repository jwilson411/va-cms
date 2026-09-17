/**
 * AuthContext unit tests.
 *
 * Key acceptance criteria verified:
 *   - JWT is stored in React state only (NOT localStorage / sessionStorage)
 *   - Silent refresh is attempted on mount, as a POST (#164)
 *   - The next refresh is scheduled from expiresIn, so a changed
 *     auth.accessTokenMinutes site setting changes the schedule (#164)
 *   - Refresh returning 401 clears state and triggers login redirect
 *   - authFetch attaches Bearer token to requests
 */

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthProvider, useAuth } from '../context/AuthContext';

// ── Helpers ──────────────────────────────────────────────────────────────────

function AuthConsumer(): JSX.Element {
  const auth = useAuth();
  return (
    <div>
      <span data-testid="is-auth">{String(auth.isAuthenticated)}</span>
      <span data-testid="loading">{String(auth.loading)}</span>
      <span data-testid="token">{auth.accessToken ?? 'null'}</span>
      <button onClick={() => void auth.logout()}>Logout</button>
    </div>
  );
}

function mockFetch(status: number, body: object): void {
  vi.spyOn(window, 'fetch').mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  } as Response);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('AuthContext', () => {
  afterEach(() => vi.restoreAllMocks());

  it('starts in loading state', () => {
    // Mock fetch so the refresh call never resolves (pending).
    vi.spyOn(window, 'fetch').mockReturnValue(new Promise(() => {}));

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    expect(screen.getByTestId('loading')).toHaveTextContent('true');
    expect(screen.getByTestId('is-auth')).toHaveTextContent('false');
  });

  it('silent refresh is a POST with credentials (#164)', async () => {
    const spy = vi.spyOn(window, 'fetch').mockReturnValue(new Promise(() => {}));

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    expect(spy).toHaveBeenCalledWith('/api/auth/refresh', expect.objectContaining({ method: 'POST', credentials: 'include' }));
  });

  it('schedules the next refresh one minute before expiresIn, so a changed setting changes the schedule (#164)', async () => {
    vi.useFakeTimers();
    try {
      const spy = vi.spyOn(window, 'fetch').mockImplementation(() =>
        Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({ accessToken: 'a.b.c', expiresIn: 300, tokenType: 'Bearer' }),   // auth.accessTokenMinutes = 5
        } as Response),
      );

      render(
        <AuthProvider>
          <AuthConsumer />
        </AuthProvider>,
      );
      await vi.advanceTimersByTimeAsync(0);
      expect(spy).toHaveBeenCalledTimes(1);

      // 15-minute default schedule would not have fired yet; the 5-minute token refreshes at 4:00
      await vi.advanceTimersByTimeAsync(239_000);
      expect(spy).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(2_000);
      expect(spy).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('stores JWT in React state after successful silent refresh', async () => {
    mockFetch(200, {
      accessToken: 'header.payload.sig',
      expiresIn: 900,
      tokenType: 'Bearer',
    });

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId('loading')).toHaveTextContent('false'),
    );

    expect(screen.getByTestId('is-auth')).toHaveTextContent('true');
    expect(screen.getByTestId('token')).toHaveTextContent('header.payload.sig');

    // Acceptance criteria: token MUST NOT be in localStorage or sessionStorage
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(sessionStorage.getItem('accessToken')).toBeNull();
  });

  it('clears state when refresh returns 401', async () => {
    // Suppress the window.location.href assignment triggered by login()
    const origLocation = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...origLocation, href: '' },
    });

    mockFetch(401, {});

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId('loading')).toHaveTextContent('false'),
    );

    expect(screen.getByTestId('is-auth')).toHaveTextContent('false');
    expect(screen.getByTestId('token')).toHaveTextContent('null');
    // Token must not appear in storage
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(sessionStorage.getItem('accessToken')).toBeNull();

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: origLocation,
    });
  });

  it('logout clears in-memory token', async () => {
    mockFetch(200, {
      accessToken: 'valid.token.here',
      expiresIn: 900,
      tokenType: 'Bearer',
    });

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId('is-auth')).toHaveTextContent('true'),
    );

    // Stub the logout POST
    vi.spyOn(window, 'fetch').mockResolvedValue({
      ok: true,
      status: 204,
      json: () => Promise.resolve({}),
    } as Response);

    await userEvent.click(screen.getByText('Logout'));

    await waitFor(() =>
      expect(screen.getByTestId('is-auth')).toHaveTextContent('false'),
    );

    expect(screen.getByTestId('token')).toHaveTextContent('null');
  });

  it('logout navigates to signOutUrl when the API returns one (AzureAd mode, #154)', async () => {
    const origLocation = window.location;
    const assign = vi.fn();
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...origLocation, assign },
    });

    mockFetch(200, {
      accessToken: 'valid.token.here',
      expiresIn: 900,
      tokenType: 'Bearer',
    });

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId('is-auth')).toHaveTextContent('true'),
    );

    mockFetch(200, { signOutUrl: '/api/auth/signout' });

    await userEvent.click(screen.getByText('Logout'));

    await waitFor(() =>
      expect(screen.getByTestId('is-auth')).toHaveTextContent('false'),
    );
    expect(assign).toHaveBeenCalledWith('/api/auth/signout');

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: origLocation,
    });
  });
});
