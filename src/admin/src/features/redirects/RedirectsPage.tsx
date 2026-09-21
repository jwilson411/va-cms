/**
 * RedirectsPage — admin redirect management table.
 * Issue #48 — BRD FR-NAV-06.
 *
 * Features:
 *   - Lists all redirects with From Path, To Path, Status Code, Created By, Active status
 *   - Create, Edit, Deactivate actions
 *   - Filter by active/inactive
 */

import React, { useState } from 'react';
import {
  useRedirects,
  useCreateRedirect,
  useUpdateRedirect,
  useDeactivateRedirect,
} from './api';
import { RedirectForm } from './RedirectForm';
import type { RedirectAdminDto } from './types';

type FilterMode = 'all' | 'active' | 'inactive';

export function RedirectsPage() {
  const [filter, setFilter]       = useState<FilterMode>('all');
  const [page, setPage]           = useState(1);
  const [editTarget, setEditTarget] = useState<RedirectAdminDto | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const isActiveParam =
    filter === 'active'   ? true  :
    filter === 'inactive' ? false :
    undefined;

  const { data, isLoading, isError } = useRedirects({
    isActive:  isActiveParam,
    page,
    pageSize: 50,
  });

  const createMutation    = useCreateRedirect();
  const deactivateMutation = useDeactivateRedirect();

  // Inline update mutation — keyed to current editTarget.id
  const updateMutation = useUpdateRedirect(editTarget?.id ?? 0);

  function handleCreate(values: { fromPath: string; toPath: string; statusCode: number }) {
    setActionError(null);
    createMutation.mutate(values, {
      onSuccess: () => setShowCreate(false),
      onError:   (e) => setActionError(String(e)),
    });
  }

  function handleUpdate(values: { fromPath: string; toPath: string; statusCode: number }) {
    if (!editTarget) return;
    setActionError(null);
    updateMutation.mutate(values, {
      onSuccess: () => setEditTarget(null),
      onError:   (e) => setActionError(String(e)),
    });
  }

  function handleDeactivate(id: number) {
    setActionError(null);
    deactivateMutation.mutate(id, {
      onError: (e) => setActionError(String(e)),
    });
  }

  const totalPages = data ? Math.ceil(data.totalRows / data.pageSize) : 1;

  return (
    <main id="main-content" className="padding-y-4">
      <div className="grid-row grid-gap">
        <div className="grid-col-12">
          {/* Page header */}
          <div className="display-flex flex-align-center flex-justify margin-bottom-3">
            <h1 className="margin-0 font-heading-xl">Redirect Management</h1>
            <button
              type="button"
              className="usa-button"
              onClick={() => { setShowCreate(true); setEditTarget(null); setActionError(null); }}
            >
              + New Redirect
            </button>
          </div>

          {/* Filter tabs */}
          <div className="usa-fieldset margin-bottom-3" role="group" aria-label="Filter redirects">
            {(['all', 'active', 'inactive'] as FilterMode[]).map(f => (
              <label key={f} className="usa-radio__label margin-right-2">
                <input
                  type="radio"
                  className="usa-radio__input"
                  name="redirect-filter"
                  value={f}
                  checked={filter === f}
                  onChange={() => { setFilter(f); setPage(1); }}
                />
                {' '}{f.charAt(0).toUpperCase() + f.slice(1)}
              </label>
            ))}
          </div>

          {/* Action error */}
          {actionError && (
            <div className="usa-alert usa-alert--error usa-alert--slim margin-bottom-2" role="alert">
              <div className="usa-alert__body">
                <p className="usa-alert__text">{actionError}</p>
              </div>
            </div>
          )}

          {/* Create form (inline) */}
          {showCreate && (
            <div className="bg-base-lightest padding-3 border-1px border-base-light radius-md margin-bottom-3">
              <h2 className="font-heading-md margin-top-0">New Redirect</h2>
              <RedirectForm
                onSubmit={handleCreate}
                onCancel={() => { setShowCreate(false); setActionError(null); }}
                isSubmitting={createMutation.isPending}
                submitError={createMutation.isError ? String(createMutation.error) : null}
              />
            </div>
          )}

          {/* Edit form (inline, replaces the row) */}
          {editTarget && (
            <div className="bg-base-lightest padding-3 border-1px border-base-light radius-md margin-bottom-3">
              <h2 className="font-heading-md margin-top-0">Edit Redirect</h2>
              <RedirectForm
                existing={editTarget}
                onSubmit={handleUpdate}
                onCancel={() => { setEditTarget(null); setActionError(null); }}
                isSubmitting={updateMutation.isPending}
                submitError={updateMutation.isError ? String(updateMutation.error) : null}
              />
            </div>
          )}

          {/* Table */}
          {isLoading && <p className="usa-body">Loading…</p>}
          {isError   && (
            <div className="usa-alert usa-alert--error usa-alert--slim" role="alert">
              <div className="usa-alert__body">
                <p className="usa-alert__text">Failed to load redirects.</p>
              </div>
            </div>
          )}
          {data && (
            <>
              <div className="overflow-x-auto">
                <table className="usa-table usa-table--borderless width-full">
                  <thead>
                    <tr>
                      <th scope="col">From Path</th>
                      <th scope="col">To Path</th>
                      <th scope="col">Code</th>
                      <th scope="col">Status</th>
                      <th scope="col">Created By</th>
                      <th scope="col">Created</th>
                      <th scope="col">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.length === 0 && (
                      <tr>
                        <td colSpan={7} className="text-italic text-base">
                          No redirects found.
                        </td>
                      </tr>
                    )}
                    {data.items.map(row => (
                      <tr key={row.id}>
                        <td>
                          <code className="font-code-sm">{row.fromPath}</code>
                        </td>
                        <td>
                          <code className="font-code-sm">{row.toPath}</code>
                        </td>
                        <td>{row.statusCode}</td>
                        <td>
                          <span
                            className={`usa-tag ${row.isActive ? 'bg-success-dark' : 'bg-base'}`}
                            aria-label={row.isActive ? 'Active' : 'Inactive'}
                          >
                            {row.isActive ? 'Active' : 'Inactive'}
                          </span>
                        </td>
                        <td>
                          {row.createdByDisplayName ?? row.createdByEmail ?? '—'}
                        </td>
                        <td>{new Date(row.createdAt).toLocaleDateString()}</td>
                        <td>
                          <div className="display-flex flex-gap-1">
                            <button
                              type="button"
                              className="usa-button usa-button--unstyled"
                              onClick={() => {
                                setEditTarget(row);
                                setShowCreate(false);
                                setActionError(null);
                              }}
                              aria-label={`Edit redirect from ${row.fromPath}`}
                            >
                              Edit
                            </button>
                            {row.isActive && (
                              <button
                                type="button"
                                className="usa-button usa-button--unstyled text-error"
                                onClick={() => handleDeactivate(row.id)}
                                disabled={deactivateMutation.isPending}
                                aria-label={`Deactivate redirect from ${row.fromPath}`}
                              >
                                Deactivate
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Pagination */}
              {totalPages > 1 && (
                <nav aria-label="Pagination" className="usa-pagination margin-top-3">
                  <button
                    type="button"
                    className="usa-button usa-button--outline"
                    onClick={() => setPage(p => Math.max(1, p - 1))}
                    disabled={page <= 1}
                    aria-label="Previous page"
                  >
                    Previous
                  </button>
                  <span className="padding-x-2 font-body-sm">
                    Page {page} of {totalPages} — {data.totalRows} total
                  </span>
                  <button
                    type="button"
                    className="usa-button usa-button--outline"
                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                    disabled={page >= totalPages}
                    aria-label="Next page"
                  >
                    Next
                  </button>
                </nav>
              )}
            </>
          )}
        </div>
      </div>
    </main>
  );
}
