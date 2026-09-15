/**
 * ProtectedRoute — wraps a route and redirects to login if the user
 * is not authenticated. Used to guard every admin page.
 */
import { useEffect } from 'react';
import { useAuth } from '../context/AuthContext';

interface ProtectedRouteProps {
  children: React.ReactNode;
}

export function ProtectedRoute({ children }: ProtectedRouteProps): JSX.Element {
  const { isAuthenticated, loading, login } = useAuth();

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

  return <>{children}</>;
}
