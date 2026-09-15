/**
 * Tests for PreviewButton — issue #34, BRD FR-AUTH-08.
 *
 * AC covered:
 *  AC1: Preview button renders the current draft in the public template (new tab).
 *  AC2: Preview does not require publishing — uses a signed preview token.
 *  AC3: Preview reflects current unsaved form state (passed via query param).
 *
 * Additional checks:
 *  - USWDS usa-button--outline class applied.
 *  - Button disabled in create mode (entryId undefined).
 *  - Hint shown in create mode explaining why preview is disabled.
 *  - Error message shown (usa-error-message, role=alert) on fetch failure.
 *  - aria-label and aria-busy attributes correct.
 */

import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { PreviewButton } from './PreviewButton';

// ── Helpers ──────────────────────────────────────────────────────────────────

const FIELD_VALUES = { title: 'Draft Title', body: '## Hello\n\nDraft body.' };

function makeMockFetch(token = 'test-token-abc', ok = true, status = 200) {
  return vi.fn().mockResolvedValue({
    ok,
    status,
    json: async () => ({ token, expiresInSeconds: 3600 }),
  });
}

// ── Tests ────────────────────────────────────────────────────────────────────

describe('PreviewButton', () => {
  let openSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    // Spy on window.open (AC1: opens new tab)
    openSpy = vi.fn();
    vi.stubGlobal('open', openSpy);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── Create mode ────────────────────────────────────────────────────────────

  it('is disabled when entryId is undefined (create mode)', () => {
    render(<PreviewButton entryId={undefined} fieldValues={FIELD_VALUES} />);
    const btn = screen.getByRole('button', { name: /save the entry before previewing/i });
    expect(btn).toBeDisabled();
  });

  it('shows hint text explaining disabled state in create mode', () => {
    render(<PreviewButton entryId={undefined} fieldValues={FIELD_VALUES} />);
    expect(screen.getByText(/preview is available after you save/i)).toBeTruthy();
  });

  it('has usa-button--outline class (USWDS design)', () => {
    const { container } = render(
      <PreviewButton entryId={1} fieldValues={FIELD_VALUES} />
    );
    const btn = container.querySelector('button');
    expect(btn?.className).toContain('usa-button--outline');
  });

  // ── Edit mode — happy path ─────────────────────────────────────────────────

  it('AC2: calls POST /api/v1/content/{id}/preview-token (no publish required)', async () => {
    const mockFetch = makeMockFetch();
    vi.stubGlobal('fetch', mockFetch);

    render(<PreviewButton entryId={42} fieldValues={FIELD_VALUES} />);
    const btn = screen.getByRole('button', { name: /preview this page/i });

    await act(async () => {
      fireEvent.click(btn);
    });

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        '/api/v1/content/42/preview-token',
        expect.objectContaining({ method: 'POST' })
      );
    });
  });

  it('AC1: opens preview in a new tab after obtaining token', async () => {
    const mockFetch = makeMockFetch('tok-123');
    vi.stubGlobal('fetch', mockFetch);

    render(<PreviewButton entryId={42} fieldValues={FIELD_VALUES} />);
    const btn = screen.getByRole('button', { name: /preview this page/i });

    await act(async () => {
      fireEvent.click(btn);
    });

    await waitFor(() => {
      expect(openSpy).toHaveBeenCalledOnce();
      const [url, target] = openSpy.mock.calls[0] as [string, string, string];
      expect(url).toContain('/api/v1/preview');
      expect(url).toContain('token=');
      expect(url).toContain('tok-123');
      expect(target).toBe('_blank');
    });
  });

  it('AC3: includes current (unsaved) field values in the preview URL', async () => {
    const mockFetch = makeMockFetch('tok-xyz');
    vi.stubGlobal('fetch', mockFetch);

    const unsavedFields = { title: 'Unsaved Title', body: 'Unsaved body content' };
    render(<PreviewButton entryId={7} fieldValues={unsavedFields} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /preview this page/i }));
    });

    await waitFor(() => {
      const [url] = openSpy.mock.calls[0] as [string];
      expect(url).toContain('fields=');
      // URL-decoded fields should contain the unsaved title
      const fieldsParam = new URLSearchParams(url.split('?')[1]).get('fields');
      expect(fieldsParam).toContain('Unsaved Title');
    });
  });

  // ── Error handling ─────────────────────────────────────────────────────────

  it('shows usa-error-message with role=alert on fetch failure', async () => {
    const mockFetch = makeMockFetch('', false, 403);
    vi.stubGlobal('fetch', mockFetch);

    render(<PreviewButton entryId={99} fieldValues={FIELD_VALUES} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /preview this page/i }));
    });

    await waitFor(() => {
      const errMsg = screen.getByRole('alert');
      expect(errMsg.className).toContain('usa-error-message');
      expect(errMsg.textContent).toContain('Failed to obtain preview token');
    });
  });

  it('does not open new tab on fetch failure', async () => {
    vi.stubGlobal('fetch', makeMockFetch('', false, 500));

    render(<PreviewButton entryId={1} fieldValues={FIELD_VALUES} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /preview this page/i }));
    });

    await waitFor(() => {
      expect(openSpy).not.toHaveBeenCalled();
    });
  });

  // ── Accessibility ──────────────────────────────────────────────────────────

  it('button has aria-label describing action in edit mode', () => {
    render(<PreviewButton entryId={5} fieldValues={FIELD_VALUES} />);
    const btn = screen.getByRole('button');
    expect(btn.getAttribute('aria-label')).toContain('new tab');
  });

  it('aria-busy is set during loading', async () => {
    // Simulate a slow fetch
    let resolve!: () => void;
    const slowFetch = vi.fn().mockReturnValue(
      new Promise<unknown>((res) => {
        resolve = () => res({
          ok: true,
          status: 200,
          json: async () => ({ token: 't', expiresInSeconds: 3600 }),
        });
      })
    );
    vi.stubGlobal('fetch', slowFetch);

    render(<PreviewButton entryId={1} fieldValues={FIELD_VALUES} />);
    const btn = screen.getByRole('button');

    // Click (don't await — check mid-flight)
    fireEvent.click(btn);

    // aria-busy should be set while loading
    await waitFor(() => {
      expect(btn.getAttribute('aria-busy')).toBe('true');
    });

    // Resolve the fetch
    await act(async () => {
      resolve();
    });
  });
});
