import { render, screen } from '@testing-library/react';
import { describe, it, expect, beforeEach } from 'vitest';
import { registerCustomField, getCustomFieldComponent } from './customFields/registry';
import { GeoPointField } from './customFields/GeoPointField';
import { CustomFieldRenderer } from './CustomFieldRenderer';

const fieldDef = {
  name: 'location',
  label: 'Location',
  required: false,
  maxLength: null,
};

describe('CustomFieldRenderer', () => {
  beforeEach(() => {
    registerCustomField('geo_point', GeoPointField);
  });

  it('renders the registered component for a known typeName', () => {
    render(
      <CustomFieldRenderer
        typeName="geo_point"
        value={null}
        onChange={() => {}}
        fieldDef={fieldDef}
      />,
    );
    // GeoPointField renders two inputs
    expect(screen.getByLabelText(/Latitude/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Longitude/i)).toBeInTheDocument();
  });

  it('renders a fallback text input for an unregistered typeName', () => {
    render(
      <CustomFieldRenderer
        typeName="unknown_custom_field_xyz"
        value="some value"
        onChange={() => {}}
        fieldDef={fieldDef}
      />,
    );
    const input = screen.getByRole('textbox') as HTMLInputElement;
    expect(input).toBeInTheDocument();
    expect(input.value).toBe('some value');
  });

  it('fallback renders the field label', () => {
    render(
      <CustomFieldRenderer
        typeName="another_unknown_xyz"
        value={null}
        onChange={() => {}}
        fieldDef={{ ...fieldDef, label: 'Custom Label' }}
      />,
    );
    expect(screen.getByText('Custom Label')).toBeInTheDocument();
  });

  it('fallback shows required marker when fieldDef.required is true', () => {
    render(
      <CustomFieldRenderer
        typeName="another_unknown_xyz"
        value={null}
        onChange={() => {}}
        fieldDef={{ ...fieldDef, required: true }}
      />,
    );
    expect(screen.getByTitle('required')).toBeInTheDocument();
  });
});
