/**
 * IdleSessionGuard tests (#164, VA 6500 AC-11).
 *
 *   - Nothing renders while the user is active.
 *   - Two minutes before the idle limit a warning dialog with a countdown appears.
 *   - "Stay signed in" dismisses it and restarts the clock.
 *   - At the limit logout() is called.
 *   - Activity resets the clock while no warning is showing.
 */

import { act, fireEvent, render, screen } from '@testing-library/react';
import { IdleSessionGuard } from './IdleSessionGuard';

const logout = vi.fn(() => Promise.resolve());

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ logout }),
}));

// Site settings (epic #141): render with the code defaults, no QueryClient needed.
vi.mock('../features/siteSettings/useClientSettings', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/siteSettings/useClientSettings')>();
  return {
    ...actual,
    useClientSettings: () => actual.buildClientSettings({ 'auth.idleTimeoutMinutes': '4' }, { isLoading: false, isError: false }),
  };
});

const MINUTE = 60_000;

describe('IdleSessionGuard', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    logout.mockClear();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders nothing while the user is active', () => {
    render(<IdleSessionGuard />);
    expect(screen.queryByTestId('idle-session-dialog')).toBeNull();
  });

  it('reads the limit from auth.idleTimeoutMinutes and warns two minutes before it', async () => {
    render(<IdleSessionGuard />);   // 4 minutes → warning at 2:00

    await act(async () => { await vi.advanceTimersByTimeAsync(119_000); });
    expect(screen.queryByTestId('idle-session-dialog')).toBeNull();

    await act(async () => { await vi.advanceTimersByTimeAsync(2_000); });
    const dialog = screen.getByTestId('idle-session-dialog');
    expect(dialog).toHaveAttribute('role', 'alertdialog');
    expect(dialog).toHaveTextContent(/signed out in/);
    expect(logout).not.toHaveBeenCalled();
  });

  it('signs out at the limit', async () => {
    render(<IdleSessionGuard idleTimeoutMs={5 * MINUTE} />);

    await act(async () => { await vi.advanceTimersByTimeAsync(5 * MINUTE + 1_000); });

    expect(logout).toHaveBeenCalledTimes(1);
  });

  it('"Stay signed in" dismisses the warning and restarts the clock', async () => {
    render(<IdleSessionGuard idleTimeoutMs={5 * MINUTE} />);

    await act(async () => { await vi.advanceTimersByTimeAsync(3 * MINUTE + 1_000); });
    expect(screen.getByTestId('idle-session-dialog')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Stay signed in' }));
    expect(screen.queryByTestId('idle-session-dialog')).toBeNull();

    // Another full 5 minutes are available before sign-out
    await act(async () => { await vi.advanceTimersByTimeAsync(4 * MINUTE); });
    expect(logout).not.toHaveBeenCalled();
    await act(async () => { await vi.advanceTimersByTimeAsync(1 * MINUTE + 2_000); });
    expect(logout).toHaveBeenCalledTimes(1);
  });

  it('user activity resets the clock while no warning is showing', async () => {
    render(<IdleSessionGuard idleTimeoutMs={5 * MINUTE} />);

    await act(async () => { await vi.advanceTimersByTimeAsync(2 * MINUTE); });
    fireEvent.keyDown(window, { key: 'a' });
    await act(async () => { await vi.advanceTimersByTimeAsync(2 * MINUTE); });
    expect(screen.queryByTestId('idle-session-dialog')).toBeNull();   // 4 min since mount, 2 since activity

    await act(async () => { await vi.advanceTimersByTimeAsync(1 * MINUTE + 1_000); });
    expect(screen.getByTestId('idle-session-dialog')).toBeTruthy();   // 3:01 since activity → warning

    // Once the warning is up, a stray mouse move does not extend the session
    fireEvent.mouseMove(window);
    await act(async () => { await vi.advanceTimersByTimeAsync(2 * MINUTE + 1_000); });
    expect(logout).toHaveBeenCalledTimes(1);
  });
});
