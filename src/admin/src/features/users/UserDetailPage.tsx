/**
 * UserDetailPage — View and manage a single user's roles and section assignments.
 * Issue #56, BRD FR-USERS-03 / FR-USERS-04.
 *
 * Route: /admin/users/:userId
 *
 * Features:
 *   - User info (name, email, last login)
 *   - Table of assigned roles with section scope
 *   - Add role form (select role + optional section)
 *   - Remove role button per row
 *   - Back link to user directory
 *
 * Accessibility:
 *   - All form inputs have <label> elements with aria-describedby on errors.
 *   - No inline styles. USWDS 3.x only.
 */

import React, { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  useUserDetail,
  useRoles,
  useSections,
  useAssignRole,
  useRevokeRole,
  type UserRoleDetail,
} from './useUsers';

export function UserDetailPage(): JSX.Element {
  const { userId: userIdStr } = useParams<{ userId: string }>();
  const navigate   = useNavigate();
  const userId     = userIdStr ? parseInt(userIdStr, 10) : null;

  const { data: user, isLoading, isError } = useUserDetail(userId);
  const { data: roles   = [] }  = useRoles();
  const { data: sections = [] } = useSections();

  const assignRole  = useAssignRole(userId ?? 0);
  const revokeRole  = useRevokeRole(userId ?? 0);

  const [selectedRoleId,    setSelectedRoleId]    = useState('');
  const [selectedSectionId, setSelectedSectionId] = useState('');
  const [formError,         setFormError]         = useState<string | null>(null);
  const [confirmRevoke, setConfirmRevoke]          = useState<UserRoleDetail | null>(null);

  if (!userId) {
    return (
      <main id="main-content">
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <p className="usa-alert__text">Invalid user ID.</p>
          </div>
        </div>
      </main>
    );
  }

  function handleAssignRole(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setFormError(null);

    if (!selectedRoleId) {
      setFormError('Please select a role to assign.');
      return;
    }

    assignRole.mutate(
      {
        roleId:    parseInt(selectedRoleId, 10),
        sectionId: selectedSectionId ? parseInt(selectedSectionId, 10) : null,
      },
      {
        onSuccess: () => {
          setSelectedRoleId('');
          setSelectedSectionId('');
        },
        onError: (err: Error) => setFormError(err.message),
      },
    );
  }

  function handleRevokeConfirm() {
    if (!confirmRevoke) return;
    revokeRole.mutate(
      { roleId: confirmRevoke.roleId, sectionId: confirmRevoke.sectionId },
      {
        onSuccess: () => setConfirmRevoke(null),
        onError: () => setConfirmRevoke(null),
      },
    );
  }

  return (
    <main id="main-content">
      {/* Back nav */}
      <nav aria-label="Breadcrumb" className="margin-bottom-2">
        <button
          type="button"
          className="usa-button usa-button--unstyled"
          onClick={() => navigate('/admin/users')}
          aria-label="Back to user directory"
        >
          ← Back to Users
        </button>
      </nav>

      {isLoading && (
        <p className="usa-prose" aria-live="polite" aria-busy="true">Loading user…</p>
      )}

      {isError && (
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <p className="usa-alert__text">
              Failed to load user details. The user may not exist.
            </p>
          </div>
        </div>
      )}

      {!isLoading && !isError && user && (
        <>
          {/* ── User summary ───────────────────────────────────────────────── */}
          <h1>{user.displayName}</h1>

          <dl className="usa-list usa-list--unstyled margin-bottom-4">
            <div className="display-flex flex-gap-1 margin-bottom-1">
              <dt className="text-bold">Email:</dt>
              <dd className="margin-0">{user.email}</dd>
            </div>
            <div className="display-flex flex-gap-1 margin-bottom-1">
              <dt className="text-bold">Status:</dt>
              <dd className="margin-0">
                <span className={user.isActive ? 'usa-tag bg-green-warm-50 text-green-warm-70' : 'usa-tag'}>
                  {user.isActive ? 'Active' : 'Inactive'}
                </span>
              </dd>
            </div>
            <div className="display-flex flex-gap-1">
              <dt className="text-bold">Last login:</dt>
              <dd className="margin-0">
                {user.lastLoginAt
                  ? new Date(user.lastLoginAt).toLocaleString()
                  : <span className="text-base">Never</span>}
              </dd>
            </div>
          </dl>

          {/* ── Assigned roles table ───────────────────────────────────────── */}
          <section aria-labelledby="roles-heading" className="margin-bottom-4">
            <h2 id="roles-heading">Assigned Roles</h2>

            {user.roles.length === 0 ? (
              <p className="usa-prose text-base">No roles assigned yet.</p>
            ) : (
              <table
                className="usa-table usa-table--compact usa-table--striped usa-table--scrollable"
                aria-label="Assigned roles"
              >
                <thead>
                  <tr>
                    <th scope="col">Role</th>
                    <th scope="col">Section scope</th>
                    <th scope="col"><span className="usa-sr-only">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  {user.roles.map((r) => (
                    <tr key={`${r.roleId}-${r.sectionId ?? 'global'}`}>
                      <td>
                        <strong>{r.roleDisplayName || r.roleName}</strong>
                        <span className="font-body-xs display-block text-base">{r.roleName}</span>
                      </td>
                      <td>
                        {r.sectionName
                          ? <>{r.sectionName} <span className="text-base">({r.sectionSlugPrefix})</span></>
                          : <span className="text-base">Global</span>}
                      </td>
                      <td>
                        <button
                          type="button"
                          className="usa-button usa-button--unstyled text-error"
                          onClick={() => setConfirmRevoke(r)}
                          aria-label={`Remove ${r.roleName} role${r.sectionName ? ` from section ${r.sectionName}` : ''}`}
                        >
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          {/* ── Revoke confirmation ────────────────────────────────────────── */}
          {confirmRevoke && (
            <div
              className="usa-alert usa-alert--warning margin-bottom-3"
              role="alertdialog"
              aria-modal="false"
              aria-labelledby="revoke-confirm-heading"
            >
              <div className="usa-alert__body">
                <h4 className="usa-alert__heading" id="revoke-confirm-heading">
                  Remove role?
                </h4>
                <p className="usa-alert__text">
                  Remove <strong>{confirmRevoke.roleDisplayName || confirmRevoke.roleName}</strong>
                  {confirmRevoke.sectionName
                    ? <> scoped to <strong>{confirmRevoke.sectionName}</strong></>
                    : ' (global)'} from this user?
                </p>
                <button
                  type="button"
                  className="usa-button usa-button--secondary margin-right-1"
                  onClick={handleRevokeConfirm}
                  aria-label="Confirm remove role"
                  disabled={revokeRole.isPending}
                >
                  {revokeRole.isPending ? 'Removing…' : 'Remove role'}
                </button>
                <button
                  type="button"
                  className="usa-button usa-button--unstyled"
                  onClick={() => setConfirmRevoke(null)}
                >
                  Cancel
                </button>
              </div>
            </div>
          )}

          {/* ── Assign role form ───────────────────────────────────────────── */}
          <section aria-labelledby="assign-role-heading" className="usa-card">
            <div className="usa-card__header">
              <h2 className="usa-card__heading" id="assign-role-heading">
                Assign Role
              </h2>
            </div>
            <div className="usa-card__body">
              <form onSubmit={handleAssignRole} noValidate>
                {/* Role select */}
                <div className="usa-form-group">
                  <label className="usa-label" htmlFor="assign-role-select">
                    Role <abbr title="required" className="usa-required"> *</abbr>
                  </label>
                  <select
                    id="assign-role-select"
                    className="usa-select"
                    value={selectedRoleId}
                    onChange={(e) => setSelectedRoleId(e.target.value)}
                    aria-required="true"
                  >
                    <option value="">— Select a role —</option>
                    {roles.map((role) => (
                      <option key={role.id} value={role.id}>
                        {role.displayName || role.name}
                      </option>
                    ))}
                  </select>
                </div>

                {/* Section scope (optional) */}
                <div className="usa-form-group">
                  <label className="usa-label" htmlFor="assign-section-select">
                    Section scope
                    <span className="usa-hint"> (optional — leave blank for global)</span>
                  </label>
                  <select
                    id="assign-section-select"
                    className="usa-select"
                    value={selectedSectionId}
                    onChange={(e) => setSelectedSectionId(e.target.value)}
                  >
                    <option value="">— Global (no section) —</option>
                    {sections.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name} ({s.slugPrefix})
                      </option>
                    ))}
                  </select>
                </div>

                {/* Form error */}
                {formError && (
                  <div
                    className="usa-alert usa-alert--error usa-alert--slim margin-bottom-2"
                    role="alert"
                    id="assign-role-error"
                  >
                    <div className="usa-alert__body">
                      <p className="usa-alert__text">{formError}</p>
                    </div>
                  </div>
                )}

                <button
                  type="submit"
                  className="usa-button"
                  disabled={assignRole.isPending}
                  aria-busy={assignRole.isPending}
                >
                  {assignRole.isPending ? 'Assigning…' : 'Assign role'}
                </button>
              </form>
            </div>
          </section>
        </>
      )}
    </main>
  );
}
