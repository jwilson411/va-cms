/**
 * NavItemForm — modal dialog for creating/editing a navigation item.
 * Issue #46 — BRD FR-NAV-03.
 *
 * USWDS: usa-modal, usa-form, usa-input, usa-select, usa-checkbox, usa-button
 * Accessibility: labeled inputs, aria-describedby on error messages
 */

import React, { useEffect, useState } from 'react';
import type { NavTreeNode, UpsertItemRequest } from './types';

interface NavItemFormProps {
  /** Existing item when editing; null when creating. */
  item: NavTreeNode | null;
  /** Available parent items (for the "Parent" dropdown). Excludes the item itself and its descendants. */
  parentOptions: Array<{ id: number | null; label: string }>;
  onSubmit: (req: UpsertItemRequest) => void;
  onCancel: () => void;
  isLoading: boolean;
  error: string | null;
}

export function NavItemForm({
  item,
  parentOptions,
  onSubmit,
  onCancel,
  isLoading,
  error,
}: NavItemFormProps): JSX.Element {
  const [label, setLabel]           = useState(item?.label ?? '');
  const [url, setUrl]               = useState(item?.url ?? '');
  const [target, setTarget]         = useState(item?.target ?? '_self');
  const [sortOrder, setSortOrder]   = useState(item?.sortOrder ?? 0);
  const [isVisible, setIsVisible]   = useState(item?.isVisible ?? true);
  const [parentItemId, setParentItemId] = useState<number | null>(
    item?.parentItemId ?? null
  );

  const [labelError, setLabelError] = useState('');

  useEffect(() => {
    setLabel(item?.label ?? '');
    setUrl(item?.url ?? '');
    setTarget(item?.target ?? '_self');
    setSortOrder(item?.sortOrder ?? 0);
    setIsVisible(item?.isVisible ?? true);
    setParentItemId(item?.parentItemId ?? null);
    setLabelError('');
  }, [item]);

  function handleSubmit(e: React.FormEvent): void {
    e.preventDefault();
    if (!label.trim()) {
      setLabelError('Label is required.');
      return;
    }
    setLabelError('');
    onSubmit({
      label: label.trim(),
      url: url.trim() || null,
      target,
      sortOrder,
      isVisible,
      parentItemId,
    });
  }

  const formId = item ? `edit-item-${item.id}` : 'new-item';

  return (
    <div
      className="usa-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby={`${formId}-title`}
      data-testid="nav-item-form"
    >
      <div className="usa-modal__content">
        <div className="usa-modal__main">
          <h2 id={`${formId}-title`} className="usa-modal__heading">
            {item ? 'Edit navigation item' : 'Add navigation item'}
          </h2>

          <div className="usa-prose">
            <form onSubmit={handleSubmit} noValidate>
              {/* ── Label ─────────────────────────────────────────────── */}
              <div className="usa-form-group">
                <label className="usa-label" htmlFor={`${formId}-label`}>
                  Label <abbr title="required" className="usa-required">*</abbr>
                </label>
                {labelError && (
                  <span
                    id={`${formId}-label-error`}
                    className="usa-error-message"
                    role="alert"
                  >
                    {labelError}
                  </span>
                )}
                <input
                  className={`usa-input ${labelError ? 'usa-input--error' : ''}`}
                  id={`${formId}-label`}
                  name="label"
                  type="text"
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  aria-describedby={labelError ? `${formId}-label-error` : undefined}
                  aria-required="true"
                  aria-invalid={Boolean(labelError)}
                />
              </div>

              {/* ── URL ───────────────────────────────────────────────── */}
              <div className="usa-form-group">
                <label className="usa-label" htmlFor={`${formId}-url`}>
                  URL
                </label>
                <span className="usa-hint">
                  Absolute (/news) or external (https://…). Leave blank for
                  expandable parent items.
                </span>
                <input
                  className="usa-input"
                  id={`${formId}-url`}
                  name="url"
                  type="text"
                  value={url}
                  onChange={(e) => setUrl(e.target.value)}
                />
              </div>

              {/* ── Parent ────────────────────────────────────────────── */}
              <div className="usa-form-group">
                <label className="usa-label" htmlFor={`${formId}-parent`}>
                  Parent item
                </label>
                <span className="usa-hint">
                  Leave blank to place at the top level.
                </span>
                <select
                  className="usa-select"
                  id={`${formId}-parent`}
                  name="parentItemId"
                  value={parentItemId ?? ''}
                  onChange={(e) =>
                    setParentItemId(
                      e.target.value === '' ? null : Number(e.target.value)
                    )
                  }
                >
                  <option value="">— Top level —</option>
                  {parentOptions
                    .filter((p) => p.id !== null)
                    .map((p) => (
                      <option key={p.id} value={p.id!}>
                        {p.label}
                      </option>
                    ))}
                </select>
              </div>

              {/* ── Target ────────────────────────────────────────────── */}
              <div className="usa-form-group">
                <label className="usa-label" htmlFor={`${formId}-target`}>
                  Link target
                </label>
                <select
                  className="usa-select"
                  id={`${formId}-target`}
                  name="target"
                  value={target}
                  onChange={(e) => setTarget(e.target.value)}
                >
                  <option value="_self">Same tab (_self)</option>
                  <option value="_blank">New tab (_blank)</option>
                </select>
              </div>

              {/* ── Sort order ─────────────────────────────────────────── */}
              <div className="usa-form-group">
                <label className="usa-label" htmlFor={`${formId}-sort`}>
                  Sort order
                </label>
                <span className="usa-hint">
                  Lower numbers appear first. You can also drag and drop to
                  reorder.
                </span>
                <input
                  className="usa-input usa-input--small"
                  id={`${formId}-sort`}
                  name="sortOrder"
                  type="number"
                  min={0}
                  value={sortOrder}
                  onChange={(e) => setSortOrder(Number(e.target.value))}
                />
              </div>

              {/* ── Visible ────────────────────────────────────────────── */}
              <div className="usa-form-group">
                <div className="usa-checkbox">
                  <input
                    className="usa-checkbox__input"
                    id={`${formId}-visible`}
                    name="isVisible"
                    type="checkbox"
                    checked={isVisible}
                    onChange={(e) => setIsVisible(e.target.checked)}
                  />
                  <label
                    className="usa-checkbox__label"
                    htmlFor={`${formId}-visible`}
                  >
                    Visible in navigation
                  </label>
                </div>
              </div>

              {/* ── API error ─────────────────────────────────────────── */}
              {error && (
                <div
                  className="usa-alert usa-alert--error usa-alert--slim"
                  role="alert"
                >
                  <div className="usa-alert__body">
                    <p className="usa-alert__text">{error}</p>
                  </div>
                </div>
              )}

              {/* ── Actions ───────────────────────────────────────────── */}
              <div className="usa-modal__footer">
                <ul className="usa-button-group">
                  <li className="usa-button-group__item">
                    <button
                      type="submit"
                      className="usa-button"
                      disabled={isLoading}
                    >
                      {isLoading ? 'Saving…' : item ? 'Save changes' : 'Add item'}
                    </button>
                  </li>
                  <li className="usa-button-group__item">
                    <button
                      type="button"
                      className="usa-button usa-button--unstyled padding-105 text-center"
                      onClick={onCancel}
                      disabled={isLoading}
                    >
                      Cancel
                    </button>
                  </li>
                </ul>
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}
