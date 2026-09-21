/**
 * AdGroupMappingsSection — "AD Group Mappings" section for the Admin Settings page.
 *
 * Story #67 acceptance criteria covered:
 *   AC1: Admin Settings page includes an "AD Group Mappings" section.
 *   AC2: Admin can add a mapping: AD Group Name (text) → CMS Role (select).
 *   AC3: Multiple groups can map to the same role (no UI restriction).
 *
 * Accessible:
 *   - All inputs have <label> + aria-describedby on error messages.
 *   - Uses USWDS 3.x classes exclusively (@uswds/uswds).
 *   - No inline styles, no Tailwind, no MUI, no Bootstrap.
 */

import React, { useState } from 'react';
import {
  useAdGroupMappings,
  useCreateAdGroupMapping,
  useDeleteAdGroupMapping,
} from './useAdGroupMappings';
import { RowActions } from '../../components/table';

// ── Role options — mirrors the six CMS roles defined in CmsRoles.cs ───────────
// Role Ids are the seed values from V012__rbac_roles_seed.sql
const ROLE_OPTIONS: { id: number; label: string }[] = [
  { id: 1, label: 'ContentOwner' },
  { id: 2, label: 'Editor' },
  { id: 3, label: 'SiteAdmin' },
  { id: 4, label: 'Developer' },
  { id: 5, label: 'SystemAdmin' },
  { id: 6, label: 'ReadOnly' },
];

export function AdGroupMappingsSection(): JSX.Element {
  const { data: mappings, isLoading, isError } = useAdGroupMappings();
  const createMutation = useCreateAdGroupMapping();
  const deleteMutation = useDeleteAdGroupMapping();

  const [adGroup, setAdGroup]   = useState('');
  const [roleId, setRoleId]     = useState<number>(2); // default: Editor
  const [formError, setFormError] = useState('');

  const handleSubmit = (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setFormError('');

    if (!adGroup.trim()) {
      setFormError('AD Group Name is required.');
      return;
    }

    createMutation.mutate(
      { adGroup: adGroup.trim(), roleId },
      {
        onSuccess: () => {
          setAdGroup('');
          setRoleId(2);
        },
        onError: (err: unknown) => {
          setFormError(err instanceof Error ? err.message : 'An error occurred.');
        },
      },
    );
  };

  return (
    <section aria-labelledby="ad-group-mappings-heading">
      <h2 id="ad-group-mappings-heading" className="usa-prose h3">
        AD Group Mappings
      </h2>
      <p className="usa-prose">
        Map Azure AD groups to CMS roles. Mappings are applied at each login. Explicitly
        assigned user roles always override group mappings.
      </p>

      {/* ── Add Mapping Form ──────────────────────────────────────────────── */}
      <form
        className="usa-form usa-form--large"
        onSubmit={handleSubmit}
        aria-label="Add AD group mapping"
        noValidate
      >
        <fieldset className="usa-fieldset">
          <legend className="usa-legend usa-legend--large">Add Mapping</legend>

          {/* AD Group Name */}
          <div className="usa-form-group">
            <label className="usa-label" htmlFor="ad-group-name">
              AD Group Name{' '}
              <abbr title="required" className="usa-hint--required">
                *
              </abbr>
            </label>
            {formError && (
              <span
                id="ad-group-name-error"
                className="usa-error-message"
                role="alert"
              >
                {formError}
              </span>
            )}
            <input
              id="ad-group-name"
              name="adGroup"
              type="text"
              className={`usa-input${formError ? ' usa-input--error' : ''}`}
              value={adGroup}
              onChange={(e) => setAdGroup(e.target.value)}
              aria-required="true"
              aria-describedby={formError ? 'ad-group-name-error' : undefined}
              placeholder="e.g. VA-CMS-Editors"
            />
          </div>

          {/* CMS Role */}
          <div className="usa-form-group">
            <label className="usa-label" htmlFor="cms-role-select">
              CMS Role{' '}
              <abbr title="required" className="usa-hint--required">
                *
              </abbr>
            </label>
            <select
              id="cms-role-select"
              name="roleId"
              className="usa-select"
              value={roleId}
              onChange={(e) => setRoleId(Number(e.target.value))}
              aria-required="true"
            >
              {ROLE_OPTIONS.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.label}
                </option>
              ))}
            </select>
          </div>

          <button
            type="submit"
            className="usa-button"
            disabled={createMutation.isPending}
          >
            {createMutation.isPending ? 'Adding…' : 'Add Mapping'}
          </button>
        </fieldset>
      </form>

      {/* ── Existing Mappings Table ───────────────────────────────────────── */}
      <div className="margin-top-4">
        {isLoading && <p className="usa-prose">Loading mappings…</p>}
        {isError && (
          <p className="usa-prose usa-error-message" role="alert">
            Failed to load mappings. Please refresh the page.
          </p>
        )}
        {!isLoading && !isError && mappings !== undefined && (
          <>
            {mappings.length === 0 ? (
              <p className="usa-prose">No mappings configured.</p>
            ) : (
              <div className="usa-table-container--scrollable" tabIndex={0}>
                <table className="usa-table usa-table--borderless width-full">
                  <caption className="usa-sr-only">AD group to CMS role mappings</caption>
                  <thead>
                    <tr>
                      <th scope="col">AD Group</th>
                      <th scope="col">CMS Role</th>
                      <th scope="col">
                        <span className="usa-sr-only">Actions</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {mappings.map((row) => (
                      <tr key={row.id}>
                        <td>{row.adGroup}</td>
                        <td>{row.roleName}</td>
                        <td>
                          <RowActions>
                            <button
                              type="button"
                              className="usa-button usa-button--unstyled text-error"
                              onClick={() => deleteMutation.mutate(row.id)}
                              disabled={deleteMutation.isPending}
                              aria-label={`Remove mapping: ${row.adGroup} → ${row.roleName}`}
                            >
                              Remove
                            </button>
                          </RowActions>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </div>
    </section>
  );
}
