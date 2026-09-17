/**
 * AuditLogPage — Admin audit log viewer (issue #57, BRD FR-USERS-06).
 *
 * Route: /admin/audit
 *
 * Features:
 *   - Filterable table: User (actorId), Action Type, Entity Type, Date Range
 *   - Paged list of AuditLog rows, newest first
 *   - Export filtered results to CSV
 *
 * Accessibility:
 *   - All inputs have <label> elements.
 *   - aria-describedby on error messages.
 *   - No inline styles. USWDS 3.x only.
 */

import React, { useState } from 'react';
import { useAuditLog, buildExportUrl, type AuditLogFilters } from './useAuditLog';
import { clientSettingKeys, useClientSettings } from '../siteSettings/useClientSettings';


interface FilterState {
  actorId: string;
  action: string;
  entityType: string;
  fromDate: string;
  toDate: string;
}

const EMPTY_FILTERS: FilterState = {
  actorId: '',
  action: '',
  entityType: '',
  fromDate: '',
  toDate: '',
};

function filtersToQuery(f: FilterState): AuditLogFilters {
  const q: AuditLogFilters = {};
  if (f.actorId.trim())   q.actorId   = Number(f.actorId.trim());
  if (f.action.trim())    q.action    = f.action.trim();
  if (f.entityType.trim()) q.entityType = f.entityType.trim();
  if (f.fromDate.trim())  q.fromDate  = f.fromDate.trim();
  if (f.toDate.trim())    q.toDate    = f.toDate.trim();
  return q;
}

export function AuditLogPage(): JSX.Element {
  // Rows per page: admin.auditLogPageSize (site setting, default 50)
  const clientSettings = useClientSettings();
  const PAGE_SIZE = Math.max(1, clientSettings.getInt(clientSettingKeys.adminAuditLogPageSize));
  const [draft, setDraft]       = useState<FilterState>(EMPTY_FILTERS);
  const [applied, setApplied]   = useState<AuditLogFilters>({});
  const [page, setPage]         = useState(1);

  const { data, isLoading, isError } = useAuditLog(applied, page, PAGE_SIZE);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalItems / PAGE_SIZE)) : 1;

  function handleApply(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setApplied(filtersToQuery(draft));
    setPage(1);
  }

  function handleClear() {
    setDraft(EMPTY_FILTERS);
    setApplied({});
    setPage(1);
  }

  function handleFieldChange(field: keyof FilterState) {
    return (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
      setDraft((prev) => ({ ...prev, [field]: e.target.value }));
    };
  }

  const exportUrl = buildExportUrl(applied);

  return (
    <main id="main-content" className="grid-container">
      <h1>Audit Log</h1>
      <p className="usa-prose">
        Immutable log of all system mutations. Filter by user, action, entity type, or date range.
        Exports up to 1,000 rows.
      </p>

      {/* ── Filter form ─────────────────────────────────────────────────────── */}
      <form
        onSubmit={handleApply}
        className="usa-form margin-bottom-4"
        aria-label="Audit log filters"
        id="audit-filter-form"
      >
        <div className="grid-row grid-gap">
          {/* Actor ID */}
          <div className="grid-col-12 tablet:grid-col-3">
            <label className="usa-label" htmlFor="audit-actor-id">
              User ID
            </label>
            <input
              id="audit-actor-id"
              className="usa-input"
              type="number"
              min="1"
              value={draft.actorId}
              onChange={handleFieldChange('actorId')}
              placeholder="e.g. 42"
              aria-describedby="audit-actor-id-hint"
            />
            <span id="audit-actor-id-hint" className="usa-hint">
              Filter by actor user ID
            </span>
          </div>

          {/* Action Type */}
          <div className="grid-col-12 tablet:grid-col-3">
            <label className="usa-label" htmlFor="audit-action">
              Action Type
            </label>
            <input
              id="audit-action"
              className="usa-input"
              type="text"
              value={draft.action}
              onChange={handleFieldChange('action')}
              placeholder="e.g. Publish"
              aria-describedby="audit-action-hint"
            />
            <span id="audit-action-hint" className="usa-hint">
              Exact action string (e.g. Publish, Deactivate)
            </span>
          </div>

          {/* Entity Type */}
          <div className="grid-col-12 tablet:grid-col-3">
            <label className="usa-label" htmlFor="audit-entity-type">
              Entity Type
            </label>
            <input
              id="audit-entity-type"
              className="usa-input"
              type="text"
              value={draft.entityType}
              onChange={handleFieldChange('entityType')}
              placeholder="e.g. ContentEntry"
              aria-describedby="audit-entity-type-hint"
            />
            <span id="audit-entity-type-hint" className="usa-hint">
              Exact entity type (e.g. ContentEntry, User)
            </span>
          </div>
        </div>

        <div className="grid-row grid-gap margin-top-2">
          {/* From Date */}
          <div className="grid-col-12 tablet:grid-col-3">
            <label className="usa-label" htmlFor="audit-from-date">
              From date
            </label>
            <input
              id="audit-from-date"
              className="usa-input"
              type="datetime-local"
              value={draft.fromDate}
              onChange={handleFieldChange('fromDate')}
              aria-describedby="audit-from-date-hint"
            />
            <span id="audit-from-date-hint" className="usa-hint">
              Include rows on or after this date/time
            </span>
          </div>

          {/* To Date */}
          <div className="grid-col-12 tablet:grid-col-3">
            <label className="usa-label" htmlFor="audit-to-date">
              To date
            </label>
            <input
              id="audit-to-date"
              className="usa-input"
              type="datetime-local"
              value={draft.toDate}
              onChange={handleFieldChange('toDate')}
              aria-describedby="audit-to-date-hint"
            />
            <span id="audit-to-date-hint" className="usa-hint">
              Include rows on or before this date/time
            </span>
          </div>
        </div>

        <div className="margin-top-3 display-flex flex-align-center flex-wrap gap-2">
          <button type="submit" className="usa-button margin-right-1">
            Apply filters
          </button>
          <button
            type="button"
            className="usa-button usa-button--unstyled margin-right-3"
            onClick={handleClear}
          >
            Clear filters
          </button>
          <a
            href={exportUrl}
            className="usa-button usa-button--outline"
            download="audit-log.csv"
            aria-label="Export filtered audit log to CSV"
          >
            Export CSV
          </a>
        </div>
      </form>

      {/* ── Loading / error states ───────────────────────────────────────────── */}
      {isLoading && (
        <p className="usa-prose" aria-live="polite" aria-busy="true">
          Loading audit log…
        </p>
      )}

      {isError && (
        <div
          className="usa-alert usa-alert--error"
          role="alert"
          id="audit-error-message"
        >
          <div className="usa-alert__body">
            <p className="usa-alert__text" aria-describedby="audit-error-message">
              Failed to load the audit log. Please refresh the page.
            </p>
          </div>
        </div>
      )}

      {/* ── Results summary ──────────────────────────────────────────────────── */}
      {!isLoading && !isError && data && (
        <p className="usa-prose text-base margin-bottom-2" aria-live="polite">
          {data.totalItems === 0
            ? 'No audit log entries match the current filters.'
            : `Showing ${(page - 1) * PAGE_SIZE + 1}–${Math.min(page * PAGE_SIZE, data.totalItems)} of ${data.totalItems.toLocaleString()} entries`}
        </p>
      )}

      {/* ── Audit log table ──────────────────────────────────────────────────── */}
      {!isLoading && !isError && data && data.items.length > 0 && (
        <div className="usa-table-container--scrollable" tabIndex={0}>
          <table
            className="usa-table usa-table--striped usa-table--compact usa-table--scrollable"
            aria-label="Audit log entries"
          >
            <thead>
              <tr>
                <th scope="col">Date/Time</th>
                <th scope="col">Actor</th>
                <th scope="col">Entity Type</th>
                <th scope="col">Entity ID</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((row) => (
                <tr key={row.id}>
                  <td>
                    <time dateTime={row.createdAt}>
                      {new Date(row.createdAt).toLocaleString()}
                    </time>
                  </td>
                  <td>
                    {row.actorDisplayName ?? row.actorEmail ?? (
                      <span className="text-base">System</span>
                    )}
                  </td>
                  <td>{row.entityType}</td>
                  <td>{row.entityId}</td>
                  <td>{row.action}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── Pagination ───────────────────────────────────────────────────────── */}
      {!isLoading && !isError && data && data.totalItems > PAGE_SIZE && (
        <nav aria-label="Audit log pagination" className="usa-pagination margin-top-3">
          <ul className="usa-pagination__list">
            <li className="usa-pagination__item usa-pagination__arrow">
              <button
                type="button"
                className="usa-pagination__link usa-pagination__previous-page"
                aria-label="Previous page"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                <span aria-hidden="true">«</span>
                <span className="usa-pagination__link-text">Previous</span>
              </button>
            </li>

            <li className="usa-pagination__item usa-pagination__page-no" aria-current="page">
              <span className="usa-pagination__button usa-current" aria-label={`Page ${page} of ${totalPages}`}>
                {page} / {totalPages}
              </span>
            </li>

            <li className="usa-pagination__item usa-pagination__arrow">
              <button
                type="button"
                className="usa-pagination__link usa-pagination__next-page"
                aria-label="Next page"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              >
                <span className="usa-pagination__link-text">Next</span>
                <span aria-hidden="true">»</span>
              </button>
            </li>
          </ul>
        </nav>
      )}
    </main>
  );
}
