import React from 'react';
import { getCustomFieldComponent } from './customFields/registry';
import type { CustomFieldProps } from './customFields/types';

interface CustomFieldRendererProps extends CustomFieldProps {
  /** The field type name (matches the registered key in the custom field registry). */
  typeName: string;
}

/**
 * Admin form editor integration point for custom field types (FR-DEV-05 / issue #27).
 *
 * Looks up the registered React component for `typeName` and renders it.
 * Falls back to a plain `<input type="text">` if no component is registered,
 * so existing content entries with an unknown custom type remain editable.
 *
 * Usage in the form editor:
 * ```tsx
 * <CustomFieldRenderer
 *   typeName="geo_point"
 *   value={fieldValue}
 *   onChange={handleChange}
 *   fieldDef={fieldDef}
 * />
 * ```
 */
export function CustomFieldRenderer({
  typeName,
  value,
  onChange,
  fieldDef,
}: CustomFieldRendererProps): JSX.Element {
  const Component = getCustomFieldComponent(typeName);

  if (Component != null) {
    return <Component value={value} onChange={onChange} fieldDef={fieldDef} />;
  }

  // Fallback: plain USWDS text input for unrecognised custom types.
  const fallbackId = `custom-field-${fieldDef.name}`;
  return (
    <div className="usa-form-group">
      <label className="usa-label" htmlFor={fallbackId}>
        {fieldDef.label}
        {fieldDef.required && (
          <abbr title="required" className="usa-required">
            {' '}
            *
          </abbr>
        )}
      </label>
      <input
        id={fallbackId}
        type="text"
        className="usa-input"
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value || null)}
        aria-required={fieldDef.required}
        maxLength={fieldDef.maxLength ?? undefined}
      />
    </div>
  );
}
