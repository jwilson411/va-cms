/**
 * ContentEntryFormPage — create/edit form for a content entry (issue #30).
 *
 * Acceptance criteria:
 *  - Form renders all field types for the selected content type
 *  - USWDS form components used for all inputs (usa-input, usa-label, usa-error-message, usa-hint)
 *  - Required fields marked with asterisk
 *  - Inline validation fires on blur
 *  - Auto-save fires every 60 seconds and shows 'Last saved at HH:MM' indicator
 *  - Issue #33: Slug auto-populates from title; editable; shows URL preview; duplicate error surfaced
 */

import React from 'react';
import { useContentEntryForm } from './useContentEntryForm';
import { FieldRenderer } from './FieldRenderers';
import { SlugField } from './SlugField';
import { PreviewButton } from './PreviewButton';
import { WorkflowActions } from './WorkflowActions';

export interface ContentEntryFormPageProps {
  /**
   * Content type machine name (e.g. "standard_page").
   * Required — determines which fields are rendered.
   */
  contentTypeName: string;
  /**
   * Entry id for edit mode. Undefined = create mode.
   */
  entryId?: number;
  /**
   * Optional display label for the content type (shown in heading).
   */
  contentTypeDisplayName?: string;
  /**
   * Called after a successful create with the new entry id.
   */
  onCreated?: (id: number) => void;
  /**
   * Called when the user wants to cancel and return to the list.
   */
  onCancel?: () => void;
}

/**
 * Create/edit form for a content entry. Uses USWDS components for all inputs.
 * Auto-saves in edit mode every 60 s and shows a "Last saved at HH:MM" indicator.
 */
export function ContentEntryFormPage({
  contentTypeName,
  entryId,
  contentTypeDisplayName,
  onCreated,
  onCancel,
}: ContentEntryFormPageProps): JSX.Element {
  const isEditMode = entryId !== undefined;

  const {
    fieldDefs,
    isSchemaLoading,
    schemaError,
    fieldValues,
    setFieldValue,
    validationErrors,
    validateOnBlur,
    slug,
    setSlug,
    slugError,
    isSaving,
    saveError,
    lastSavedAt,
    handleSave,
    hasErrors,
  } = useContentEntryForm({
    contentTypeName,
    entryId,
    onCreated,
  });

  const heading = isEditMode
    ? `Edit ${contentTypeDisplayName ?? contentTypeName}`
    : `New ${contentTypeDisplayName ?? contentTypeName}`;

  // ── Loading schema ─────────────────────────────────────────────────────────

  if (isSchemaLoading) {
    return (
      <main id="main-content" className="grid-container">
        <div className="usa-section">
          <p>Loading form…</p>
        </div>
      </main>
    );
  }

  // ── Schema error ───────────────────────────────────────────────────────────

  if (schemaError) {
    return (
      <main id="main-content" className="grid-container">
        <div className="usa-section">
          <div className="usa-alert usa-alert--error" role="alert">
            <div className="usa-alert__body">
              <h2 className="usa-alert__heading">Unable to load form</h2>
              <p className="usa-alert__text">{schemaError.message}</p>
            </div>
          </div>
        </div>
      </main>
    );
  }

  // ── Form ───────────────────────────────────────────────────────────────────

  return (
    <main id="main-content" className="grid-container">
      <div className="usa-section">

        {/* ── Page heading ────────────────────────────────────────────── */}
        <h1>{heading}</h1>

        {/* ── Auto-save indicator ─────────────────────────────────────── */}
        {isEditMode && (
          <p
            className="usa-hint"
            aria-live="polite"
            aria-atomic="true"
            data-testid="autosave-indicator"
          >
            {isSaving
              ? 'Saving…'
              : lastSavedAt
              ? `Last saved at ${lastSavedAt}`
              : 'Not yet saved'}
          </p>
        )}

        {/* ── Save error ───────────────────────────────────────────────── */}
        {saveError && (
          <div className="usa-alert usa-alert--error" role="alert">
            <div className="usa-alert__body">
              <h2 className="usa-alert__heading">Save failed</h2>
              <p className="usa-alert__text">{saveError}</p>
            </div>
          </div>
        )}

        {/* ── Form fields ─────────────────────────────────────────────── */}
        <form
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            void handleSave();
          }}
          aria-label={`${heading} form`}
        >
          {/* ── Slug field — auto-generated from title, editable, shows URL preview ── */}
          {/* Issue #33: FR-NAV-05 */}
          <SlugField
            value={slug}
            onChange={setSlug}
            error={slugError ?? validationErrors['slug']}
            disabled={isSaving}
          />

          {/* Dynamic field renderers from content type schema */}
          {fieldDefs.map((def) => (
            <FieldRenderer
              key={def.name}
              def={def}
              value={fieldValues[def.name] ?? null}
              error={validationErrors[def.name]}
              onChange={(v) => setFieldValue(def.name, v)}
              onBlur={validateOnBlur}
            />
          ))}

          {/* ── Form actions ─────────────────────────────────────── */}
          <div className="usa-form-group">
            <button
              type="submit"
              className="usa-button"
              disabled={isSaving}
              aria-label={isEditMode ? 'Save changes' : 'Create entry'}
            >
              {isSaving ? 'Saving…' : isEditMode ? 'Save changes' : 'Create entry'}
            </button>

            {/* ── Preview button — issue #34, BRD FR-AUTH-08 ──────────── */}
            {/* Passes current (possibly unsaved) field values for live preview (AC3) */}
            <PreviewButton
              entryId={entryId}
              fieldValues={fieldValues}
              isSaving={isSaving}
            />

            {onCancel && (
              <button
                type="button"
                className="usa-button usa-button--outline"
                onClick={onCancel}
                aria-label="Cancel and return to list"
              >
                Cancel
              </button>
            )}
          </div>

          {/* ── Workflow (issue #37) — edit mode only; a new entry is Draft ── */}
          {isEditMode && entryId !== undefined && (
            <WorkflowActions entryId={entryId} disabled={isSaving} />
          )}

          {/* ── Validation summary ───────────────────────────────────── */}
          {hasErrors && (
            <div
              className="usa-alert usa-alert--error"
              role="alert"
              aria-live="assertive"
              data-testid="validation-summary"
            >
              <div className="usa-alert__body">
                <h2 className="usa-alert__heading">Please correct the following errors</h2>
                <ul className="usa-list">
                  {Object.entries(validationErrors).map(([field, msg]) => (
                    <li key={field}>
                      <a href={`#field-${field}`}>{msg}</a>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          )}
        </form>

        {/* ── Auto-save notice (bottom of page, always visible in edit mode) ── */}
        {isEditMode && (
          <p className="usa-hint">
            This form auto-saves every 60 seconds.
          </p>
        )}
      </div>
    </main>
  );
}
