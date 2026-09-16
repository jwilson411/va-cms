/**
 * PreviewButton — issue #34, BRD FR-AUTH-08.
 *
 * Acceptance criteria:
 *  AC1: Preview button renders the current draft in the public template (new tab).
 *  AC2: Preview does not require publishing — uses a signed preview token.
 *  AC3: Preview reflects current unsaved form state (passed via query param).
 *
 * Flow:
 *  1. User clicks Preview while editing.
 *  2. Component POSTs to /api/v1/content/{id}/preview-token to get a signed token.
 *  3. Opens /api/v1/preview?token=...&fields=... in a new tab.
 *     The `fields` param carries the current form state as URL-encoded JSON,
 *     so the preview reflects unsaved changes (AC3).
 *
 * When entryId is undefined (create mode, never saved), shows a tooltip explaining
 * that preview requires at least one save.
 */

import React, { useState, useCallback } from 'react';
import { authorizedFetch } from '../../lib/authorizedFetch';

const PREVIEW_TOKEN_API = (entryId: number) =>
  `/api/v1/content/${entryId}/preview-token`;

const PREVIEW_URL = (token: string, fields: string) =>
  `/api/v1/preview?token=${encodeURIComponent(token)}&fields=${encodeURIComponent(fields)}`;

export interface PreviewButtonProps {
  /** Id of the content entry. undefined = create mode (preview disabled). */
  entryId: number | undefined;
  /**
   * Current field values from the form — may contain unsaved state.
   * Passed to the preview endpoint as URL-encoded JSON (AC3).
   */
  fieldValues: Record<string, unknown>;
  /** Whether the parent form is currently saving. */
  isSaving?: boolean;
  /** Extra CSS class (optional). */
  className?: string;
}

interface PreviewTokenResponse {
  token: string;
  expiresInSeconds: number;
}

/**
 * "Preview" button for the content entry form.
 * Opens a signed preview URL in a new tab with current (possibly unsaved) field values.
 */
export function PreviewButton({
  entryId,
  fieldValues,
  isSaving = false,
  className,
}: PreviewButtonProps): JSX.Element {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError]         = useState<string | null>(null);

  const disabled = entryId === undefined || isSaving || isLoading;

  const handlePreview = useCallback(async () => {
    if (entryId === undefined) return;

    setIsLoading(true);
    setError(null);

    try {
      // AC2: Obtain signed preview token — no publish required.
      const res = await authorizedFetch(PREVIEW_TOKEN_API(entryId), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
      });

      if (!res.ok) {
        throw new Error(`Failed to obtain preview token (HTTP ${res.status})`);
      }

      const { token } = (await res.json()) as PreviewTokenResponse;

      // AC3: Serialize current (possibly unsaved) form state.
      const fields = JSON.stringify(fieldValues);

      // AC1: Open preview in a new tab.
      window.open(PREVIEW_URL(token, fields), '_blank', 'noopener,noreferrer');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Preview failed.';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [entryId, fieldValues]);

  const isNewEntry = entryId === undefined;

  return (
    <div className="usa-form-group">
      {/* AC1: Preview button — USWDS outline button, opens new tab */}
      <button
        type="button"
        className={`usa-button usa-button--outline ${className ?? ''}`.trim()}
        onClick={() => { void handlePreview(); }}
        disabled={disabled}
        aria-label={
          isNewEntry
            ? 'Save the entry before previewing'
            : isLoading
            ? 'Loading preview…'
            : 'Preview this page in a new tab'
        }
        aria-busy={isLoading}
        title={
          isNewEntry
            ? 'Save the entry at least once before previewing'
            : undefined
        }
      >
        {isLoading ? 'Opening preview…' : 'Preview'}
      </button>

      {/* Error message — USWDS error pattern with aria-describedby for accessibility */}
      {error && (
        <p
          id="preview-error"
          className="usa-error-message"
          role="alert"
          aria-live="assertive"
        >
          {error}
        </p>
      )}

      {/* Hint for create mode — explains why Preview is disabled */}
      {isNewEntry && (
        <p className="usa-hint" id="preview-hint">
          Preview is available after you save the entry for the first time.
        </p>
      )}
    </div>
  );
}
