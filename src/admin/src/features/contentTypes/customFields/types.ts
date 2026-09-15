/**
 * Props passed to every custom field React component (FR-DEV-05 / issue #27).
 *
 * A custom field component receives the current value, a change callback, and
 * the field definition so it can render a USWDS-compliant form group with a
 * correct label and optional required marker.
 */
export interface CustomFieldProps {
  /** Current serialised value (matches the backend StorageType serialisation). */
  value: string | null;
  /** Called with the new serialised value whenever the field changes. */
  onChange: (value: string | null) => void;
  /** The field definition from the content type schema. */
  fieldDef: {
    name: string;
    label: string;
    required: boolean;
    maxLength: number | null;
  };
}

/** React component type for a custom field renderer. */
export type CustomFieldComponent = React.ComponentType<CustomFieldProps>;
