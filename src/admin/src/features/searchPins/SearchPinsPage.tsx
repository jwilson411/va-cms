/**
 * SearchPinsPage — Admin page for managing pinned search results (issue #52, FR-SEARCH-04).
 *
 * Route: /admin/search/pins
 *
 * Features:
 *   - Table of all pinned results showing: query string, linked content entry title/slug, status, created date
 *   - "Add pin" form: query string + content entry ID
 *   - Delete button on each row
 *
 * Acceptance criteria:
 *   - Admin can add a pinned result for a specific query string → content entry.
 *   - Pinned results are managed via this table.
 *
 * Accessibility:
 *   - All form inputs have <label> elements.
 *   - aria-describedby on error messages.
 *   - No inline styles, no Tailwind, no MUI.
 *   - USWDS 3.x components exclusively.
 */

import React, { useState } from 'react';
import { useSearchPins, useCreateSearchPin, useDeleteSearchPin } from './useSearchPins';

export function SearchPinsPage(): JSX.Element {
  const { data, isLoading, isError } = useSearchPins();
  const createPin  = useCreateSearchPin();
  const deletePin  = useDeleteSearchPin();

  const [queryString,    setQueryString]    = useState('');
  const [contentEntryId, setContentEntryId] = useState('');
  const [formError,      setFormError]      = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);

  function handleAdd(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setFormError(null);

    if (!queryString.trim()) {
      setFormError('Query string is required.');
      return;
    }
    const entryId = parseInt(contentEntryId, 10);
    if (!entryId || entryId <= 0) {
      setFormError('Content entry ID must be a positive number.');
      return;
    }

    createPin.mutate(
      { queryString: queryString.trim(), contentEntryId: entryId },
      {
        onSuccess: () => {
          setQueryString('');
          setContentEntryId('');
        },
        onError: (err: Error) => {
          setFormError(err.message);
        },
      },
    );
  }

  function handleDeleteClick(id: number) {
    setConfirmDeleteId(id);
  }

  function handleDeleteConfirm() {
    if (confirmDeleteId === null) return;
    deletePin.mutate(confirmDeleteId, {
      onSuccess: () => setConfirmDeleteId(null),
      onError: () => setConfirmDeleteId(null),
    });
  }

  return (
    <main id="main-content">
      <h1>Pinned Search Results</h1>
      <p className="usa-prose">
        Pin a content entry to the top of search results for a specific query string. Each
        query string can have one pinned result. Pinned results appear with a{' '}
        <strong>Featured result</strong> label in search.
      </p>

      {/* ── Add pin form ───────────────────────────────────────────────────── */}
      <section aria-labelledby="add-pin-heading" className="usa-card margin-bottom-4">
        <div className="usa-card__header">
          <h2 className="usa-card__heading" id="add-pin-heading">
            Add Pinned Result
          </h2>
        </div>
        <div className="usa-card__body">
          <form onSubmit={handleAdd} noValidate>
            {/* Query string */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="pin-query-string">
                Query string <abbr title="required" className="usa-required"> *</abbr>
              </label>
              <span className="usa-hint" id="pin-query-string-hint">
                The exact search term that will trigger this pinned result (case-insensitive).
              </span>
              <input
                id="pin-query-string"
                className="usa-input"
                type="text"
                value={queryString}
                onChange={(e) => setQueryString(e.target.value)}
                aria-describedby="pin-query-string-hint"
                aria-required="true"
                autoComplete="off"
                maxLength={500}
              />
            </div>

            {/* Content entry ID */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="pin-entry-id">
                Content entry ID <abbr title="required" className="usa-required"> *</abbr>
              </label>
              <span className="usa-hint" id="pin-entry-id-hint">
                The numeric ID of the content entry to pin. The entry must be Published.
              </span>
              <input
                id="pin-entry-id"
                className="usa-input usa-input--sm"
                type="number"
                min="1"
                value={contentEntryId}
                onChange={(e) => setContentEntryId(e.target.value)}
                aria-describedby="pin-entry-id-hint"
                aria-required="true"
              />
            </div>

            {/* Form-level error */}
            {formError && (
              <div
                className="usa-alert usa-alert--error usa-alert--slim margin-bottom-2"
                role="alert"
                id="add-pin-error"
              >
                <div className="usa-alert__body">
                  <p className="usa-alert__text">{formError}</p>
                </div>
              </div>
            )}

            <button
              type="submit"
              className="usa-button"
              disabled={createPin.isPending}
              aria-busy={createPin.isPending}
            >
              {createPin.isPending ? 'Saving…' : 'Add pin'}
            </button>
          </form>
        </div>
      </section>

      {/* ── Delete confirmation ────────────────────────────────────────────── */}
      {confirmDeleteId !== null && (
        <div
          className="usa-alert usa-alert--warning margin-bottom-2"
          role="alertdialog"
          aria-modal="false"
          aria-labelledby="delete-confirm-heading"
        >
          <div className="usa-alert__body">
            <h4 className="usa-alert__heading" id="delete-confirm-heading">
              Remove pinned result?
            </h4>
            <p className="usa-alert__text">
              Are you sure you want to remove this pinned result? The content entry will no
              longer appear featured for this query.
            </p>
            <button
              type="button"
              className="usa-button usa-button--secondary margin-right-1"
              onClick={handleDeleteConfirm}
              aria-label="Confirm remove pinned result"
            >
              Remove
            </button>
            <button
              type="button"
              className="usa-button usa-button--unstyled"
              onClick={() => setConfirmDeleteId(null)}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* ── Pins table ────────────────────────────────────────────────────── */}
      {isLoading && (
        <p className="usa-prose" aria-live="polite" aria-busy="true">
          Loading pinned results…
        </p>
      )}

      {isError && (
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <p className="usa-alert__text">
              Failed to load pinned results. Please refresh the page.
            </p>
          </div>
        </div>
      )}

      {!isLoading && !isError && data && (
        <>
          {data.items.length === 0 ? (
            <p className="usa-prose">No pinned results yet. Use the form above to add one.</p>
          ) : (
            <table
              className="usa-table usa-table--striped usa-table--compact usa-table--scrollable"
              aria-label="Pinned search results"
            >
              <thead>
                <tr>
                  <th scope="col">Query string</th>
                  <th scope="col">Content entry</th>
                  <th scope="col">Status</th>
                  <th scope="col">Pinned on</th>
                  <th scope="col">
                    <span className="usa-sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((pin) => (
                  <tr key={pin.id}>
                    <td>
                      <code>{pin.queryString}</code>
                    </td>
                    <td>
                      {pin.entryTitle ?? <em>Untitled</em>}
                      {pin.entrySlug && (
                        <span className="font-body-xs display-block text-base">
                          /{pin.entrySlug}
                        </span>
                      )}
                    </td>
                    <td>
                      <span
                        className={
                          pin.entryStatus === 'Published'
                            ? 'usa-tag bg-green-warm-50 text-green-warm-70'
                            : 'usa-tag'
                        }
                      >
                        {pin.entryStatus ?? '—'}
                      </span>
                    </td>
                    <td>{new Date(pin.createdAt).toLocaleDateString()}</td>
                    <td>
                      <button
                        type="button"
                        className="usa-button usa-button--unstyled text-error"
                        onClick={() => handleDeleteClick(pin.id)}
                        aria-label={`Remove pin for query "${pin.queryString}"`}
                      >
                        Remove
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
