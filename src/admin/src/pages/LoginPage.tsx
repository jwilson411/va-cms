/**
 * LoginPage — entry point for the VA CMS Admin SPA OIDC login flow.
 *
 * Acceptance criteria:
 *   - Admin SPA login page uses the OIDC flow
 *   - Clicking "Sign in with VA network account" navigates to GET /api/auth/login
 *     which redirects to Azure AD (OIDC)
 */

import { useEffect } from 'react';
import { useAuth } from '../context/AuthContext';

export function LoginPage(): JSX.Element {
  const { isAuthenticated, loading, login } = useAuth();

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
                        src="/img/us_flag_small.png"
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
          </div>
        </div>
      </div>
    </main>
  );
}
