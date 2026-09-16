/**
 * USWDS field renderers for all content type field types (issue #30).
 *
 * Each renderer:
 *  - Uses USWDS CSS classes (usa-label, usa-input, usa-error-message, usa-hint)
 *  - Marks required fields with <abbr title="required"> *</abbr>
 *  - Fires onBlur validation via the passed onBlur callback
 *  - Associates aria-describedby on error messages
 *  - No inline styles; no !important overrides
 */

import React from 'react';
import type { FieldDefinitionDto, FieldValues } from './formTypes';
import { RichTextEditor } from './RichTextEditor';

export interface FieldRendererProps {
  def: FieldDefinitionDto;
  value: FieldValues[string];
  error?: string;
  onChange: (value: FieldValues[string]) => void;
  onBlur: (fieldName: string) => void;
}

// ── Short Text ─────────────────────────────────────────────────────────────────

export function ShortTextField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      <label className="usa-label" htmlFor={inputId}>
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </label>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <input
        id={inputId}
        type="text"
        className={`usa-input${error ? ' usa-input--error' : ''}`}
        value={typeof value === 'string' ? value : ''}
        maxLength={def.maxLength ?? undefined}
        aria-required={def.required}
        aria-describedby={describedBy}
        aria-invalid={!!error}
        onChange={(e) => onChange(e.target.value)}
        onBlur={() => onBlur(def.name)}
      />
    </div>
  );
}

// ── Long Text ──────────────────────────────────────────────────────────────────

export function LongTextField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      <label className="usa-label" htmlFor={inputId}>
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </label>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <textarea
        id={inputId}
        className={`usa-textarea${error ? ' usa-input--error' : ''}`}
        value={typeof value === 'string' ? value : ''}
        maxLength={def.maxLength ?? undefined}
        aria-required={def.required}
        aria-describedby={describedBy}
        aria-invalid={!!error}
        rows={6}
        onChange={(e) => onChange(e.target.value)}
        onBlur={() => onBlur(def.name)}
      />
    </div>
  );
}

// ── Rich Text (TipTap WYSIWYG, Markdown storage — issue #115) ─────────────────
//
// The editor IS the live preview: no split pane, no preview toggle. Stored value
// is a Markdown string serialised by tiptap-markdown; Markdig (#66) renders it on
// the public site with DisableHtml(), so the editor never emits raw HTML.

export function RichTextField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const editorId = `field-${def.name}`;
  const labelId  = `field-${def.name}-label`;
  const errorId  = `field-${def.name}-error`;
  const hintId   = `field-${def.name}-hint`;
  const hasHint  = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div
      className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}
      data-testid="rich-text-field"
    >
      {/*
       * A contenteditable <div> is non-labellable per the HTML spec, so this is
       * a <span> with an id rather than <label htmlFor>; the editor's
       * contenteditable references it via aria-labelledby.
       */}
      <span id={labelId} className="usa-label">
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </span>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <RichTextEditor
        editorId={editorId}
        labelId={labelId}
        value={typeof value === 'string' ? value : ''}
        onChange={(md) => onChange(md)}
        onBlur={() => onBlur(def.name)}
        ariaDescribedby={describedBy}
        ariaInvalid={!!error}
        ariaRequired={def.required}
      />
    </div>
  );
}

// ── Number ─────────────────────────────────────────────────────────────────────

export function NumberField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      <label className="usa-label" htmlFor={inputId}>
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </label>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <input
        id={inputId}
        type="number"
        className={`usa-input${error ? ' usa-input--error' : ''}`}
        value={typeof value === 'number' ? value : ''}
        aria-required={def.required}
        aria-describedby={describedBy}
        aria-invalid={!!error}
        onChange={(e) => {
          const v = e.target.value;
          onChange(v === '' ? null : Number(v));
        }}
        onBlur={() => onBlur(def.name)}
      />
    </div>
  );
}

// ── Date / Time ────────────────────────────────────────────────────────────────

export function DateTimeField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  // Convert ISO string to datetime-local format (YYYY-MM-DDTHH:MM)
  const displayValue =
    typeof value === 'string' && value
      ? value.substring(0, 16)
      : '';

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      <label className="usa-label" htmlFor={inputId}>
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </label>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <input
        id={inputId}
        type="datetime-local"
        className={`usa-input${error ? ' usa-input--error' : ''}`}
        value={displayValue}
        aria-required={def.required}
        aria-describedby={describedBy}
        aria-invalid={!!error}
        onChange={(e) => onChange(e.target.value ? `${e.target.value}:00Z` : null)}
        onBlur={() => onBlur(def.name)}
      />
    </div>
  );
}

// ── Boolean (Checkbox) ─────────────────────────────────────────────────────────

export function BooleanField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <div className="usa-checkbox">
        <input
          id={inputId}
          type="checkbox"
          className="usa-checkbox__input"
          checked={value === true}
          aria-describedby={describedBy}
          aria-invalid={!!error}
          onChange={(e) => onChange(e.target.checked)}
          onBlur={() => onBlur(def.name)}
        />
        <label className="usa-checkbox__label" htmlFor={inputId}>
          {def.label}
          {def.required && (
            <abbr title="required" className="usa-hint usa-hint--required">
              {' '}*
            </abbr>
          )}
        </label>
      </div>
    </div>
  );
}

// ── Media Reference ────────────────────────────────────────────────────────────

export function MediaReferenceField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      <label className="usa-label" htmlFor={inputId}>
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </label>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      <span className="usa-hint">Enter a media asset ID or URL.</span>
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <input
        id={inputId}
        type="text"
        className={`usa-input${error ? ' usa-input--error' : ''}`}
        value={typeof value === 'string' ? value : ''}
        aria-required={def.required}
        aria-describedby={describedBy}
        aria-invalid={!!error}
        aria-label={`${def.label} media asset reference`}
        onChange={(e) => onChange(e.target.value || null)}
        onBlur={() => onBlur(def.name)}
      />
    </div>
  );
}

// ── Taxonomy / Related Entry (single + multi treated as comma-separated IDs) ───

export function TaxonomyReferenceField({
  def,
  value,
  error,
  onChange,
  onBlur,
}: FieldRendererProps): JSX.Element {
  const inputId = `field-${def.name}`;
  const errorId = `field-${def.name}-error`;
  const hintId  = `field-${def.name}-hint`;
  const hasHint = !!def.hint;
  const isMulti =
    def.type === 'MultiTaxonomyReference' || def.type === 'MultiRelatedEntry';

  const describedBy = [
    hasHint  ? hintId  : null,
    error    ? errorId : null,
  ]
    .filter(Boolean)
    .join(' ') || undefined;

  return (
    <div className={`usa-form-group${error ? ' usa-form-group--error' : ''}`}>
      <label className="usa-label" htmlFor={inputId}>
        {def.label}
        {def.required && (
          <abbr title="required" className="usa-hint usa-hint--required">
            {' '}*
          </abbr>
        )}
      </label>
      {hasHint && (
        <span id={hintId} className="usa-hint">
          {def.hint}
        </span>
      )}
      <span className="usa-hint">
        {isMulti ? 'Enter comma-separated IDs.' : 'Enter the ID or slug.'}
      </span>
      {error && (
        <span id={errorId} className="usa-error-message" role="alert">
          {error}
        </span>
      )}
      <input
        id={inputId}
        type="text"
        className={`usa-input${error ? ' usa-input--error' : ''}`}
        value={typeof value === 'string' ? value : ''}
        aria-required={def.required}
        aria-describedby={describedBy}
        aria-invalid={!!error}
        onChange={(e) => onChange(e.target.value || null)}
        onBlur={() => onBlur(def.name)}
      />
    </div>
  );
}

// ── Field type router ──────────────────────────────────────────────────────────

/**
 * Renders the correct USWDS field component for the given field type.
 * Custom field types (not matched here) fall back to ShortTextField.
 */
export function FieldRenderer(props: FieldRendererProps): JSX.Element {
  switch (props.def.type) {
    case 'ShortText':
      return <ShortTextField {...props} />;
    case 'LongText':
      return <LongTextField {...props} />;
    case 'RichText':
      return <RichTextField {...props} />;
    case 'Number':
      return <NumberField {...props} />;
    case 'DateTime':
      return <DateTimeField {...props} />;
    case 'Boolean':
      return <BooleanField {...props} />;
    case 'MediaReference':
      return <MediaReferenceField {...props} />;
    case 'TaxonomyReference':
    case 'MultiTaxonomyReference':
    case 'RelatedEntry':
    case 'MultiRelatedEntry':
      return <TaxonomyReferenceField {...props} />;
    default:
      // Unknown / custom field type: fall back to short text
      return <ShortTextField {...props} />;
  }
}
