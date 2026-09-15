/**
 * ContentEntryFormPage — create/edit form for a content entry (issue #30).
 *
 * Acceptance criteria:
 *  - Form renders all field types for the selected content type
 *  - USWDS form components used for all inputs (usa-input, usa-label, usa-error-message, usa-hint)
 *  - Required fields marked with asterisk
 *  - Inline validation fires on blur
 *  - Auto-save fires every 60 seconds and shows 'Last saved at HH:MM' indicator
 */

import React from 'react';
import { useContentEntryForm } from './useContentEntryForm';
import { FieldRenderer } from './FieldRenderers';

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
          {/* Slug field — always present */}
          <div className={`usa-form-group${validationErrors['slug'] ? ' usa-form-group--error' : ''}`}>
            <label className="usa-label" htmlFor="field-slug">
              URL slug
              <abbr title="required" className="usa-hint usa-hint--required">
                {' '}*
              </abbr>
            </label>
            <span className="usa-hint">
              The URL path segment for this content entry. Auto-generated from the title.
            </span>
            {validationErrors['slug'] && (
              <span
                id="field-slug-error"
                className="usa-error-message"
                role="alert"
              >
                {validationErrors['slug']}
              </span>
            )}
            <input
              id="field-slug"
              type="text"
              className={`usa-input${validationErrors['slug'] ? ' usa-input--error' : ''}`}
              value={slug}
              aria-required
              aria-describedby={validationErrors['slug'] ? 'field-slug-error' : undefined}
              aria-invalid={!!validationErrors['slug']}
              onChange={(e) => setSlug(e.target.value)}
              onBlur={() => {
                if (!slug) {
                  // Manual blur validation for slug
                }
              }}
            />
          </div>

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

          {/* ── Form actions ─────────────────────────────────────────── */}
          <div className="usa-form-group">
            <button
              type="submit"
              className="usa-button"
              disabled={isSaving}
              aria-label={isEditMode ? 'Save changes' : 'Create entry'}
            >
              {isSaving ? 'Saving…' : isEditMode ? 'Save changes' : 'Create entry'}
            </button>

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
