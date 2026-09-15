/**
 * useFocusTrap — WCAG 2.1 / USWDS-compliant keyboard focus trap for modal dialogs.
 *
 * Behaviour:
 *   - On mount: focuses the first focusable element inside the container.
 *   - Tab / Shift+Tab: cycles focus within the container; wraps at boundaries.
 *   - Escape: calls onEscape() when provided.
 *
 * Usage:
 *   const dialogRef = useRef<HTMLDivElement>(null);
 *   useFocusTrap(dialogRef, { onEscape: closeModal });
 *
 * This does NOT polyfill the native HTML `dialog` element — it augments a
 * `role="dialog"` div to match USWDS Modal focus-trap behaviour.
 */

import { RefObject, useEffect } from 'react';

const FOCUSABLE_SELECTORS = [
  'a[href]',
  'area[href]',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'button:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
  'details > summary',
].join(', ');

export interface UseFocusTrapOptions {
  /** Called when the user presses Escape. Typically closes the modal. */
  onEscape?: () => void;
  /**
   * Set to false to disable the trap without unmounting.
   * Defaults to true.
   */
  enabled?: boolean;
}

/**
 * Traps keyboard focus inside `containerRef` while enabled.
 */
export function useFocusTrap<T extends HTMLElement>(
  containerRef: RefObject<T>,
  { onEscape, enabled = true }: UseFocusTrapOptions = {},
): void {
  useEffect(() => {
    if (!enabled) return;

    const container = containerRef.current;
    if (!container) return;

    // ── Save the element that was focused before the modal opened ──────────
    const previouslyFocused = document.activeElement as HTMLElement | null;

    // ── Focus first focusable child on open ────────────────────────────────
    const getFocusableElements = (): HTMLElement[] =>
      Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTORS));

    const focusFirst = () => {
      const els = getFocusableElements();
      if (els.length > 0) els[0].focus();
    };

    // Small rAF to let the DOM settle before focusing (animations, conditional renders).
    const rafId = requestAnimationFrame(focusFirst);

    // ── Tab / Escape handler ────────────────────────────────────────────────
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onEscape?.();
        return;
      }

      if (e.key !== 'Tab') return;

      const els = getFocusableElements();
      if (els.length === 0) {
        e.preventDefault();
        return;
      }

      const first = els[0];
      const last = els[els.length - 1];

      if (e.shiftKey) {
        // Shift+Tab — if focus is on first, wrap to last
        if (document.activeElement === first) {
          e.preventDefault();
          last.focus();
        }
      } else {
        // Tab — if focus is on last, wrap to first
        if (document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };

    document.addEventListener('keydown', handleKeyDown);

    return () => {
      cancelAnimationFrame(rafId);
      document.removeEventListener('keydown', handleKeyDown);
      // Restore focus to the element that was active before the modal opened
      previouslyFocused?.focus();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled]);
}
