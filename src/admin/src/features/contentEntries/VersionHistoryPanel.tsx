/**
 * VersionHistoryPanel — issue #32 (FR-AUTH-05).
 *
 * Displays the version history for a content entry with:
 *   - version number, author, date, change note, status columns
 *   - "Restore this version" button per row
 *
 * Uses USWDS 3.x table and button components. No inline styles.
 */

import React from 'react';
import { useContentVersions, useRestoreVersion } from './useContentVersions';

export interface VersionHistoryPanelProps {
  entryId: number;
  /** Called after a successful restore with the new version id. */
  onRestored?: (newVersionId: number) => void;
}

export function VersionHistoryPanel({
  entryId,
  onRestored,
}: VersionHistoryPanelProps): JSX.Element {
  const { data: versions, isLoading, error } = useContentVersions(entryId);
  const restoreMutation = useRestoreVersion(entryId);

  const handleRestore = (versionId: number) => {
    restoreMutation.mutate(versionId, {
      onSuccess: (result) => {
        onRestored?.(result.newVersionId);
      },
    });
  };

  if (isLoading) {
    return (
      <div className="usa-section" aria-live="polite" aria-busy="true">
        <p>Loading version history…</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="usa-alert usa-alert--error" role="alert">
        <div className="usa-alert__body">
          <h3 className="usa-alert__heading">Unable to load version history</h3>
          <p className="usa-alert__text">{error.message}</p>
        </div>
      </div>
    );
  }

  if (!versions || versions.length === 0) {
    return (
      <div className="usa-section">
        <p>No version history yet. Save the entry to create the first version.</p>
      </div>
    );
  }

  return (
    <section aria-label="Version history">
      <h2>Version history</h2>

      {restoreMutation.isError && (
        <div className="usa-alert usa-alert--error" role="alert">
          <div className="usa-alert__body">
            <h3 className="usa-alert__heading">Restore failed</h3>
            <p className="usa-alert__text">{restoreMutation.error?.message}</p>
          </div>
        </div>
      )}

      {restoreMutation.isSuccess && (
        <div className="usa-alert usa-alert--success" role="status">
          <div className="usa-alert__body">
            <p className="usa-alert__text">
              Version restored. A new version has been created with the restored content.
            </p>
          </div>
        </div>
      )}

      <table
        className="usa-table usa-table--borderless width-full"
        aria-label="Content version history"
        data-testid="version-history-table"
      >
        <thead>
          <tr>
            <th scope="col">Version</th>
            <th scope="col">Author</th>
            <th scope="col">Date</th>
            <th scope="col">Change note</th>
            <th scope="col">Status</th>
            <th scope="col">
              <span className="usa-sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {versions.map((v) => (
            <tr key={v.id} data-testid={`version-row-${v.versionNumber}`}>
              <td>{v.versionNumber}</td>
              <td>{v.authorName}</td>
              <td>
                <time dateTime={v.createdAt}>
                  {new Date(v.createdAt).toLocaleString()}
                </time>
              </td>
              <td>{v.changeNote ?? <span className="usa-hint">—</span>}</td>
              <td>{v.status}</td>
              <td>
                <button
                  type="button"
                  className="usa-button usa-button--unstyled"
                  aria-label={`Restore version ${v.versionNumber}`}
                  disabled={restoreMutation.isPending}
                  onClick={() => handleRestore(v.id)}
                  data-testid={`restore-btn-${v.versionNumber}`}
                >
                  Restore this version
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
