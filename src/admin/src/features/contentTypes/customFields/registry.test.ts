import { describe, it, expect, beforeEach } from 'vitest';
import { registerCustomField, getCustomFieldComponent, getRegisteredCustomFieldTypeNames } from '../customFields/registry';
import { GeoPointField } from '../customFields/GeoPointField';

describe('custom field registry', () => {
  beforeEach(() => {
    // Re-import registry to reset state is not easily possible in ESM;
    // instead we rely on consistent test ordering. registerCustomField is
    // idempotent (last-write wins) so registering in each test is safe.
  });

  it('returns undefined for unregistered type', () => {
    expect(getCustomFieldComponent('no_such_type_xyz')).toBeUndefined();
  });

  it('registerCustomField stores the component', () => {
    registerCustomField('test_type_abc', GeoPointField);
    const retrieved = getCustomFieldComponent('test_type_abc');
    expect(retrieved).toBe(GeoPointField);
  });

  it('getRegisteredCustomFieldTypeNames includes registered type', () => {
    registerCustomField('geo_point', GeoPointField);
    expect(getRegisteredCustomFieldTypeNames()).toContain('geo_point');
  });

  it('overwriting a registration replaces the component', () => {
    const DummyComponent = () => null;
    registerCustomField('geo_point', GeoPointField);
    registerCustomField('geo_point', DummyComponent as never);
    expect(getCustomFieldComponent('geo_point')).toBe(DummyComponent);
    // Restore for subsequent tests
    registerCustomField('geo_point', GeoPointField);
  });
});
