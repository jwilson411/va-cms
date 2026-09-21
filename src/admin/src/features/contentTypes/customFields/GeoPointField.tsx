import React, { useId, useState } from 'react';
import type { CustomFieldProps } from './types';

interface GeoPointValue {
  lat: string;
  lng: string;
}

function parseValue(raw: string | null): GeoPointValue {
  if (!raw) return { lat: '', lng: '' };
  try {
    const obj = JSON.parse(raw) as { lat?: unknown; lng?: unknown };
    return {
      lat: obj.lat != null ? String(obj.lat) : '',
      lng: obj.lng != null ? String(obj.lng) : '',
    };
  } catch {
    return { lat: '', lng: '' };
  }
}

function formatValue(lat: string, lng: string): string | null {
  if (!lat && !lng) return null;
  const latNum = parseFloat(lat);
  const lngNum = parseFloat(lng);
  if (isNaN(latNum) || isNaN(lngNum)) return null;
  return JSON.stringify({ lat: latNum, lng: lngNum });
}

/**
 * Reference implementation of a custom field component (FR-DEV-05 / issue #27).
 *
 * Renders two USWDS text inputs for latitude and longitude, serialises the
 * pair as a JSON string for storage.
 *
 * Register at app startup:
 * ```ts
 * import { registerCustomField } from './registry';
 * import { GeoPointField } from './GeoPointField';
 * registerCustomField('geo_point', GeoPointField);
 * ```
 */
export function GeoPointField({
  value,
  onChange,
  fieldDef,
}: CustomFieldProps): JSX.Element {
  const baseId = useId();
  const latId = `${baseId}-lat`;
  const lngId = `${baseId}-lng`;
  const latErrorId = `${baseId}-lat-error`;
  const lngErrorId = `${baseId}-lng-error`;

  const parsed = parseValue(value);
  const [lat, setLat] = useState(parsed.lat);
  const [lng, setLng] = useState(parsed.lng);
  const [latError, setLatError] = useState<string | null>(null);
  const [lngError, setLngError] = useState<string | null>(null);

  function handleLatChange(e: React.ChangeEvent<HTMLInputElement>): void {
    const newLat = e.target.value;
    setLat(newLat);

    if (newLat !== '') {
      const num = parseFloat(newLat);
      if (isNaN(num) || num < -90 || num > 90) {
        setLatError('Latitude must be between -90 and 90.');
      } else {
        setLatError(null);
      }
    } else {
      setLatError(null);
    }

    onChange(formatValue(newLat, lng));
  }

  function handleLngChange(e: React.ChangeEvent<HTMLInputElement>): void {
    const newLng = e.target.value;
    setLng(newLng);

    if (newLng !== '') {
      const num = parseFloat(newLng);
      if (isNaN(num) || num < -180 || num > 180) {
        setLngError('Longitude must be between -180 and 180.');
      } else {
        setLngError(null);
      }
    } else {
      setLngError(null);
    }

    onChange(formatValue(lat, newLng));
  }

  return (
    <fieldset className="usa-fieldset">
      <legend className="usa-legend">
        {fieldDef.label}
        {fieldDef.required && (
          <abbr title="required" className="usa-hint--required">
            {' '}
            *
          </abbr>
        )}
      </legend>

      {/* Latitude */}
      <div className="usa-form-group">
        <label className="usa-label" htmlFor={latId}>
          Latitude
        </label>
        {latError != null && (
          <span
            id={latErrorId}
            className="usa-error-message"
            role="alert"
          >
            {latError}
          </span>
        )}
        <input
          id={latId}
          name={`${fieldDef.name}-lat`}
          type="number"
          step="any"
          min={-90}
          max={90}
          className={`usa-input${latError != null ? ' usa-input--error' : ''}`}
          value={lat}
          onChange={handleLatChange}
          aria-describedby={latError != null ? latErrorId : undefined}
          aria-required={fieldDef.required}
          placeholder="e.g. 38.9"
        />
      </div>

      {/* Longitude */}
      <div className="usa-form-group">
        <label className="usa-label" htmlFor={lngId}>
          Longitude
        </label>
        {lngError != null && (
          <span
            id={lngErrorId}
            className="usa-error-message"
            role="alert"
          >
            {lngError}
          </span>
        )}
        <input
          id={lngId}
          name={`${fieldDef.name}-lng`}
          type="number"
          step="any"
          min={-180}
          max={180}
          className={`usa-input${lngError != null ? ' usa-input--error' : ''}`}
          value={lng}
          onChange={handleLngChange}
          aria-describedby={lngError != null ? lngErrorId : undefined}
          aria-required={fieldDef.required}
          placeholder="e.g. -77.0"
        />
      </div>
    </fieldset>
  );
}
