/**
 * RedirectForm — create or edit a redirect rule.
 * Issue #48 — BRD FR-NAV-06.
 */

import React, { useEffect, useId, useRef, useState } from 'react';
import type { RedirectAdminDto } from './types';

export interface RedirectFormProps {
  /** Existing redirect being edited; null/undefined = create mode. */
  existing?: RedirectAdminDto | null;
  onSubmit: (values: { fromPath: string; toPath: string; statusCode: number }) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
  submitError?: string | null;
}

export function RedirectForm({
  existing,
  onSubmit,
  onCancel,
  isSubmitting = false,
  submitError,
}: RedirectFormProps) {
  const uid = useId();
  const fromId   = `${uid}-from`;
  const toId     = `${uid}-to`;
  const codeId   = `${uid}-code`;
  const errFromId = `${uid}-from-err`;
  const errToId   = `${uid}-to-err`;

  const [fromPath,   setFromPath]   = useState(existing?.fromPath   ?? '');
  const [toPath,     setToPath]     = useState(existing?.toPath     ?? '');
  const [statusCode, setStatusCode] = useState(existing?.statusCode ?? 301);
  const [errors,     setErrors]     = useState<{ fromPath?: string; toPath?: string }>({});
  const firstRef = useRef<HTMLInputElement>(null);

  // Sync fields when existing changes (edit modal re-open)
  useEffect(() => {
    setFromPath(existing?.fromPath   ?? '');
    setToPath(existing?.toPath       ?? '');
    setStatusCode(existing?.statusCode ?? 301);
    setErrors({});
    setTimeout(() => firstRef.current?.focus(), 0);
  }, [existing?.id]);  // eslint-disable-line react-hooks/exhaustive-deps

  function validate() {
    const errs: { fromPath?: string; toPath?: string } = {};
    if (!fromPath.trim())
      errs.fromPath = 'From Path is required.';
    else if (!fromPath.startsWith('/'))
      errs.fromPath = 'From Path must start with /.';
    if (!toPath.trim())
      errs.toPath = 'To Path is required.';
    else if (!toPath.startsWith('/') && !toPath.startsWith('http'))
      errs.toPath = 'To Path must start with / or http(s)://.';
    return errs;
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const errs = validate();
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;
    onSubmit({ fromPath: fromPath.trim(), toPath: toPath.trim(), statusCode });
  }

  return (
    <form onSubmit={handleSubmit} noValidate data-testid="redirect-form">
      {/* From Path */}
      <div className="usa-form-group">
        <label className="usa-label" htmlFor={fromId}>
          From Path <abbr title="required" className="usa-hint--required"> *</abbr>
        </label>
        {errors.fromPath && (
          <span id={errFromId} className="usa-error-message" role="alert">
            {errors.fromPath}
          </span>
        )}
        <input
          ref={firstRef}
          id={fromId}
          className={`usa-input${errors.fromPath ? ' usa-input--error' : ''}`}
          type="text"
          value={fromPath}
          onChange={e => setFromPath(e.target.value)}
          aria-describedby={errors.fromPath ? errFromId : undefined}
          aria-required="true"
          placeholder="/old-page-path"
        />
      </div>

      {/* To Path */}
      <div className="usa-form-group">
        <label className="usa-label" htmlFor={toId}>
          To Path <abbr title="required" className="usa-hint--required"> *</abbr>
        </label>
        {errors.toPath && (
          <span id={errToId} className="usa-error-message" role="alert">
            {errors.toPath}
          </span>
        )}
        <input
          id={toId}
          className={`usa-input${errors.toPath ? ' usa-input--error' : ''}`}
          type="text"
          value={toPath}
          onChange={e => setToPath(e.target.value)}
          aria-describedby={errors.toPath ? errToId : undefined}
          aria-required="true"
          placeholder="/new-page-path"
        />
      </div>

      {/* Status Code */}
      <div className="usa-form-group">
        <label className="usa-label" htmlFor={codeId}>
          Status Code
        </label>
        <select
          id={codeId}
          className="usa-select"
          value={statusCode}
          onChange={e => setStatusCode(Number(e.target.value))}
        >
          <option value={301}>301 — Permanent</option>
          <option value={302}>302 — Temporary</option>
        </select>
      </div>

      {/* Server error */}
      {submitError && (
        <div className="usa-alert usa-alert--error usa-alert--slim" role="alert">
          <div className="usa-alert__body">
            <p className="usa-alert__text">{submitError}</p>
          </div>
        </div>
      )}

      {/* Actions */}
      <div className="usa-button-group margin-top-2">
        <button type="submit" className="usa-button" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : existing ? 'Save Changes' : 'Create Redirect'}
        </button>
        <button
          type="button"
          className="usa-button usa-button--unstyled padding-105 text-center"
          onClick={onCancel}
          disabled={isSubmitting}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
