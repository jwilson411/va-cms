/**
 * ProtectedRoute — wraps a route and redirects to login if the user
 * is not authenticated. Used to guard every admin page.
 */
import { useEffect } from 'react';
import { useAuth } from '../context/AuthContext';

interface ProtectedRouteProps {
  children: React.ReactNode;
}

/**
 * Shown to a signed-in user who holds no CMS role (#155). The API returns 403 to
 * every /api/v1 call for such a principal, so an empty dashboard would only
 * surface as a wall of errors.
 */
export function NoAccessPage(): JSX.Element {
  const { logout } = useAuth();
  return (
    <main id="main-content" className="grid-container">
      <div className="usa-alert usa-alert--warning margin-top-4" role="alert">
        <div className="usa-alert__body">
          <h1 className="usa-alert__heading">No access to the CMS</h1>
          <p className="usa-alert__text">
            Your account signed in successfully but has not been assigned a CMS role.
            Contact your site administrator to request access, then sign in again.
          </p>
        </div>
      </div>
      <button type="button" className="usa-button usa-button--outline margin-top-2" onClick={() => void logout()}>
        Sign out
      </button>
    </main>
  );
}

export function ProtectedRoute({ children }: ProtectedRouteProps): JSX.Element {
  const { isAuthenticated, loading, login, roles } = useAuth();

  useEffect(() => {
    if (!loading && !isAuthenticated) {
      // No token in memory — redirect to AD login.
      login();
    }
  }, [loading, isAuthenticated, login]);

  if (loading) {
    // Show USWDS-compliant loading indicator while we attempt silent refresh.
    return (
      <main id="main-content" className="grid-container">
        <div className="usa-alert usa-alert--info" role="status" aria-live="polite">
          <div className="usa-alert__body">
            <p className="usa-alert__text">Signing in…</p>
          </div>
        </div>
      </main>
    );
  }

  if (!isAuthenticated) {
    // Redirecting — return empty (the useEffect fires the navigation).
    return <></>;
  }

  if (roles.length === 0) {
    return <NoAccessPage />;
  }

  return <>{children}</>;
}
