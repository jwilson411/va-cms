import { useAuth } from '../context/AuthContext';

export function DashboardPage(): JSX.Element {
  const { logout } = useAuth();

  return (
    <main id="main-content" className="grid-container">
      <h1>VA CMS Admin</h1>
      <p>You are signed in.</p>
      <button
        type="button"
        className="usa-button usa-button--secondary"
        onClick={() => void logout()}
      >
        Sign out
      </button>
    </main>
  );
}
