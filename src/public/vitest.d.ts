/**
 * Matcher typings for vitest 5 (#160).
 *
 * vitest 5 changed `Assertion<T>` to `Assertion<R, T>`, so the augmentation that
 * @testing-library/jest-dom and @types/jest-axe ship no longer merges. Extend the
 * `Matchers` interface vitest 5 exposes for custom matchers instead. Runtime
 * registration still happens in vitest.setup.ts / the individual tests.
 */
import 'vitest';
import type { TestingLibraryMatchers } from '@testing-library/jest-dom/matchers';

declare module 'vitest' {
  interface Matchers<R = void, T = unknown> extends TestingLibraryMatchers<unknown, R> {
    /** jest-axe: the axe result set reports no violations. */
    toHaveNoViolations(): R;
  }
}
