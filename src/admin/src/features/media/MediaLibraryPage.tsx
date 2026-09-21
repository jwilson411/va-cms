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
 *
 * Built from USWDS components only (Card grid, Search, Select, Button group,
 * Alert, Table, Pagination, Icons). No inline styles or ad-hoc glyphs.
 */

import React, { useState, useCallback, useId } from 'react';
import { useMediaAssets, useMediaDetail, useUpdateMediaMetadata } from './useMediaAssets';
import { MediaUploadForm } from './MediaUploadForm';
import type { MediaAssetSummary, MediaDetailDto } from './mediaTypes';
import { clientSettingKeys, useClientSettings } from '../siteSettings/useClientSettings';
import { AuthedImage } from './AuthedImage';
import { Icon } from '../../components/Icon';
import { AdminPagination, RowActions, SortableHeader, useSortableRows } from '../../components/table';

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

function isImage(mimeType: string): boolean {
  return mimeType.startsWith('image/');
}

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
  const viewModeLabelId = useId();

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
  const hasDetail = selectedId !== null;

  return (
    <main id="main-content" data-testid="media-library-page">
      <h1>Media Library</h1>
      <p className="usa-prose">
        Browse, upload, and caption the images and documents used across the site.
      </p>

      {/* Upload — POST /api/v1/media/upload (issue #40); hidden while features.mediaUpload is off */}
      {uploadEnabled ? (
        <MediaUploadForm onUploaded={(id) => setSelectedId(id)} />
      ) : (
        <div className="usa-alert usa-alert--info usa-alert--slim margin-bottom-3" data-testid="media-upload-disabled">
          <div className="usa-alert__body">
            <p className="usa-alert__text">Media uploads are currently disabled by a site administrator.</p>
          </div>
        </div>
      )}

      {/* ── Toolbar: search, filter, view toggle ─────────────────────────── */}
      {/* USWDS grid gives the controls real gutters and spreads them across
          the full width; `grid-gap-*` only spaces `.grid-row` children. */}
      <div className="grid-row grid-gap-2 flex-align-end va-media-toolbar margin-bottom-3">

        {/* Search */}
        <div className="grid-col-12 tablet:grid-col-6">
          <label className="usa-label margin-top-0" htmlFor={searchInputId}>
            Search media
          </label>
          <form
            onSubmit={handleSearchSubmit}
            role="search"
            className="usa-search usa-search--small margin-top-0"
            data-testid="media-search-form"
          >
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
              <Icon name="search" className="usa-search__submit-icon" />
            </button>
          </form>
        </div>

        {/* MIME type filter */}
        <div className="grid-col-6 tablet:grid-col-3">
          <label className="usa-label margin-top-0" htmlFor={mimeFilterId}>
            File type
          </label>
          <select
            id={mimeFilterId}
            className="usa-select width-full"
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

        {/* View toggle — USWDS button group used as a segmented control */}
        <div className="grid-col-6 tablet:grid-col-3">
          <span className="usa-label margin-top-0" id={viewModeLabelId}>View</span>
          <ul className="usa-button-group margin-bottom-0" aria-labelledby={viewModeLabelId}>
            <li className="usa-button-group__item">
              <button
                type="button"
                className={`usa-button${viewMode === 'grid' ? '' : ' usa-button--outline'}`}
                aria-pressed={viewMode === 'grid'}
                onClick={() => setViewMode('grid')}
                data-testid="view-toggle-grid"
              >
                <Icon name="grid_view" size={3} />
                <span className="usa-sr-only">Grid view</span>
              </button>
            </li>
            <li className="usa-button-group__item">
              <button
                type="button"
                className={`usa-button${viewMode === 'list' ? '' : ' usa-button--outline'}`}
                aria-pressed={viewMode === 'list'}
                onClick={() => setViewMode('list')}
                data-testid="view-toggle-list"
              >
                <Icon name="list" size={3} />
                <span className="usa-sr-only">List view</span>
              </button>
            </li>
          </ul>
        </div>
      </div>

      {/* ── Main content area (library + detail) ────────────────────────── */}
      <div className="grid-row grid-gap-3">

        {/* Asset browser */}
        <div className={hasDetail ? 'grid-col-12 tablet:grid-col-8' : 'grid-col-12'}>
          {isLoading && (
            <p className="usa-prose" data-testid="media-loading" aria-live="polite" aria-busy="true">
              Loading media…
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
            <p className="usa-prose text-base" data-testid="media-empty">
              No assets found.{submittedSearch ? ' Try clearing the search.' : ''}
            </p>
          )}

          {!isLoading && !isError && data && data.items.length > 0 && (
            viewMode === 'grid'
              ? <MediaGrid items={data.items} selectedId={selectedId} onSelect={handleSelect} />
              : <MediaList items={data.items} selectedId={selectedId} onSelect={handleSelect} />
          )}

          {/* Pagination */}
          {totalPages > 1 && (
            <AdminPagination
              page={page}
              totalPages={totalPages}
              onPage={(p) => { setPage(p); setSelectedId(null); }}
              ariaLabel="Media pagination"
              totalRows={data?.totalItems}
              itemLabel="assets"
            />
          )}
        </div>

        {/* Detail side panel */}
        {hasDetail && (
          <MediaDetailPanel
            isLoading={detailLoading}
            detail={detail ?? null}
            onClose={() => setSelectedId(null)}
            onUseAsset={onSelect ? handleUseAsset : undefined}
          />
        )}
      </div>
    </main>
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
    <ul className="usa-card-group" data-testid="media-grid">
      {items.map((asset) => (
        <li key={asset.id} className="usa-card tablet:grid-col-6 desktop:grid-col-4">
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

type MediaSortKey = 'fileName' | 'mimeType' | 'altText' | 'fileSizeBytes' | 'createdAt';

function mediaSortValue(asset: MediaAssetSummary, key: MediaSortKey): string | number {
  if (key === 'altText') return asset.altText ?? '';
  return asset[key];
}

function MediaList({ items, selectedId, onSelect }: MediaListProps): JSX.Element {
  const {
    rows: sortedItems,
    sortKey,
    sortDirection,
    toggleSort,
  } = useSortableRows<MediaAssetSummary, MediaSortKey>(items, {
    initialKey: 'fileName',
    getValue: mediaSortValue,
  });

  return (
    <div className="usa-table-container--scrollable" tabIndex={0}>
      <table className="usa-table usa-table--borderless width-full" data-testid="media-list">
        <caption className="usa-sr-only">Media assets</caption>
        <thead>
          <tr>
            <th scope="col">Preview</th>
            <SortableHeader label="Filename" field="fileName" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Type" field="mimeType" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Alt Text" field="altText" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Size" field="fileSizeBytes" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Uploaded" field="createdAt" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <th scope="col"><span className="usa-sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {sortedItems.map((asset) => (
            <tr
              key={asset.id}
              aria-selected={asset.id === selectedId}
              data-testid={`media-list-row-${asset.id}`}
            >
              <td className="va-media-list__thumb">
                <AssetThumbnail asset={asset} size={40} />
              </td>
              <td>
                <button
                  type="button"
                  className="usa-button usa-button--unstyled"
                  onClick={() => onSelect(asset)}
                  aria-label={`Open ${asset.fileName}`}
                >
                  {asset.fileName}
                </button>
              </td>
              <td>{asset.mimeType}</td>
              <td>{asset.altText ?? <span className="usa-hint">—</span>}</td>
              <td>{formatBytes(asset.fileSizeBytes)}</td>
              <td>{new Date(asset.createdAt).toLocaleDateString()}</td>
              <td>
                <RowActions>
                  <button
                    type="button"
                    className="usa-button usa-button--unstyled"
                    onClick={() => onSelect(asset)}
                    data-testid={`media-list-select-${asset.id}`}
                    aria-label={`View details for ${asset.fileName}`}
                  >
                    View
                  </button>
                </RowActions>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Card (grid item) ──────────────────────────────────────────────────────────

interface MediaCardProps {
  asset:      MediaAssetSummary;
  isSelected: boolean;
  onSelect:   (asset: MediaAssetSummary) => void;
}

/**
 * USWDS Card styling on a button: the framework has no selectable-card variant,
 * so `.va-media-card` adds the interactive states (hover, focus, selected) while
 * `usa-card__*` supplies the surface, media, and body layout.
 */
function MediaCard({ asset, isSelected, onSelect }: MediaCardProps): JSX.Element {
  return (
    <button
      type="button"
      className={`usa-card__container va-media-card${isSelected ? ' va-media-card--selected' : ''}`}
      onClick={() => onSelect(asset)}
      aria-pressed={isSelected}
      aria-label={`${asset.fileName}${asset.altText ? ` — ${asset.altText}` : ''}`}
      data-testid={`media-card-${asset.id}`}
    >
      <span className="usa-card__media va-media-card__media">
        <AssetThumbnail asset={asset} size={112} />
      </span>
      <span className="usa-card__body va-media-card__body">
        <span className="va-media-card__name">{asset.fileName}</span>
        {!asset.altText && isImage(asset.mimeType) && (
          <span className="usa-tag usa-tag--new margin-top-1">No alt text</span>
        )}
      </span>
    </button>
  );
}

// ── Thumbnail ─────────────────────────────────────────────────────────────────

function AssetThumbnail({ asset, size }: { asset: MediaAssetSummary; size: number }): JSX.Element {
  if (isImage(asset.mimeType)) {
    return (
      <AuthedImage
        assetId={asset.id}
        alt={asset.altText ?? ''}
        width={size}
        height={size}
        className="va-media-thumb__img"
        data-testid={`media-thumb-${asset.id}`}
      />
    );
  }

  // Non-image: USWDS file icon with the MIME subtype as a short label.
  const ext = asset.mimeType.split('/')[1]?.toUpperCase().slice(0, 4) ?? 'FILE';
  return (
    <span
      role="img"
      aria-label={asset.mimeType}
      className="va-media-thumb va-media-thumb--file"
      data-testid={`media-thumb-${asset.id}`}
    >
      <Icon name="file_present" size={6} />
      <span className="va-media-thumb__ext">{ext}</span>
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

  const image = detail ? isImage(detail.mimeType) : false;
  const missingAltText = image && (!detail?.altText || detail.altText.trim() === '');

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
      className="grid-col-12 tablet:grid-col-4 va-media-detail"
      data-testid="media-detail-panel"
    >
      <div className="va-media-detail__header">
        <h2 className="font-heading-md margin-0">Asset details</h2>
        <button
          type="button"
          className="usa-button usa-button--unstyled"
          aria-label="Close details panel"
          onClick={onClose}
          data-testid="media-detail-close"
        >
          <Icon name="close" size={3} />
        </button>
      </div>

      {isLoading && (
        <p data-testid="media-detail-loading" aria-live="polite" aria-busy="true">
          Loading…
        </p>
      )}

      {!isLoading && detail && (
        <>
          {/* Preview */}
          <div className="va-media-detail__preview" data-testid="media-detail-preview">
            {isImage(detail.mimeType) ? (
              <AuthedImage
                assetId={detail.id}
                alt={detail.altText ?? ''}
                className="va-media-detail__preview-img"
              />
            ) : (
              <Icon name="file_present" size={8} className="text-base" />
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
          {image && (
            <div className="usa-form-group" data-testid="alt-text-field-group">
              <label className="usa-label margin-top-0" htmlFor={altTextInputId}>
                Alt text
                <abbr title="required" className="usa-hint--required"> *</abbr>
              </label>
              {!altTextEditing ? (
                <>
                  <p id={altTextInputId} data-testid="media-detail-alttext">
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
                  <RowActions className="margin-top-1">
                    <button
                      type="button"
                      className="usa-button"
                      onClick={handleSaveAltText}
                      disabled={patchMutation.isPending}
                      data-testid="alt-text-save-button"
                    >
                      {patchMutation.isPending ? 'Saving…' : 'Save'}
                    </button>
                    <button
                      type="button"
                      className="usa-button usa-button--unstyled"
                      onClick={handleCancelAltText}
                      disabled={patchMutation.isPending}
                      data-testid="alt-text-cancel-button"
                    >
                      Cancel
                    </button>
                  </RowActions>
                </>
              )}
            </div>
          )}

          {/* Metadata */}
          <dl className="va-media-detail__meta" data-testid="media-detail-metadata">
            <dt>Filename</dt>
            <dd>{detail.fileName}</dd>

            <dt>MIME type</dt>
            <dd>{detail.mimeType}</dd>

            <dt>Size</dt>
            <dd>{formatBytes(detail.fileSizeBytes)}</dd>

            {detail.width && detail.height && (
              <>
                <dt>Dimensions</dt>
                <dd>{detail.width} × {detail.height}px</dd>
              </>
            )}

            {/* Alt text for non-images (no required field, just display) */}
            {!image && (
              <>
                <dt>Alt text</dt>
                <dd data-testid="media-detail-alttext">
                  {detail.altText ?? <span className="usa-hint">Not set</span>}
                </dd>
              </>
            )}

            {detail.title && (
              <>
                <dt>Title</dt>
                <dd>{detail.title}</dd>
              </>
            )}

            <dt>Uploaded</dt>
            <dd>{new Date(detail.createdAt).toLocaleString()}</dd>

            <dt>Storage backend</dt>
            <dd>{detail.storageBackend}</dd>
          </dl>

          {/* Usage list */}
          <section aria-labelledby="media-usage-heading" data-testid="media-detail-usages">
            <h3 id="media-usage-heading" className="font-heading-sm margin-bottom-1">
              Used in ({detail.usages.length})
            </h3>
            {detail.usages.length === 0 ? (
              <p className="usa-hint">Not referenced by any content entry.</p>
            ) : (
              <ul className="usa-list usa-list--unstyled va-media-detail__usage">
                {detail.usages.map((u) => (
                  <li
                    key={`${u.contentEntryId}-${u.fieldName}`}
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
                    <span className="usa-hint display-block">
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

// ── Utilities ─────────────────────────────────────────────────────────────────

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
