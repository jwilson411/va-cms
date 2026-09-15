/**
 * SlugField — issue #33 (FR-NAV-05)
 *
 * Acceptance criteria implemented here:
 *  - Slug auto-populates from title field (lowercase, hyphenated, URL-safe)
 *  - Slug field is editable by the user
 *  - Shows full URL preview below the field (e.g. "https://va.gov/resources/my-slug")
 *  - Duplicate slug validation error is surfaced inline
 *  - USWDS form components throughout (usa-form-group, usa-label, usa-input, usa-hint, usa-error-message)
 *  - Accessible: label htmlFor, aria-describedby, aria-invalid, aria-required
 */

import React from 'react';

/** Base URL used for the URL preview. Reads from a build-time env variable if present. */
const PUBLIC_BASE_URL =
  (typeof window !== 'undefined' && (window as unknown as Record<string, string>).__CMS_PUBLIC_BASE_URL__) ||
  'https://va.gov';

export interface SlugFieldProps {
  /** Current slug value */
  value: string;
  /** Called when the user edits the slug */
  onChange: (newSlug: string) => void;
  /** Validation error message (e.g. duplicate slug) */
  error?: string | null;
  /** Whether the field is disabled (e.g. while saving) */
  disabled?: boolean;
}

/**
 * Convert an arbitrary string into a URL-safe slug:
 * lowercase, strip non-alphanumeric (except spaces and hyphens),
 * collapse spaces/hyphens, trim leading/trailing hyphens.
 */
export function slugify(input: string): string {
  return input
    .toLowerCase()
    .replace(/[^a-z0-9\s/-]/g, '')
    .replace(/[\s]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

/**
 * Slug input field with URL preview (issue #33).
 *
 * Renders:
 *  - A labelled usa-input for the slug
 *  - A usa-hint below showing the full public URL preview
 *  - A usa-error-message when `error` is set
 */
export function SlugField({ value, onChange, error, disabled }: SlugFieldProps): JSX.Element {
  const previewUrl = `${PUBLIC_BASE_URL}/${value}`;
  const hasError = !!error;

  return (
    <div
      className={`usa-form-group${hasError ? ' usa-form-group--error' : ''}`}
      data-testid="slug-field"
    >
      <label className="usa-label" htmlFor="field-slug">
        URL slug
        <abbr title="required" className="usa-hint usa-hint--required"> *</abbr>
      </label>

      <span className="usa-hint" id="field-slug-hint">
        Auto-generated from the page title. You may edit it manually.
      </span>

      {hasError && (
        <span
          id="field-slug-error"
          className="usa-error-message"
          role="alert"
          data-testid="slug-field-error"
        >
          {error}
        </span>
      )}

      <input
        id="field-slug"
        name="slug"
        type="text"
        className={`usa-input${hasError ? ' usa-input--error' : ''}`}
        value={value}
        disabled={disabled}
        aria-required
        aria-invalid={hasError || undefined}
        aria-describedby={
          [hasError ? 'field-slug-error' : null, 'field-slug-hint', 'field-slug-preview']
            .filter(Boolean)
            .join(' ') || undefined
        }
        onChange={(e) => onChange(e.target.value)}
        data-testid="slug-input"
      />

      <span
        className="usa-hint"
        id="field-slug-preview"
        data-testid="slug-url-preview"
        aria-live="polite"
        aria-atomic="true"
      >
        URL preview:{' '}
        <span className="usa-hint" style={{ wordBreak: 'break-all' }}>
          {previewUrl}
        </span>
      </span>
    </div>
  );
}
