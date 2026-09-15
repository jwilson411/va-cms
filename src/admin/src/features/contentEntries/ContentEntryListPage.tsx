import { useState, useCallback } from 'react';
import { useContentEntries, useContentTypesForPicker, useDuplicateEntry } from './useContentEntries';
import type {
  ContentEntryListFilters,
  SortBy,
  SortDir,
  ContentTypeSummaryForPicker,
} from './types';

const STATUS_OPTIONS = ['Draft', 'InReview', 'Approved', 'Published'];

// ── Sub-component: Create New modal (content type picker) ─────────────────────

interface ContentTypePickerModalProps {
  types: ContentTypeSummaryForPicker[];
  isLoading: boolean;
  onClose: () => void;
}

function ContentTypePickerModal({
  types,
  isLoading,
  onClose,
}: ContentTypePickerModalProps): JSX.Element {
  return (
    // Overlay: USWDS modal-style
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="ctp-modal-heading"
      className="usa-modal-overlay"
      style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.5)', zIndex: 1000 }}
    >
      <div
        className="usa-modal usa-modal--lg"
        style={{
          position: 'relative',
          margin: '2rem auto',
          maxWidth: '40rem',
          background: '#fff',
          padding: '2rem',
        }}
      >
        <h2 id="ctp-modal-heading" className="usa-modal__heading">
          Choose a content type
        </h2>

        {isLoading && <p>Loading content types…</p>}

        {!isLoading && types.length === 0 && (
          <p>No content types are available.</p>
        )}

        {!isLoading && types.length > 0 && (
          <ul className="usa-list usa-list--unstyled">
            {types.map((ct) => (
              <li key={ct.id} style={{ marginBottom: '0.75rem' }}>
                <button
                  type="button"
                  className="usa-button usa-button--outline"
                  aria-label={`Create new ${ct.displayName}`}
                  onClick={() => {
                    // Navigation to create form wired in a later story (#30+)
                    onClose();
                  }}
                >
                  {ct.displayName}
                  {ct.description && (
                    <span
                      className="usa-hint"
                      style={{ display: 'block', fontSize: '0.875rem', fontWeight: 'normal' }}
                    >
                      {ct.description}
                    </span>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}

        <button
          type="button"
          className="usa-button usa-button--secondary"
          aria-label="Close content type picker"
          onClick={onClose}
          style={{ marginTop: '1rem' }}
        >
          Cancel
        </button>
      </div>
    </div>
  );
}

// ── Sub-component: Sort header cell ──────────────────────────────────────────

interface SortHeaderProps {
  label: string;
  field: SortBy;
  currentSortBy: SortBy;
  currentSortDir: SortDir;
  onSort: (field: SortBy) => void;
}

function SortHeader({
  label,
  field,
  currentSortBy,
  currentSortDir,
  onSort,
}: SortHeaderProps): JSX.Element {
  const isActive = currentSortBy === field;
  const indicator = isActive ? (currentSortDir === 'ASC' ? ' ▲' : ' ▼') : '';
  return (
    <th
      scope="col"
      className={`usa-table__header--sortable${isActive ? ' usa-table__header--sorted' : ''}`}
      aria-sort={
        isActive
          ? currentSortDir === 'ASC'
            ? 'ascending'
            : 'descending'
          : 'none'
      }
    >
      <button
        type="button"
        className="usa-table__header-button"
        onClick={() => onSort(field)}
        aria-label={`Sort by ${label}`}
      >
        {label}
        {indicator}
      </button>
    </th>
  );
}

// ── Main list component ───────────────────────────────────────────────────────

export function ContentEntryListPage(): JSX.Element {
  // Filter state
  const [filters, setFilters] = useState<ContentEntryListFilters>({});
  const [sortBy,  setSortBy]  = useState<SortBy>('UpdatedAt');
  const [sortDir, setSortDir] = useState<SortDir>('DESC');
  const [page,    setPage]    = useState(1);
  const PAGE_SIZE = 25;

  // Modal state
  const [pickerOpen, setPickerOpen] = useState(false);

  // Duplicate mutation state
  const [duplicateError, setDuplicateError] = useState<string | null>(null);
  const duplicateMutation = useDuplicateEntry();

  const handleDuplicate = useCallback((entryId: number, entryTitle: string) => {
    setDuplicateError(null);
    duplicateMutation.mutate(entryId, {
      onError: (err) => {
        setDuplicateError(
          err instanceof Error ? err.message : 'Failed to duplicate entry.',
        );
      },
    });
  }, [duplicateMutation]);

  // Queries
  const { data, isLoading, isError, error } = useContentEntries({
    filters, sortBy, sortDir, page, pageSize: PAGE_SIZE,
  });
  const { data: contentTypes = [], isLoading: typesLoading } = useContentTypesForPicker();

  // Sort handler: toggle dir if already sorted on this field, else sort DESC
  const handleSort = useCallback((field: SortBy) => {
    if (sortBy === field) {
      setSortDir((d) => (d === 'ASC' ? 'DESC' : 'ASC'));
    } else {
      setSortBy(field);
      setSortDir('DESC');
    }
    setPage(1);
  }, [sortBy]);

  // Filter handler helpers
  const setFilter = useCallback(<K extends keyof ContentEntryListFilters>(
    key: K,
    value: ContentEntryListFilters[K] | undefined,
  ) => {
    setFilters((f) => ({ ...f, [key]: value || undefined }));
    setPage(1);
  }, []);

  const totalPages = data?.totalPages ?? 1;

  return (
    <main id="main-content" className="grid-container">
      <div className="usa-section">

        {/* ── Header row ─────────────────────────────────────────────────── */}
        <div
          style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}
        >
          <h1>Content Entries</h1>
          <button
            type="button"
            className="usa-button"
            aria-label="Create new content entry"
            onClick={() => setPickerOpen(true)}
          >
            Create new
          </button>
        </div>

        {/* ── Filters ────────────────────────────────────────────────────── */}
        <fieldset className="usa-fieldset" aria-label="Filter content entries">
          <legend className="usa-legend usa-legend--large">Filters</legend>
          <div className="usa-form-group" style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>

            {/* Content Type filter */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-content-type">
                Content Type
              </label>
              <select
                id="filter-content-type"
                className="usa-select"
                aria-label="Filter by content type"
                value={filters.contentTypeId ?? ''}
                onChange={(e) =>
                  setFilter('contentTypeId', e.target.value ? Number(e.target.value) : undefined)
                }
              >
                <option value="">All types</option>
                {contentTypes.map((ct) => (
                  <option key={ct.id} value={ct.id}>
                    {ct.displayName}
                  </option>
                ))}
              </select>
            </div>

            {/* Status filter */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-status">
                Status
              </label>
              <select
                id="filter-status"
                className="usa-select"
                aria-label="Filter by status"
                value={filters.status ?? ''}
                onChange={(e) => setFilter('status', e.target.value || undefined)}
              >
                <option value="">All statuses</option>
                {STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>

            {/* Author search filter */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-author">
                Author
              </label>
              <input
                id="filter-author"
                className="usa-input"
                type="search"
                aria-label="Search by author name"
                placeholder="Author name…"
                value={filters.authorSearch ?? ''}
                onChange={(e) => setFilter('authorSearch', e.target.value || undefined)}
              />
            </div>

            {/* Date from */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-date-from">
                Modified from
              </label>
              <input
                id="filter-date-from"
                className="usa-input"
                type="date"
                aria-label="Modified from date"
                value={filters.dateFrom ?? ''}
                onChange={(e) => setFilter('dateFrom', e.target.value || undefined)}
              />
            </div>

            {/* Date to */}
            <div className="usa-form-group">
              <label className="usa-label" htmlFor="filter-date-to">
                Modified to
              </label>
              <input
                id="filter-date-to"
                className="usa-input"
                type="date"
                aria-label="Modified to date"
                value={filters.dateTo ?? ''}
                onChange={(e) => setFilter('dateTo', e.target.value || undefined)}
              />
            </div>

            {/* Clear filters */}
            <div className="usa-form-group">
              <button
                type="button"
                className="usa-button usa-button--outline"
                onClick={() => { setFilters({}); setPage(1); }}
              >
                Clear filters
              </button>
            </div>
          </div>
        </fieldset>

        {/* ── Loading / error states ─────────────────────────────────── */}
        {isLoading && <p>Loading content entries…</p>}

        {(isError || duplicateError) && (
          <div className="usa-alert usa-alert--error" role="alert">
            <div className="usa-alert__body">
              {isError && (
                <>
                  <h2 className="usa-alert__heading">Error loading content entries</h2>
                  <p className="usa-alert__text">
                    {error instanceof Error ? error.message : 'An unexpected error occurred.'}
                  </p>
                </>
              )}
              {duplicateError && !isError && (
                <>
                  <h2 className="usa-alert__heading">Duplicate failed</h2>
                  <p className="usa-alert__text">{duplicateError}</p>
                </>
              )}
            </div>
          </div>
        )}

        {/* ── USWDS Table ────────────────────────────────────────────────── */}
        {!isLoading && !isError && data && (
          <>
            {data.items.length === 0 ? (
              <p>No content entries found.</p>
            ) : (
              <div className="usa-table-container--scrollable" tabIndex={0}>
                <table className="usa-table usa-table--borderless" style={{ width: '100%' }}>
                  <caption className="usa-sr-only">
                    Content entries,{' '}
                    {data.totalRows} total, page {page} of {totalPages}
                  </caption>
                  <thead>
                    <tr>
                      <SortHeader
                        label="Title"
                        field="Title"
                        currentSortBy={sortBy}
                        currentSortDir={sortDir}
                        onSort={handleSort}
                      />
                      <th scope="col">Content Type</th>
                      <th scope="col">Author</th>
                      <SortHeader
                        label="Status"
                        field="Status"
                        currentSortBy={sortBy}
                        currentSortDir={sortDir}
                        onSort={handleSort}
                      />
                      <SortHeader
                        label="Last Modified"
                        field="UpdatedAt"
                        currentSortBy={sortBy}
                        currentSortDir={sortDir}
                        onSort={handleSort}
                      />
                      <th scope="col">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((row) => (
                      <tr key={row.id}>
                        <td>{row.title}</td>
                        <td>{row.contentTypeName}</td>
                        <td>{row.authorDisplayName}</td>
                        <td>
                          <span
                            className={`usa-tag${row.status === 'Published' ? ' usa-tag--green' : ''}`}
                          >
                            {row.status}
                          </span>
                        </td>
                        <td>
                          {new Date(row.lastModified).toLocaleDateString('en-US', {
                            year: 'numeric', month: 'short', day: 'numeric',
                          })}
                        </td>
                        <td>
                          <button
                            type="button"
                            className="usa-button usa-button--unstyled"
                            aria-label={`Edit ${row.title}`}
                          >
                            Edit
                          </button>
                          {' '}
                          <button
                            type="button"
                            className="usa-button usa-button--unstyled"
                            aria-label={`Duplicate ${row.title}`}
                            disabled={duplicateMutation.isPending}
                            onClick={() => handleDuplicate(row.id, row.title)}
                          >
                            Duplicate
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {/* ── USWDS Pagination ─────────────────────────────────────── */}
            {totalPages > 1 && (
              <nav aria-label="Pagination" className="usa-pagination">
                <ul className="usa-pagination__list">
                  <li className="usa-pagination__item usa-pagination__arrow">
                    <button
                      type="button"
                      className="usa-pagination__link usa-pagination__previous-page"
                      aria-label="Previous page"
                      disabled={page <= 1}
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                    >
                      <span className="usa-pagination__link-text" aria-hidden="true">
                        ‹ Previous
                      </span>
                    </button>
                  </li>

                  {/* Page number buttons: show up to 7 pages around current */}
                  {buildPageNumbers(page, totalPages).map((p, i) =>
                    p === '…' ? (
                      <li
                        key={`ellipsis-${i}`}
                        className="usa-pagination__item usa-pagination__overflow"
                        role="presentation"
                        aria-hidden="true"
                      >
                        <span>…</span>
                      </li>
                    ) : (
                      <li key={p} className="usa-pagination__item usa-pagination__page-no">
                        <button
                          type="button"
                          className={`usa-pagination__button${p === page ? ' usa-current' : ''}`}
                          aria-label={`Page ${p}`}
                          aria-current={p === page ? 'page' : undefined}
                          onClick={() => setPage(p as number)}
                        >
                          {p}
                        </button>
                      </li>
                    ),
                  )}

                  <li className="usa-pagination__item usa-pagination__arrow">
                    <button
                      type="button"
                      className="usa-pagination__link usa-pagination__next-page"
                      aria-label="Next page"
                      disabled={page >= totalPages}
                      onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                    >
                      <span className="usa-pagination__link-text" aria-hidden="true">
                        Next ›
                      </span>
                    </button>
                  </li>
                </ul>
                <p className="usa-pagination__status" aria-live="polite">
                  Page {page} of {totalPages} ({data.totalRows} entries)
                </p>
              </nav>
            )}
          </>
        )}
      </div>

      {/* ── Content type picker modal ───────────────────────────────────── */}
      {pickerOpen && (
        <ContentTypePickerModal
          types={contentTypes}
          isLoading={typesLoading}
          onClose={() => setPickerOpen(false)}
        />
      )}
    </main>
  );
}

// ── Pagination helper ─────────────────────────────────────────────────────────

function buildPageNumbers(current: number, total: number): (number | '…')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | '…')[] = [];
  const DELTA = 2;
  const left  = current - DELTA;
  const right = current + DELTA;

  pages.push(1);
  if (left > 2)  pages.push('…');
  for (let p = Math.max(2, left); p <= Math.min(total - 1, right); p++) pages.push(p);
  if (right < total - 1) pages.push('…');
  pages.push(total);
  return pages;
}
