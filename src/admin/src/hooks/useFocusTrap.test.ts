/**
 * useFocusTrap tests — WCAG 2.1 modal focus trap hook.
 *
 * Covers:
 *   - First focusable element receives focus on mount
 *   - Tab wraps from last to first
 *   - Shift+Tab wraps from first to last
 *   - Escape calls onEscape
 *   - Focus is restored on unmount
 *   - enabled=false disables the trap
 */

import { renderHook } from '@testing-library/react';
import { useRef } from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useFocusTrap } from './useFocusTrap';

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeContainer(): HTMLDivElement {
  const div = document.createElement('div');
  ['btn-a', 'btn-b', 'btn-c'].forEach((id) => {
    const btn = document.createElement('button');
    btn.id = id;
    btn.textContent = id;
    div.appendChild(btn);
  });
  document.body.appendChild(div);
  return div;
}

function fireKeyDown(key: string, shift = false) {
  const event = new KeyboardEvent('keydown', {
    key,
    shiftKey: shift,
    bubbles: true,
    cancelable: true,
  });
  document.dispatchEvent(event);
  return event;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('useFocusTrap', () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = makeContainer();
    // Use fake timers so rAF resolves synchronously
    vi.useFakeTimers();
  });

  afterEach(() => {
    document.body.removeChild(container);
    vi.useRealTimers();
  });

  it('focuses the first focusable element on mount', () => {
    const ref = { current: container };
    renderHook(() => useFocusTrap(ref));
    vi.runAllTimers();
    expect(document.activeElement?.id).toBe('btn-a');
  });

  it('wraps Tab from last element to first', () => {
    const ref = { current: container };
    renderHook(() => useFocusTrap(ref));
    vi.runAllTimers();

    // Focus the last button manually
    const last = container.querySelector<HTMLElement>('#btn-c')!;
    last.focus();
    expect(document.activeElement?.id).toBe('btn-c');

    fireKeyDown('Tab', false);
    expect(document.activeElement?.id).toBe('btn-a');
  });

  it('wraps Shift+Tab from first element to last', () => {
    const ref = { current: container };
    renderHook(() => useFocusTrap(ref));
    vi.runAllTimers();

    // Focus already on btn-a (first)
    container.querySelector<HTMLElement>('#btn-a')!.focus();
    expect(document.activeElement?.id).toBe('btn-a');

    fireKeyDown('Tab', true);
    expect(document.activeElement?.id).toBe('btn-c');
  });

  it('calls onEscape when Escape is pressed', () => {
    const ref = { current: container };
    const onEscape = vi.fn();
    renderHook(() => useFocusTrap(ref, { onEscape }));
    vi.runAllTimers();

    fireKeyDown('Escape');
    expect(onEscape).toHaveBeenCalledTimes(1);
  });

  it('restores focus to previously-focused element on unmount', () => {
    // Give something else focus before the trap
    const external = document.createElement('button');
    external.id = 'external';
    document.body.appendChild(external);
    external.focus();
    expect(document.activeElement?.id).toBe('external');

    const ref = { current: container };
    const { unmount } = renderHook(() => useFocusTrap(ref));
    vi.runAllTimers();

    unmount();
    expect(document.activeElement?.id).toBe('external');

    document.body.removeChild(external);
  });

  it('does nothing when enabled=false', () => {
    const ref = { current: container };
    const onEscape = vi.fn();
    renderHook(() => useFocusTrap(ref, { onEscape, enabled: false }));
    vi.runAllTimers();

    // Focus should NOT have moved to btn-a
    expect(document.activeElement?.id).not.toBe('btn-a');

    fireKeyDown('Escape');
    expect(onEscape).not.toHaveBeenCalled();
  });
});
