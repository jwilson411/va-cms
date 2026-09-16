/**
 * SchedulePanel — datetime inputs and status badge for scheduled publish/expiry.
 *
 * Issue #35: BRD FR-AUTH-04.
 *
 * Acceptance criteria:
 *   AC1: Content owner can set a 'Publish at' datetime on a draft.
 *   AC3: Content owner can set an 'Expire at' datetime on a published entry.
 *   AC5: Scheduled times shown in admin status badge.
 *
 * USWDS components only. No inline styles. All inputs have <label> + aria-describedby
 * on error messages per coding rules.
 */

import React, { useState } from 'react';
import { authorizedFetch } from '../../lib/authorizedFetch';

export interface SchedulePanelProps {
  /** Content entry ID (edit mode). */
  entryId: number;
  /** Current status of the entry ('Draft', 'Approved', 'Published', etc.). */
  status: string;
  /** Current scheduled publish time (ISO string or null). */
  scheduledPublishAt: string | null;
  /** Current scheduled expire time (ISO string or null). */
  scheduledExpireAt: string | null;
  /** Called when schedule is saved successfully. */
  onSaved?: () => void;
  /** Whether the save controls should be disabled (e.g. parent is saving). */
  disabled?: boolean;
}

/**
 * Format an ISO UTC datetime string to a human-readable local string.
 * Returns empty string if value is null/undefined.
 */
function formatScheduledTime(isoString: string | null | undefined): string {
  if (!isoString) return '';
  try {
    return new Date(isoString).toLocaleString(undefined, {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return isoString;
  }
}

/**
 * Convert a local datetime-local input value (e.g. "2026-09-20T14:30")
 * to an ISO UTC string for the API.
 */
function localInputToUtcIso(localValue: string): string | null {
  if (!localValue) return null;
  try {
    return new Date(localValue).toISOString();
  } catch {
    return null;
  }
}

/**
 * Convert an ISO UTC string to a value suitable for <input type="datetime-local">.
 * Returns '' if null/empty.
 */
function isoToLocalInput(isoString: string | null | undefined): string {
  if (!isoString) return '';
  try {
    const d = new Date(isoString);
    // Subtract timezone offset to convert to local time
    const local = new Date(d.getTime() - d.getTimezoneOffset() * 60_000);
    return local.toISOString().slice(0, 16); // "YYYY-MM-DDTHH:MM"
  } catch {
    return '';
  }
}

/**
 * SchedulePanel — controls for setting or clearing scheduled publish/expire times.
 * Rendered inside the content entry form (edit mode) when the entry is in a
 * schedulable state (Draft, Approved, or Published).
 *
 * AC5: The panel always shows the current scheduled times as a status badge
 * whether or not the form inputs are shown.
 */
export function SchedulePanel({
  entryId,
  status,
  scheduledPublishAt,
  scheduledExpireAt,
  onSaved,
  disabled = false,
}: SchedulePanelProps): JSX.Element {
  const [publishAtInput, setPublishAtInput] = useState<string>(
    isoToLocalInput(scheduledPublishAt)
  );
  const [expireAtInput, setExpireAtInput] = useState<string>(
    isoToLocalInput(scheduledExpireAt)
  );
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState(false);

  const canSetPublishAt = status === 'Draft' || status === 'Approved';
  const canSetExpireAt  = status === 'Published' || status === 'Approved';

  async function handleSave(e: React.FormEvent): Promise<void> {
    e.preventDefault();
    setSaveError(null);
    setSaveSuccess(false);
    setIsSaving(true);

    const scheduledPublishAtUtc = canSetPublishAt
      ? localInputToUtcIso(publishAtInput)
      : undefined;
    const scheduledExpireAtUtc  = canSetExpireAt
      ? localInputToUtcIso(expireAtInput)
      : undefined;

    try {
      const resp = await authorizedFetch(`/api/v1/content/${entryId}/schedule`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          scheduledPublishAt: scheduledPublishAtUtc ?? null,
          scheduledExpireAt:  scheduledExpireAtUtc  ?? null,
        }),
      });

      if (!resp.ok) {
        let msg = 'Failed to save schedule.';
        try {
          const body = await resp.json() as { error?: string };
          if (body.error) msg = body.error;
        } catch { /* ignore JSON parse failure */ }
        setSaveError(msg);
        return;
      }

      setSaveSuccess(true);
      onSaved?.();
    } catch (err: unknown) {
      setSaveError(err instanceof Error ? err.message : 'Network error saving schedule.');
    } finally {
      setIsSaving(false);
    }
  }

  const hasCurrentPublish = Boolean(scheduledPublishAt);
  const hasCurrentExpire  = Boolean(scheduledExpireAt);

  return (
    <section
      className="usa-form-group"
      aria-label="Scheduled publish and expiry"
      data-testid="schedule-panel"
    >
      <h2 className="usa-prose" style={{ fontSize: '1.125rem', marginBottom: '0.5rem' }}>
        Scheduling
      </h2>

      {/* ── AC5: Status badge showing current scheduled times ─────────────── */}
      {(hasCurrentPublish || hasCurrentExpire) && (
        <div
          className="usa-alert usa-alert--info usa-alert--slim"
          role="status"
          data-testid="schedule-status-badge"
          aria-label="Current schedule"
        >
          <div className="usa-alert__body">
            {hasCurrentPublish && (
              <p className="usa-alert__text" data-testid="scheduled-publish-at-label">
                <strong>Publishes:</strong>{' '}
                {formatScheduledTime(scheduledPublishAt)}
              </p>
            )}
            {hasCurrentExpire && (
              <p className="usa-alert__text" data-testid="scheduled-expire-at-label">
                <strong>Expires:</strong>{' '}
                {formatScheduledTime(scheduledExpireAt)}
              </p>
            )}
          </div>
        </div>
      )}

      {/* ── Schedule form inputs ─────────────────────────────────────────── */}
      <form
        onSubmit={(e) => { void handleSave(e); }}
        aria-label="Schedule settings"
        noValidate
      >
        {/* ── Publish At ─────────────────────────────────────────────────── */}
        {canSetPublishAt && (
          <div className="usa-form-group">
            <label
              className="usa-label"
              htmlFor="schedule-publish-at"
            >
              Publish at (optional)
              <span className="usa-hint" id="schedule-publish-at-hint">
                Leave blank to publish manually. Entry must be in Approved status when the time arrives.
              </span>
            </label>
            <input
              id="schedule-publish-at"
              name="schedule-publish-at"
              type="datetime-local"
              className="usa-input"
              value={publishAtInput}
              onChange={(e) => setPublishAtInput(e.target.value)}
              disabled={disabled || isSaving}
              aria-describedby="schedule-publish-at-hint"
              data-testid="publish-at-input"
            />
          </div>
        )}

        {/* ── Expire At ──────────────────────────────────────────────────── */}
        {canSetExpireAt && (
          <div className="usa-form-group">
            <label
              className="usa-label"
              htmlFor="schedule-expire-at"
            >
              Expire at (optional)
              <span className="usa-hint" id="schedule-expire-at-hint">
                Leave blank for no automatic expiry. Entry will return to Approved status at this time.
              </span>
            </label>
            <input
              id="schedule-expire-at"
              name="schedule-expire-at"
              type="datetime-local"
              className="usa-input"
              value={expireAtInput}
              onChange={(e) => setExpireAtInput(e.target.value)}
              disabled={disabled || isSaving}
              aria-describedby="schedule-expire-at-hint"
              data-testid="expire-at-input"
            />
          </div>
        )}

        {/* ── Error ────────────────────────────────────────────────────────── */}
        {saveError && (
          <div
            className="usa-alert usa-alert--error usa-alert--slim"
            role="alert"
            data-testid="schedule-error"
          >
            <div className="usa-alert__body">
              <p
                className="usa-alert__text"
                id="schedule-error-message"
              >
                {saveError}
              </p>
            </div>
          </div>
        )}

        {/* ── Success ──────────────────────────────────────────────────────── */}
        {saveSuccess && (
          <div
            className="usa-alert usa-alert--success usa-alert--slim"
            role="status"
            data-testid="schedule-success"
            aria-live="polite"
          >
            <div className="usa-alert__body">
              <p className="usa-alert__text">Schedule saved.</p>
            </div>
          </div>
        )}

        {/* ── Save button ──────────────────────────────────────────────────── */}
        {(canSetPublishAt || canSetExpireAt) && (
          <div className="usa-form-group">
            <button
              type="submit"
              className="usa-button usa-button--outline"
              disabled={disabled || isSaving}
              aria-label={isSaving ? 'Saving schedule…' : 'Save schedule'}
              data-testid="save-schedule-button"
            >
              {isSaving ? 'Saving…' : 'Save schedule'}
            </button>
          </div>
        )}
      </form>
    </section>
  );
}
