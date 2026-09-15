/**
 * Custom field registry bootstrap (issue #27 / FR-DEV-05).
 *
 * Import this file once at app startup (e.g. in main.tsx) to register all
 * custom field React components with the admin form editor registry.
 *
 * GeoPoint is the reference implementation. Additional custom fields follow
 * the same pattern:
 *   import { MyCustomField } from './MyCustomField';
 *   registerCustomField('my_type_name', MyCustomField);
 */
import { registerCustomField } from './registry';
import { GeoPointField } from './GeoPointField';

// Register the GeoPoint reference implementation.
registerCustomField('geo_point', GeoPointField);

export { registerCustomField, getCustomFieldComponent, getRegisteredCustomFieldTypeNames } from './registry';
export type { CustomFieldProps, CustomFieldComponent } from './types';
