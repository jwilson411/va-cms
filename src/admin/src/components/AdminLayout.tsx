/**
 * AdminLayout — shell for every protected admin page: USWDS sidenav (AdminNav)
 * in a left column, the routed page in the right column, sign-out at the top.
 * Pages keep their own <main id="main-content"> so the skip link keeps working.
 */
import { Outlet } from 'react-router-dom';
import { AdminNav, SkipNav } from './AdminNav';
import { useAuth } from '../context/AuthContext';

export function AdminLayout(): JSX.Element {
  const { logout } = useAuth();

  return (
    <div className="grid-container-widescreen">
      <SkipNav />
      <header className="display-flex flex-justify flex-align-center padding-y-2 border-bottom border-base-lighter">
        <span className="text-bold font-sans-lg">VA CMS Admin</span>
        <button
          type="button"
          className="usa-button usa-button--outline usa-button--small"
          onClick={() => void logout()}
        >
          Sign out
        </button>
      </header>
      <div className="grid-row grid-gap margin-top-2">
        <aside className="tablet:grid-col-3 desktop:grid-col-2">
          <AdminNav />
        </aside>
        <div className="tablet:grid-col-9 desktop:grid-col-10">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
