/**
 * MediaLibraryPage — admin media library browser (issue #42).
 *
 * BRD FR-MEDIA-01.
 *
 * Acceptance criteria:
 *   AC1: Grid and list view toggle.
 *   AC2: Search by filename, alt text, tag.
 *   AC3: Filter by MIME type and upload date.
 *   AC4: Click to view detail: preview, alt text, usage list, metadata.
 *   AC5: 'Use this asset' button when opened from content editor (via onSelect prop).
 */

import React, { useState, useCallback, useId } from 'react';
import { useMediaAssets, useMediaDetail, useUpdateMediaMetadata } from './useMediaAssets';
import { MediaUploadForm } from './MediaUploadForm';
import type { MediaAssetSummary, MediaDetailDto } from './mediaTypes';
import { clientSettingKeys, useClientSettings } from '../siteSettings/useClientSettings';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface MediaLibraryPageProps {
  /**
   * When provided, renders a "Use this asset" button in the detail panel.
   * Called when the user selects an asset from the content editor picker.
   */
  onSelect?: (asset: MediaAssetSummary) => void;
}

type ViewMode = 'grid' | 'list';

const MIME_OPTIONS: { label: string; value: string }[] = [
  { label: 'All types',  value: '' },
  { label: 'Images',     value: 'image/' },
  { label: 'PDFs',       value: 'application/pdf' },
  { label: 'Text/CSV',   value: 'text/' },
  { label: 'Documents',  value: 'application/' },
];

// ── Main component ────────────────────────────────────────────────────────────

/**
 * Full-page media library browser with grid/list view, search, MIME filter,
 * and detail side-panel. Designed for the admin SPA at /admin/media.
 */
export function MediaLibraryPage({ onSelect }: MediaLibraryPageProps): JSX.Element {
  const clientSettings = useClientSettings();
  const uploadEnabled = clientSettings.getBool(clientSettingKeys.featureMediaUpload);
  const [viewMode, setViewMode]     = useState<ViewMode>('grid');
  const [search, setSearch]         = useState('');
  const [submittedSearch, setSubmittedSearch] = useState('');
  const [mimeType, setMimeType]     = useState('');
  const [page, setPage]             = useState(1);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const searchInputId  = useId();
  const mimeFilterId   = useId();

  const pageSize = 48;

  // Fetch list
  const { data, isLoading, isError } = useMediaAssets({
    q:        submittedSearch || undefined,
    mimeType: mimeType || undefined,
    page,
    pageSize,
  });

  // Fetch detail when an asset is selected
  const { data: detail, isLoading: detailLoading } = useMediaDetail(selectedId);

  const handleSearchSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      setSubmittedSearch(search);
      setPage(1);
      setSelectedId(null);
    },
    [search],
  );

  const handleMimeChange = useCallback((value: string) => {
    setMimeType(value);
    setPage(1);
    setSelectedId(null);
  }, []);

  const handleSelect = useCallback((asset: MediaAssetSummary) => {
    setSelectedId(asset.id === selectedId ? null : asset.id);
  }, [selectedId]);

  const handleUseAsset = useCallback(() => {
    if (onSelect && detail) {
      onSelect({
        id:               detail.id,
        fileName:         detail.fileName,
        storagePath:      detail.storagePath,
        mimeType:         detail.mimeType,
        fileSizeBytes:    detail.fileSizeBytes,
        altText:          detail.altText,
        title:            detail.title,
        width:            detail.width,
        height:           detail.height,
        webPStoragePath:  detail.webPStoragePath,
        isVirusScanPassed: detail.isVirusScanPassed,
        createdAt:        detail.createdAt,
      });
    }
  }, [onSelect, detail]);

  const totalPages = data ? Math.ceil(data.totalItems / pageSize) : 0;

  return (
    <div className="grid-container" data-testid="media-library-page">
      <h1 className="page-heading">Media Library</h1>

      {/* Upload — POST /api/v1/media/upload (issue #40); hidden while features.mediaUpload is off */}
      {uploadEnabled ? (
        <MediaUploadForm onUploaded={(id) => setSelectedId(id)} />
      ) : (
        <div className="usa-alert usa-alert--info usa-alert--slim margin-bottom-2" data-testid="media-upload-disabled">
          <div className="usa-alert__body">
            <p className="usa-alert__text">Media uploads are currently disabled by a site administrator.</p>
          </div>
        </div>
      )}

      {/* ── Toolbar ─────────────────────────────────────────────────────── */}
      <div className="usa-prose display-flex flex-align-center flex-wrap margin-bottom-2">

        {/* Search */}
        <form
          onSubmit={handleSearchSubmit}
          role="search"
          className="usa-search usa-search--small margin-right-2"
          data-testid="media-search-form"
        >
          <label className="usa-sr-only" htmlFor={searchInputId}>
            Search media assets
          </label>
          <input
            id={searchInputId}
            className="usa-input"
            type="search"
            placeholder="Search filename, alt text…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            data-testid="media-search-input"
          />
          <button
            type="submit"
            className="usa-button"
            data-testid="media-search-button"
          >
            <span className="usa-sr-only">Search</span>
            <svg
              aria-hidden="true"
              focusable="false"
              role="img"
              xmlns="http://www.w3.org/2000/svg"
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <circle cx="11" cy="11" r="8" />
              <line x1="21" y1="21" x2="16.65" y2="16.65" />
            </svg>
          </button>
        </form>

        {/* MIME type filter */}
        <div className="usa-form-group margin-right-2 margin-bottom-0">
          <label className="usa-label usa-sr-only" htmlFor={mimeFilterId}>
            Filter by file type
          </label>
          <select
            id={mimeFilterId}
            className="usa-select"
            value={mimeType}
            onChange={(e) => handleMimeChange(e.target.value)}
            data-testid="media-mime-filter"
          >
            {MIME_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>

        {/* View toggle */}
        <div
          role="group"
          aria-label="View mode"
          className="display-flex flex-align-center"
        >
          <button
            type="button"
            className={`usa-button usa-button--unstyled padding-x-1${viewMode === 'grid' ? ' text-bold' : ''}`}
            aria-pressed={viewMode === 'grid'}
            onClick={() => setViewMode('grid')}
            data-testid="view-toggle-grid"
            title="Grid view"
          >
            <span aria-hidden="true">⊞</span>
            <span className="usa-sr-only">Grid view</span>
          </button>
          <button
            type="button"
            className={`usa-button usa-button--unstyled padding-x-1${viewMode === 'list' ? ' text-bold' : ''}`}
            aria-pressed={viewMode === 'list'}
            onClick={() => setViewMode('list')}
            data-testid="view-toggle-list"
            title="List view"
          >
            <span aria-hidden="true">☰</span>
            <span className="usa-sr-only">List view</span>
          </button>
        </div>
      </div>

      {/* ── Main content area (library + detail) ────────────────────────── */}
      <div className="display-flex flex-gap-4">

        {/* Asset browser */}
        <div className="flex-fill" style={{ minWidth: 0 }}>
          {isLoading && (
            <p className="usa-prose" data-testid="media-loading">
              Loading…
            </p>
          )}

          {isError && (
            <div className="usa-alert usa-alert--error" role="alert" data-testid="media-error">
              <div className="usa-alert__body">
                <p className="usa-alert__text">Failed to load media assets. Please try again.</p>
              </div>
            </div>
          )}

          {!isLoading && !isError && data && data.items.length === 0 && (
            <p className="usa-prose" data-testid="media-empty">
              No assets found.{submittedSearch ? ` Try clearing the search.` : ''}
            </p>
          )}

          {!isLoading && !isError && data && data.items.length > 0 && (
            viewMode === 'grid'
              ? <MediaGrid items={data.items} selectedId={selectedId} onSelect={handleSelect} />
              : <MediaList items={data.items} selectedId={selectedId} onSelect={handleSelect} />
          )}

          {/* Pagination */}
          {totalPages > 1 && (
            <Pagination
              page={page}
              totalPages={totalPages}
              onPage={(p) => { setPage(p); setSelectedId(null); }}
            />
          )}
        </div>

        {/* Detail side panel */}
        {selectedId !== null && (
          <MediaDetailPanel
            isLoading={detailLoading}
            detail={detail ?? null}
            onClose={() => setSelectedId(null)}
            onUseAsset={onSelect ? handleUseAsset : undefined}
          />
        )}
      </div>
    </div>
  );
}

// ── Grid view ─────────────────────────────────────────────────────────────────

interface MediaGridProps {
  items:      MediaAssetSummary[];
  selectedId: number | null;
  onSelect:   (asset: MediaAssetSummary) => void;
}

function MediaGrid({ items, selectedId, onSelect }: MediaGridProps): JSX.Element {
  return (
    <ul
      className="usa-card-group"
      data-testid="media-grid"
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
        gap: '1rem',
        listStyle: 'none',
        padding: 0,
        margin: 0,
      }}
    >
      {items.map((asset) => (
        <li key={asset.id}>
          <MediaCard
            asset={asset}
            isSelected={asset.id === selectedId}
            onSelect={onSelect}
          />
        </li>
      ))}
    </ul>
  );
}

// ── List view ─────────────────────────────────────────────────────────────────

interface MediaListProps {
  items:      MediaAssetSummary[];
  selectedId: number | null;
  onSelect:   (asset: MediaAssetSummary) => void;
}

function MediaList({ items, selectedId, onSelect }: MediaListProps): JSX.Element {
  return (
    <table className="usa-table usa-table--borderless width-full" data-testid="media-list">
      <caption className="usa-sr-only">Media assets</caption>
      <thead>
        <tr>
          <th scope="col">Preview</th>
          <th scope="col">Filename</th>
          <th scope="col">Type</th>
          <th scope="col">Alt Text</th>
          <th scope="col">Size</th>
          <th scope="col">Uploaded</th>
          <th scope="col"><span className="usa-sr-only">Actions</span></th>
        </tr>
      </thead>
      <tbody>
        {items.map((asset) => (
          <tr
            key={asset.id}
            aria-selected={asset.id === selectedId}
            data-testid={`media-list-row-${asset.id}`}
          >
            <td style={{ width: 64 }}>
              <AssetThumbnail asset={asset} size={40} />
            </td>
            <td>{asset.fileName}</td>
            <td>{asset.mimeType}</td>
            <td>{asset.altText ?? <span className="usa-hint">—</span>}</td>
            <td>{formatBytes(asset.fileSizeBytes)}</td>
            <td>{new Date(asset.createdAt).toLocaleDateString()}</td>
            <td>
              <button
                type="button"
                className="usa-button usa-button--unstyled"
                onClick={() => onSelect(asset)}
                data-testid={`media-list-select-${asset.id}`}
                aria-label={`View details for ${asset.fileName}`}
              >
                View
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

// ── Card (grid item) ──────────────────────────────────────────────────────────

interface MediaCardProps {
  asset:      MediaAssetSummary;
  isSelected: boolean;
  onSelect:   (asset: MediaAssetSummary) => void;
}

function MediaCard({ asset, isSelected, onSelect }: MediaCardProps): JSX.Element {
  return (
    <button
      type="button"
      className={`usa-button usa-button--unstyled width-full${isSelected ? ' bg-blue-10' : ''}`}
      style={{
        border: isSelected ? '2px solid #005ea2' : '2px solid #dfe1e2',
        borderRadius: 4,
        padding: '0.5rem',
        textAlign: 'left',
        cursor: 'pointer',
        background: isSelected ? '#e7f0f9' : '#fff',
      }}
      onClick={() => onSelect(asset)}
      aria-pressed={isSelected}
      aria-label={`${asset.fileName}${asset.altText ? ` — ${asset.altText}` : ''}`}
      data-testid={`media-card-${asset.id}`}
    >
      <div style={{ height: 120, display: 'flex', alignItems: 'center', justifyContent: 'center', overflow: 'hidden' }}>
        <AssetThumbnail asset={asset} size={112} />
      </div>
      <p style={{ margin: '0.25rem 0 0', fontSize: '0.8rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
        {asset.fileName}
      </p>
      {!asset.altText && asset.mimeType.startsWith('image/') && (
        <span className="usa-tag usa-tag--new" style={{ fontSize: '0.65rem' }}>No alt text</span>
      )}
    </button>
  );
}

// ── Thumbnail ─────────────────────────────────────────────────────────────────

function AssetThumbnail({ asset, size }: { asset: MediaAssetSummary; size: number }): JSX.Element {
  if (asset.mimeType.startsWith('image/')) {
    const src = asset.webPStoragePath ?? asset.storagePath;
    return (
      <img
        src={`/api/v1/media/serve/${asset.id}`}
        alt={asset.altText ?? ''}
        width={size}
        height={size}
        style={{ objectFit: 'cover', maxWidth: '100%', maxHeight: '100%' }}
        data-testid={`media-thumb-${asset.id}`}
      />
    );
  }

  // Non-image: icon with MIME prefix label
  const label = asset.mimeType.split('/')[1]?.toUpperCase().slice(0, 4) ?? 'FILE';
  return (
    <span
      aria-label={asset.mimeType}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: size,
        height: size,
        background: '#f0f0f0',
        borderRadius: 4,
        fontSize: '0.75rem',
        fontWeight: 'bold',
        color: '#565c65',
      }}
      data-testid={`media-thumb-${asset.id}`}
    >
      {label}
    </span>
  );
}

// ── Detail panel ──────────────────────────────────────────────────────────────

interface MediaDetailPanelProps {
  isLoading:   boolean;
  detail:      MediaDetailDto | null;
  onClose:     () => void;
  /** When present, renders "Use this asset" button (content editor picker flow). */
  onUseAsset?: () => void;
}

/**
 * Detail panel for a selected media asset.
 * Issue #43: shows a required alt text input for images, with a USWDS Alert
 * warning when alt text is missing.
 */
function MediaDetailPanel({
  isLoading,
  detail,
  onClose,
  onUseAsset,
}: MediaDetailPanelProps): JSX.Element {
  const [altTextDraft, setAltTextDraft] = useState<string>('');
  const [altTextEditing, setAltTextEditing] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const altTextInputId    = useId();
  const altTextDescribeId = useId();

  const patchMutation = useUpdateMediaMetadata(detail?.id ?? null);

  const isImage = detail?.mimeType.startsWith('image/') ?? false;
  const missingAltText = isImage && (!detail?.altText || detail.altText.trim() === '');

  const handleEditAltText = useCallback(() => {
    setAltTextDraft(detail?.altText ?? '');
    setAltTextEditing(true);
    setSaveError(null);
  }, [detail?.altText]);

  const handleCancelAltText = useCallback(() => {
    setAltTextEditing(false);
    setSaveError(null);
  }, []);

  const handleSaveAltText = useCallback(async () => {
    if (!detail) return;
    setSaveError(null);
    try {
      await patchMutation.mutateAsync({ altText: altTextDraft.trim() || null });
      setAltTextEditing(false);
    } catch {
      setSaveError('Failed to save alt text. Please try again.');
    }
  }, [detail, altTextDraft, patchMutation]);

  return (
    <aside
      aria-label="Asset details"
      style={{
        width: 320,
        flexShrink: 0,
        border: '1px solid #dfe1e2',
        borderRadius: 4,
        padding: '1rem',
        background: '#fff',
        overflowY: 'auto',
        maxHeight: '80vh',
      }}
      data-testid="media-detail-panel"
    >
      <div className="display-flex flex-justify flex-align-center margin-bottom-2">
        <h2 className="usa-modal__heading margin-0" style={{ fontSize: '1rem' }}>
          Asset Details
        </h2>
        <button
          type="button"
          className="usa-button usa-button--unstyled"
          aria-label="Close details panel"
          onClick={onClose}
          data-testid="media-detail-close"
        >
          ✕
        </button>
      </div>

      {isLoading && <p data-testid="media-detail-loading">Loading…</p>}

      {!isLoading && detail && (
        <>
          {/* Preview */}
          <div
            style={{ marginBottom: '1rem', textAlign: 'center', background: '#f0f0f0', padding: '0.5rem', borderRadius: 4 }}
            data-testid="media-detail-preview"
          >
            {detail.mimeType.startsWith('image/') ? (
              <img
                src={`/api/v1/media/serve/${detail.id}`}
                alt={detail.altText ?? ''}
                style={{ maxWidth: '100%', maxHeight: 200, objectFit: 'contain' }}
              />
            ) : (
              <span style={{ fontSize: '2rem' }}>📄</span>
            )}
          </div>

          {/* ── Issue #43: USWDS Alert when image is missing alt text ──────── */}
          {missingAltText && (
            <div
              className="usa-alert usa-alert--warning usa-alert--slim margin-bottom-2"
              role="alert"
              data-testid="alt-text-required-alert"
            >
              <div className="usa-alert__body">
                <p className="usa-alert__text">
                  This image needs alt text before it can be used in published content.
                </p>
              </div>
            </div>
          )}

          {/* ── Issue #43: Required alt text field for images ──────────────── */}
          {isImage && (
            <div className="usa-form-group margin-bottom-2" data-testid="alt-text-field-group">
              <label className="usa-label" htmlFor={altTextInputId}>
                Alt text
                <abbr title="required" className="usa-required"> *</abbr>
              </label>
              {!altTextEditing ? (
                <>
                  <p
                    id={altTextInputId}
                    data-testid="media-detail-alttext"
                    style={{ marginBottom: '0.25rem' }}
                  >
                    {detail.altText ?? <span className="usa-hint">Not set</span>}
                  </p>
                  <button
                    type="button"
                    className="usa-button usa-button--unstyled"
                    onClick={handleEditAltText}
                    data-testid="alt-text-edit-button"
                    aria-label="Edit alt text"
                  >
                    Edit
                  </button>
                </>
              ) : (
                <>
                  <input
                    id={altTextInputId}
                    className={`usa-input${saveError ? ' usa-input--error' : ''}`}
                    type="text"
                    value={altTextDraft}
                    onChange={(e) => setAltTextDraft(e.target.value)}
                    aria-describedby={saveError ? altTextDescribeId : undefined}
                    aria-required="true"
                    data-testid="alt-text-input"
                    maxLength={500}
                  />
                  {saveError && (
                    <span id={altTextDescribeId} className="usa-error-message" role="alert" data-testid="alt-text-error">
                      {saveError}
                    </span>
                  )}
                  <div className="display-flex flex-gap-2 margin-top-1">
                    <button
                      type="button"
                      className="usa-button usa-button--small"
                      onClick={handleSaveAltText}
                      disabled={patchMutation.isPending}
                      data-testid="alt-text-save-button"
                    >
                      {patchMutation.isPending ? 'Saving…' : 'Save'}
                    </button>
                    <button
                      type="button"
                      className="usa-button usa-button--unstyled usa-button--small"
                      onClick={handleCancelAltText}
                      disabled={patchMutation.isPending}
                      data-testid="alt-text-cancel-button"
                    >
                      Cancel
                    </button>
                  </div>
                </>
              )}
            </div>
          )}

          {/* Metadata table */}
          <dl data-testid="media-detail-metadata">
            <dt className="text-bold">Filename</dt>
            <dd>{detail.fileName}</dd>

            <dt className="text-bold">MIME type</dt>
            <dd>{detail.mimeType}</dd>

            <dt className="text-bold">Size</dt>
            <dd>{formatBytes(detail.fileSizeBytes)}</dd>

            {detail.width && detail.height && (
              <>
                <dt className="text-bold">Dimensions</dt>
                <dd>{detail.width} × {detail.height}px</dd>
              </>
            )}

            {/* Alt text for non-images (no required field, just display) */}
            {!isImage && (
              <>
                <dt className="text-bold">Alt text</dt>
                <dd data-testid="media-detail-alttext">
                  {detail.altText ?? (
                    <span className="usa-hint">Not set</span>
                  )}
                </dd>
              </>
            )}

            {detail.title && (
              <>
                <dt className="text-bold">Title</dt>
                <dd>{detail.title}</dd>
              </>
            )}

            <dt className="text-bold">Uploaded</dt>
            <dd>{new Date(detail.createdAt).toLocaleString()}</dd>

            <dt className="text-bold">Storage backend</dt>
            <dd>{detail.storageBackend}</dd>
          </dl>

          {/* Usage list */}
          <section aria-label="Usage" data-testid="media-detail-usages">
            <h3 style={{ fontSize: '0.9rem' }}>Used in ({detail.usages.length})</h3>
            {detail.usages.length === 0 ? (
              <p className="usa-hint">Not referenced by any content entry.</p>
            ) : (
              <ul className="usa-list usa-list--unstyled">
                {detail.usages.map((u) => (
                  <li
                    key={`${u.contentEntryId}-${u.fieldName}`}
                    style={{ marginBottom: '0.5rem', fontSize: '0.85rem' }}
                    data-testid={`usage-${u.contentEntryId}`}
                  >
                    {/* Issue #44: link to content entry edit page with entry title */}
                    <a
                      href={`/admin/content/${u.contentEntryId}/edit`}
                      className="usa-link"
                      data-testid={`usage-link-${u.contentEntryId}`}
                    >
                      {u.entryTitle || u.slug}
                    </a>
                    <br />
                    <span className="usa-hint">
                      {u.contentTypeName} · {u.status} · field: {u.fieldName}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {/* "Use this asset" button — shown only in picker context */}
          {onUseAsset && (
            <button
              type="button"
              className="usa-button width-full margin-top-2"
              onClick={onUseAsset}
              data-testid="use-asset-button"
            >
              Use this asset
            </button>
          )}
        </>
      )}
    </aside>
  );
}

// ── Pagination ────────────────────────────────────────────────────────────────

function Pagination({
  page,
  totalPages,
  onPage,
}: {
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
}): JSX.Element {
  return (
    <nav aria-label="Pagination" className="usa-pagination margin-top-4" data-testid="media-pagination">
      <ul className="usa-pagination__list">
        <li className="usa-pagination__item">
          <button
            type="button"
            className="usa-pagination__link usa-pagination__previous-page"
            disabled={page <= 1}
            onClick={() => onPage(page - 1)}
            aria-label="Previous page"
            data-testid="pagination-prev"
          >
            ‹ Previous
          </button>
        </li>
        <li className="usa-pagination__item usa-pagination__page-no">
          Page {page} of {totalPages}
        </li>
        <li className="usa-pagination__item">
          <button
            type="button"
            className="usa-pagination__link usa-pagination__next-page"
            disabled={page >= totalPages}
            onClick={() => onPage(page + 1)}
            aria-label="Next page"
            data-testid="pagination-next"
          >
            Next ›
          </button>
        </li>
      </ul>
    </nav>
  );
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
