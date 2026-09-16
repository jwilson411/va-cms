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
 */

import { useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import usFlagSmall from '@uswds/uswds/img/us_flag_small.png';

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

export function LoginPage(): JSX.Element {
  const { isAuthenticated, loading, login, devLogin } = useAuth();
  const [devUsers, setDevUsers] = useState<string[]>([]);
  const [devError, setDevError] = useState<string | null>(null);

  // Probe for DevBypass mode once; the AD button is always rendered as the fallback.
  useEffect(() => {
    let cancelled = false;
    void fetchDevUsers().then((users) => {
      if (!cancelled) setDevUsers(users);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleDevLogin = async (upn: string): Promise<void> => {
    setDevError(null);
    try {
      await devLogin(upn);
    } catch (err) {
      setDevError(err instanceof Error ? err.message : 'Dev sign-in failed');
    }
  };

  // If the user already has a valid token (e.g., landed here via direct link
  // but a refresh cookie is still valid), redirect them away from the login page.
  useEffect(() => {
    if (!loading && isAuthenticated) {
      window.location.href = '/admin';
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
            <p className="usa-intro">
              Sign in with your VA network account to access the content management
              system.
            </p>

            <p>
              This system uses VA Active Directory (Azure AD) for authentication. No
              separate CMS password is required.
            </p>

            <button
              type="button"
              className="usa-button"
              onClick={login}
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
