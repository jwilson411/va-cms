import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { GeoPointField } from './GeoPointField';

const baseFieldDef = {
  name: 'location',
  label: 'Location',
  required: false,
  maxLength: null,
};

describe('GeoPointField', () => {
  it('renders latitude and longitude inputs', () => {
    render(
      <GeoPointField value={null} onChange={() => {}} fieldDef={baseFieldDef} />,
    );
    expect(screen.getByLabelText(/Latitude/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Longitude/i)).toBeInTheDocument();
  });

  it('renders the field label from fieldDef', () => {
    render(
      <GeoPointField value={null} onChange={() => {}} fieldDef={baseFieldDef} />,
    );
    expect(screen.getByText('Location')).toBeInTheDocument();
  });

  it('shows required marker when fieldDef.required is true', () => {
    render(
      <GeoPointField
        value={null}
        onChange={() => {}}
        fieldDef={{ ...baseFieldDef, required: true }}
      />,
    );
    expect(screen.getByTitle('required')).toBeInTheDocument();
  });

  it('pre-fills inputs from a valid JSON value', () => {
    render(
      <GeoPointField
        value='{"lat":38.9,"lng":-77.5}'
        onChange={() => {}}
        fieldDef={baseFieldDef}
      />,
    );
    const latInput = screen.getByLabelText(/Latitude/i) as HTMLInputElement;
    const lngInput = screen.getByLabelText(/Longitude/i) as HTMLInputElement;
    expect(latInput.value).toBe('38.9');
    expect(lngInput.value).toBe('-77.5');
  });

  it('calls onChange with serialised JSON when lat changes', () => {
    const handleChange = (val: string | null) => {
      if (val !== null) {
        const parsed = JSON.parse(val) as { lat: number; lng: number };
        expect(parsed.lat).toBe(10);
      }
    };
    render(
      <GeoPointField
        value='{"lat":0,"lng":0}'
        onChange={handleChange}
        fieldDef={baseFieldDef}
      />,
    );
    const latInput = screen.getByLabelText(/Latitude/i);
    fireEvent.change(latInput, { target: { value: '10' } });
  });

  it('shows an error for an out-of-range latitude', () => {
    render(
      <GeoPointField value={null} onChange={() => {}} fieldDef={baseFieldDef} />,
    );
    const latInput = screen.getByLabelText(/Latitude/i);
    fireEvent.change(latInput, { target: { value: '95' } });
    expect(screen.getByText(/Latitude must be between -90 and 90/i)).toBeInTheDocument();
  });

  it('shows an error for an out-of-range longitude', () => {
    render(
      <GeoPointField value={null} onChange={() => {}} fieldDef={baseFieldDef} />,
    );
    const lngInput = screen.getByLabelText(/Longitude/i);
    fireEvent.change(lngInput, { target: { value: '200' } });
    expect(screen.getByText(/Longitude must be between -180 and 180/i)).toBeInTheDocument();
  });

  it('error messages have role=alert', () => {
    render(
      <GeoPointField value={null} onChange={() => {}} fieldDef={baseFieldDef} />,
    );
    const latInput = screen.getByLabelText(/Latitude/i);
    fireEvent.change(latInput, { target: { value: '999' } });
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('clears the error when a valid value is entered', () => {
    render(
      <GeoPointField value={null} onChange={() => {}} fieldDef={baseFieldDef} />,
    );
    const latInput = screen.getByLabelText(/Latitude/i);
    fireEvent.change(latInput, { target: { value: '999' } });
    expect(screen.getByRole('alert')).toBeInTheDocument();

    fireEvent.change(latInput, { target: { value: '45' } });
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
