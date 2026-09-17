/**
 * IdleSessionGuard — admin session inactivity control (#164, VA Handbook 6500 /
 * NIST AC-11, AC-12).
 *
 * Watches for user activity (pointer, keyboard, scroll, touch, tab focus). After
 * `auth.idleTimeoutMinutes` (site setting, default 15) minus two minutes with no
 * activity a USWDS modal warns the user with a countdown; "Stay signed in" resets
 * the clock, otherwise logout() runs at the limit. The API enforces the same
 * window on the refresh token (LastUsedAt), so a tab left open past the limit
 * cannot silently refresh even if this timer were defeated.
 *
 * Mounted inside AdminLayout, i.e. only while a session exists.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useFocusTrap } from '../hooks/useFocusTrap';
import { clientSettingKeys, useClientSettings } from '../features/siteSettings/useClientSettings';

/** Show the warning this long before the idle limit. */
export const IDLE_WARNING_MS = 2 * 60_000;

/** Activity is sampled at most this often so a busy mousemove stream stays cheap. */
const ACTIVITY_THROTTLE_MS = 1_000;

const ACTIVITY_EVENTS: (keyof WindowEventMap)[] = [
  'mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'wheel', 'focus',
];

interface IdleSessionGuardProps {
  /** Override the site-setting driven limit (tests). Milliseconds. */
  idleTimeoutMs?: number;
}

export function IdleSessionGuard({ idleTimeoutMs }: IdleSessionGuardProps): JSX.Element | null {
  const { logout } = useAuth();
  const settings = useClientSettings();
  const limitMs = idleTimeoutMs ?? Math.max(1, settings.getInt(clientSettingKeys.authIdleTimeoutMinutes)) * 60_000;
  const warnAtMs = Math.max(limitMs - IDLE_WARNING_MS, Math.floor(limitMs / 2));

  const lastActivityRef = useRef(Date.now());
  const lastSampleRef = useRef(0);
  const [warning, setWarning] = useState(false);
  const [secondsLeft, setSecondsLeft] = useState(0);
  const dialogRef = useRef<HTMLDivElement>(null);

  const resetActivity = useCallback(() => {
    lastActivityRef.current = Date.now();
    setWarning(false);
  }, []);

  // Sample activity while the warning is not showing (once it shows, only the
  // explicit "Stay signed in" button counts, so a stray mouse move on a walked-away
  // workstation does not extend the session).
  useEffect(() => {
    const onActivity = (): void => {
      if (warning) return;
      const now = Date.now();
      if (now - lastSampleRef.current < ACTIVITY_THROTTLE_MS) return;
      lastSampleRef.current = now;
      lastActivityRef.current = now;
    };
    for (const ev of ACTIVITY_EVENTS) window.addEventListener(ev, onActivity, { passive: true });
    return () => {
      for (const ev of ACTIVITY_EVENTS) window.removeEventListener(ev, onActivity);
    };
  }, [warning]);

  // One-second tick drives both the warning threshold and the countdown.
  useEffect(() => {
    const tick = window.setInterval(() => {
      const idleFor = Date.now() - lastActivityRef.current;
      if (idleFor >= limitMs) {
        window.clearInterval(tick);
        void logout();
        return;
      }
      if (idleFor >= warnAtMs) {
        setWarning(true);
        setSecondsLeft(Math.max(0, Math.ceil((limitMs - idleFor) / 1000)));
      }
    }, 1000);
    return () => window.clearInterval(tick);
  }, [limitMs, warnAtMs, logout]);

  useFocusTrap(dialogRef, { enabled: warning, onEscape: resetActivity });

  if (!warning) return null;

  const minutes = Math.floor(secondsLeft / 60);
  const seconds = String(secondsLeft % 60).padStart(2, '0');

  return (
    <div
      className="usa-modal"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="idle-session-title"
      aria-describedby="idle-session-desc"
      data-testid="idle-session-dialog"
      ref={dialogRef}
    >
      <div className="usa-modal__content">
        <div className="usa-modal__main">
          <h2 id="idle-session-title" className="usa-modal__heading">
            Your session is about to expire
          </h2>
          <div className="usa-prose">
            <p id="idle-session-desc" aria-live="polite">
              You have been inactive for a while. For security you will be signed out in{' '}
              <strong>
                {minutes}:{seconds}
              </strong>
              . Unsaved changes will be lost.
            </p>
          </div>
          <div className="usa-modal__footer">
            <ul className="usa-button-group">
              <li className="usa-button-group__item">
                <button type="button" className="usa-button" onClick={resetActivity}>
                  Stay signed in
                </button>
              </li>
              <li className="usa-button-group__item">
                <button
                  type="button"
                  className="usa-button usa-button--unstyled padding-105 text-center"
                  onClick={() => void logout()}
                >
                  Sign out now
                </button>
              </li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
