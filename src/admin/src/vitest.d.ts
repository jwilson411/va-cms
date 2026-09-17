/**
 * Matcher typings for vitest 5 (#160).
 *
 * vitest 5 changed `Assertion<T>` to `Assertion<R, T>`, so the augmentation that
 * @testing-library/jest-dom ships no longer merges. Extend the
 * `Matchers` interface vitest 5 exposes for custom matchers instead. Runtime
 * registration still happens in vitest.setup.ts / the individual tests.
 */
import 'vitest';
import type { TestingLibraryMatchers } from '@testing-library/jest-dom/matchers';

declare module 'vitest' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface Matchers<R = void, T = unknown> extends TestingLibraryMatchers<unknown, R> {}
}
