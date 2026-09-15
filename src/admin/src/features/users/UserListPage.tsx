/**
 * UserListPage — Admin user directory (issue #56, BRD FR-USERS-03 / FR-USERS-04).
 *
 * Route: /admin/users
 *
 * Features:
 *   - Searchable table of all active users (name, email, last login)
 *   - Click a user row to navigate to the detail/role-assignment page
 *   - Deactivate user action with confirmation
 *
 * Accessibility:
 *   - All inputs have <label> elements.
 *   - aria-describedby on error messages.
 *   - No inline styles. USWDS 3.x only.
 */

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useUsers, useDeactivateUser } from './useUsers';

export function UserListPage(): JSX.Element {
  const navigate = useNavigate();
  const [search, setSearch]               = useState('');
  const [searchQuery, setSearchQuery]     = useState('');
  const [confirmDeactId, setConfirmDeactId] = useState<number | null>(null);

  const { data: users, isLoading, isError } = useUsers(searchQuery || undefined);
  const deactivate = useDeactivateUser();

  function handleSearch(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setSearchQuery(search.trim());
  }

  function handleDeactivateClick(id: number) {
    setConfirmDeactId(id);
  }

  function handleDeactivateConfirm() {
    if (confirmDeactId === null) return;
    deactivate.mutate(confirmDeactId, {
      onSuccess: () => setConfirmDeactId(null),
      onError: () => setConfirmDeactId(null),
    });
  }

  return (
    <main id="main-content" className="grid-container">
      <h1>User Directory</h1>
      <p className="usa-prose">
        Manage CMS users, assign roles, and deactivate accounts.
      </p>

      {/* ── Search form ────────────────────────────────────────────────────── */}
      <form onSubmit={handleSearch} className="usa-search usa-search--small margin-bottom-3" role="search">
        <label className="usa-label usa-sr-only" htmlFor="user-search-input">
          Search by name or email
        </label>
        <input
          id="user-search-input"
          className="usa-input"
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search by name or email"
          aria-label="Search users by name or email"
          autoComplete="off"
        />
        <button type="submit" className="usa-button">
          Search
        </button>
        {searchQuery && (
          <button
            type="button"
            className="usa-button usa-button--unstyled margin-left-2"
            onClick={() => { setSearch(''); setSearchQuery(''); }}
          >
            Clear
          </button>
        )}
      </form>

      {/* ── Deactivate confirmation ────────────────────────────────────────── */}
      {confirmDeactId !== null && (
        <div
          className="usa-alert usa-alert--warning margin-bottom-3"
          role="alertdialog"
          aria-modal="false"
          aria-labelledby="deact-confirm-heading"
        >
          <div className="usa-alert__body">
            <h4 className="usa-alert__heading" id="deact-confirm-heading">
              Deactivate this user?
            </h4>
            <p className="usa-alert__text">
              The user will be unable to log in at their next session. This cannot be undone from the UI.
            </p>
            <button
              type="button"
              className="usa-button usa-button--secondary margin-right-1"
              onClick={handleDeactivateConfirm}
              aria-label="Confirm deactivate user"
              disabled={deactivate.isPending}
            >
              {deactivate.isPending ? 'Deactivating…' : 'Deactivate'}
            </button>
            <button
              type="button"
              className="usa-button usa-button--unstyled"
              onClick={() => setConfirmDeactId(null)}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* ── States ────────────────────────────────────────────────────────── */}
      {isLoading && (
        <p className="usa-prose" aria-live="polite" aria-busy="true">
          Loading users…
        </p>
      )}

      {isError && (
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <p className="usa-alert__text">
              Failed to load users. Please refresh the page.
            </p>
          </div>
        </div>
      )}

      {/* ── User table ────────────────────────────────────────────────────── */}
      {!isLoading && !isError && users && (
        <>
          {users.length === 0 ? (
            <p className="usa-prose">
              {searchQuery ? `No users found matching "${searchQuery}".` : 'No active users found.'}
            </p>
          ) : (
            <table
              className="usa-table usa-table--striped usa-table--compact usa-table--scrollable"
              aria-label="Active CMS users"
            >
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Email</th>
                  <th scope="col">Last login</th>
                  <th scope="col"><span className="usa-sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <button
                        type="button"
                        className="usa-button usa-button--unstyled"
                        onClick={() => navigate(`/admin/users/${user.id}`)}
                        aria-label={`View details for ${user.displayName}`}
                      >
                        {user.displayName}
                      </button>
                    </td>
                    <td>{user.email}</td>
                    <td>
                      {user.lastLoginAt
                        ? new Date(user.lastLoginAt).toLocaleDateString()
                        : <span className="text-base">Never</span>}
                    </td>
                    <td>
                      <button
                        type="button"
                        className="usa-button usa-button--unstyled text-error"
                        onClick={() => handleDeactivateClick(user.id)}
                        aria-label={`Deactivate ${user.displayName}`}
                      >
                        Deactivate
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </main>
  );
}
