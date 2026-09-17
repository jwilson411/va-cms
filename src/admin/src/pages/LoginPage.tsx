/**
 * LoginPage — entry point for the VA CMS Admin SPA OIDC login flow.
 *
 * Acceptance criteria:
 *   - Admin SPA login page uses the OIDC flow
 *   - Clicking "Sign in with VA network account" navigates to GET /api/auth/login
 *     which redirects to Azure AD (OIDC)
 *
 * Local development (API running with Auth:Mode=DevBypass):
 *   - GET /api/auth/dev-users returns the allowed dev UPNs (404 otherwise)
 *   - A "Development sign-in" panel lists them; picking one calls devLogin(),
 *     which POSTs to /api/auth/dev-login and stores the JWT in memory
 *
 * #164 (VA Handbook 6500 / NIST AC-8): the system-use notification is shown
 * before authentication and must be acknowledged before any sign-in button
 * enables. The wording is the auth.systemUseNotice site setting (Public scope,
 * read anonymously from /api/v1/settings/public); the API records the
 * acknowledgement on the Logon audit row and refuses to start a sign-in
 * without it.
 */

import { useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import usFlagSmall from '@uswds/uswds/img/us_flag_small.png';

/** Anonymous settings endpoint (Public scope only). */
export const PUBLIC_SETTINGS_API = '/api/v1/settings/public';
export const SYSTEM_USE_NOTICE_KEY = 'auth.systemUseNotice';

/**
 * Code default — must match SiteSettingDefinitions.SystemUseNoticeDefault so the
 * banner is correct even before (or without) the settings request.
 */
export const SYSTEM_USE_NOTICE_DEFAULT =
  'This is a U.S. Government computer system, which may be accessed and used only for authorized Government ' +
  'business by authorized personnel. Unauthorized access or use of this computer system may subject violators ' +
  'to criminal, civil, and/or administrative action. All information on this computer system may be ' +
  'intercepted, recorded, read, copied, and disclosed by and to authorized personnel for official purposes, ' +
  'including criminal investigations. Such information includes sensitive data encrypted to comply with ' +
  'confidentiality and privacy requirements. Access or use of this computer system by any person, whether ' +
  'authorized or unauthorized, constitutes consent to these terms. There is no right of privacy in this system.';

/** Fetch the system-use notice text; falls back to the code default on any failure. */
async function fetchSystemUseNotice(): Promise<string> {
  try {
    const res = await fetch(PUBLIC_SETTINGS_API);
    if (!res.ok) return SYSTEM_USE_NOTICE_DEFAULT;
    const data = (await res.json()) as Record<string, unknown>;
    const text = data[SYSTEM_USE_NOTICE_KEY];
    return typeof text === 'string' && text.trim().length > 0 ? text : SYSTEM_USE_NOTICE_DEFAULT;
  } catch {
    return SYSTEM_USE_NOTICE_DEFAULT;
  }
}

/**
 * Destination after sign-in: the ?returnUrl if it is a same-site admin path
 * (never a protocol-relative or absolute URL), otherwise /admin.
 */
function safeReturnUrl(search: string): string {
  const raw = new URLSearchParams(search).get('returnUrl');
  if (raw && raw.startsWith('/') && !raw.startsWith('//') && !raw.startsWith('/login')) return raw;
  return '/admin';
}

/** Fetch the DevBypass user list; resolves to [] when the API is not in DevBypass mode. */
async function fetchDevUsers(): Promise<string[]> {
  try {
    const res = await fetch('/api/auth/dev-users');
    if (!res.ok) return [];
    const data: { users?: unknown } = await res.json();
    return Array.isArray(data.users) ? data.users.filter((u): u is string => typeof u === 'string') : [];
  } catch {
    return [];
  }
}

/** Messages for the ?error= codes the API's login callback can redirect back with (#155). */
const LOGIN_ERRORS: Record<string, string> = {
  not_provisioned:
    'Your VA account is not set up in the CMS. Contact your site administrator to be added, then sign in again.',
};

export function LoginPage(): JSX.Element {
  const { isAuthenticated, loading, login, devLogin } = useAuth();
  const [devUsers, setDevUsers] = useState<string[]>([]);
  const [devError, setDevError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string>(SYSTEM_USE_NOTICE_DEFAULT);
  const [acknowledged, setAcknowledged] = useState(false);
  const loginError = LOGIN_ERRORS[new URLSearchParams(window.location.search).get('error') ?? ''] ?? null;

  // Probe for DevBypass mode once; the AD button is always rendered as the fallback.
  // Load the configured notice wording alongside.
  useEffect(() => {
    let cancelled = false;
    void fetchDevUsers().then((users) => {
      if (!cancelled) setDevUsers(users);
    });
    void fetchSystemUseNotice().then((text) => {
      if (!cancelled) setNotice(text);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleDevLogin = async (upn: string): Promise<void> => {
    setDevError(null);
    try {
      await devLogin(upn, { acknowledged });
    } catch (err) {
      setDevError(err instanceof Error ? err.message : 'Dev sign-in failed');
    }
  };

  // Once signed in (or already signed in via a valid refresh cookie), leave the
  // login page for the originally requested admin path, or the dashboard.
  useEffect(() => {
    if (!loading && isAuthenticated) {
      window.location.href = safeReturnUrl(window.location.search);
    }
  }, [loading, isAuthenticated]);

  if (loading) {
    return (
      <main id="main-content" className="grid-container">
        <div className="usa-alert usa-alert--info" role="status" aria-live="polite">
          <div className="usa-alert__body">
            <p className="usa-alert__text">Checking session…</p>
          </div>
        </div>
      </main>
    );
  }

  return (
    <main id="main-content" className="grid-container">
      <div className="grid-row grid-gap">
        <div className="tablet:grid-col-8 tablet:grid-offset-2">
          <div className="margin-top-6">
            {/* USWDS USA Banner */}
            <div className="usa-banner">
              <div className="usa-accordion">
                <header className="usa-banner__header">
                  <div className="usa-banner__inner">
                    <div className="grid-col-auto">
                      <img
                        aria-hidden="true"
                        className="usa-banner__header-flag"
                        src={usFlagSmall}
                        alt=""
                      />
                    </div>
                    <div className="grid-col-fill tablet:grid-col-auto" aria-hidden="true">
                      <p className="usa-banner__header-text">
                        An official website of the United States government
                      </p>
                    </div>
                  </div>
                </header>
              </div>
            </div>

            <h1 className="margin-top-4">VA CMS Admin</h1>
            {loginError && (
              <div className="usa-alert usa-alert--error margin-bottom-3" role="alert">
                <div className="usa-alert__body">
                  <p className="usa-alert__text">{loginError}</p>
                </div>
              </div>
            )}
            <p className="usa-intro">
              Sign in with your VA network account to access the content management
              system.
            </p>

            <p>
              This system uses VA Active Directory (Azure AD) for authentication. No
              separate CMS password is required.
            </p>

            {/* System-use notification (NIST AC-8) — must be acknowledged before sign-in */}
            <section
              className="usa-alert usa-alert--info usa-alert--no-icon margin-bottom-3"
              aria-labelledby="system-use-heading"
              data-testid="system-use-notice"
            >
              <div className="usa-alert__body">
                <h2 id="system-use-heading" className="usa-alert__heading">
                  System use notification
                </h2>
                <p className="usa-alert__text">{notice}</p>
                <div className="usa-checkbox margin-top-2">
                  <input
                    id="system-use-ack"
                    className="usa-checkbox__input"
                    type="checkbox"
                    checked={acknowledged}
                    onChange={(e) => setAcknowledged(e.target.checked)}
                    aria-describedby="system-use-ack-hint"
                  />
                  <label className="usa-checkbox__label" htmlFor="system-use-ack">
                    I have read and agree to these terms
                  </label>
                  <span id="system-use-ack-hint" className="usa-hint display-block margin-top-1">
                    You must agree before you can sign in.
                  </span>
                </div>
              </div>
            </section>

            <button
              type="button"
              className="usa-button"
              onClick={() => login({ acknowledged: true })}
              disabled={!acknowledged}
              aria-describedby="login-help"
            >
              Sign in with VA network account
            </button>

            <p id="login-help" className="usa-hint margin-top-2">
              You will be redirected to the VA identity provider to complete sign-in.
            </p>

            {devUsers.length > 0 && (
              <section
                className="usa-alert usa-alert--warning margin-top-4"
                aria-labelledby="dev-login-heading"
              >
                <div className="usa-alert__body">
                  <h2 id="dev-login-heading" className="usa-alert__heading">
                    Development sign-in
                  </h2>
                  <p className="usa-alert__text">
                    The API is running in <code>DevBypass</code> mode. Pick a sample user to
                    sign in without Azure AD.
                  </p>
                  <ul className="usa-button-group margin-top-2">
                    {devUsers.map((upn) => (
                      <li key={upn} className="usa-button-group__item">
                        <button
                          type="button"
                          className="usa-button usa-button--outline"
                          onClick={() => void handleDevLogin(upn)}
                          disabled={!acknowledged}
                        >
                          Sign in as {upn}
                        </button>
                      </li>
                    ))}
                  </ul>
                  {devError && (
                    <p className="usa-error-message" role="alert">
                      {devError}
                    </p>
                  )}
                </div>
              </section>
            )}
          </div>
        </div>
      </div>
    </main>
  );
}
