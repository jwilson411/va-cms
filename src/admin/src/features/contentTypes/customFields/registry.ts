/**
 * Client-side registry for custom field React components (FR-DEV-05 / issue #27).
 *
 * Usage:
 *   // 1. Register your component (once, at app startup):
 *   registerCustomField('geo_point', GeoPointField);
 *
 *   // 2. Render any registered component by type name:
 *   const Component = getCustomFieldComponent('geo_point');
 *   if (Component) return <Component {...props} />;
 */
import type { CustomFieldComponent } from './types';

const _registry = new Map<string, CustomFieldComponent>();

/**
 * Registers a React component to handle the given custom field `typeName`.
 * Calling this more than once for the same `typeName` overwrites the previous
 * registration (last-write wins — allows hot-reload in development).
 */
export function registerCustomField(
  typeName: string,
  component: CustomFieldComponent,
): void {
  _registry.set(typeName, component);
}

/**
 * Retrieves the React component registered for `typeName`, or `undefined` if
 * no component has been registered for that type.
 */
export function getCustomFieldComponent(
  typeName: string,
): CustomFieldComponent | undefined {
  return _registry.get(typeName);
}

/**
 * Returns the full list of registered custom field type names.
 * Useful for the admin type browser to indicate which types have a paired renderer.
 */
export function getRegisteredCustomFieldTypeNames(): string[] {
  return Array.from(_registry.keys());
}
