import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { SchedulePanel } from './SchedulePanel';

/**
 * Tests for SchedulePanel — issue #35 (BRD FR-AUTH-04).
 *
 * Acceptance criteria verified here:
 *   AC1: Publish at input shown for Draft/Approved entries.
 *   AC3: Expire at input shown for Published/Approved entries.
 *   AC5: Status badge shows current scheduled times.
 */

const BASE_PROPS = {
  entryId: 42,
  status: 'Draft',
  scheduledPublishAt: null,
  scheduledExpireAt: null,
};

describe('SchedulePanel', () => {
  beforeEach(() => {
    // Reset fetch mock between tests
    vi.restoreAllMocks();
  });

  // ── AC1: Publish At input visibility ───────────────────────────────────────

  it('shows Publish At input for Draft status', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    expect(screen.getByTestId('publish-at-input')).toBeInTheDocument();
  });

  it('shows Publish At input for Approved status', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Approved" />);
    expect(screen.getByTestId('publish-at-input')).toBeInTheDocument();
  });

  it('does NOT show Publish At input for Published status', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Published" />);
    expect(screen.queryByTestId('publish-at-input')).not.toBeInTheDocument();
  });

  // ── AC3: Expire At input visibility ───────────────────────────────────────

  it('shows Expire At input for Published status', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Published" />);
    expect(screen.getByTestId('expire-at-input')).toBeInTheDocument();
  });

  it('shows Expire At input for Approved status', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Approved" />);
    expect(screen.getByTestId('expire-at-input')).toBeInTheDocument();
  });

  it('does NOT show Expire At input for Draft status', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    expect(screen.queryByTestId('expire-at-input')).not.toBeInTheDocument();
  });

  // ── AC5: Status badge shows scheduled times ───────────────────────────────

  it('shows no status badge when no scheduled times are set', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    expect(screen.queryByTestId('schedule-status-badge')).not.toBeInTheDocument();
  });

  it('shows status badge with Publish At when scheduled', () => {
    render(
      <SchedulePanel
        {...BASE_PROPS}
        status="Draft"
        scheduledPublishAt="2026-09-20T14:30:00.000Z"
      />
    );
    expect(screen.getByTestId('schedule-status-badge')).toBeInTheDocument();
    expect(screen.getByTestId('scheduled-publish-at-label')).toBeInTheDocument();
  });

  it('shows status badge with Expire At when scheduled', () => {
    render(
      <SchedulePanel
        {...BASE_PROPS}
        status="Published"
        scheduledExpireAt="2026-10-01T00:00:00.000Z"
      />
    );
    expect(screen.getByTestId('schedule-status-badge')).toBeInTheDocument();
    expect(screen.getByTestId('scheduled-expire-at-label')).toBeInTheDocument();
  });

  it('shows both scheduled times in badge when both are set', () => {
    render(
      <SchedulePanel
        {...BASE_PROPS}
        status="Approved"
        scheduledPublishAt="2026-09-25T10:00:00.000Z"
        scheduledExpireAt="2026-10-01T00:00:00.000Z"
      />
    );
    expect(screen.getByTestId('scheduled-publish-at-label')).toBeInTheDocument();
    expect(screen.getByTestId('scheduled-expire-at-label')).toBeInTheDocument();
  });

  // ── Save behavior ─────────────────────────────────────────────────────────

  it('renders Save schedule button when inputs are present', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    expect(screen.getByTestId('save-schedule-button')).toBeInTheDocument();
  });

  it('calls PATCH /api/v1/content/{id}/schedule on save', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({}),
    });
    vi.stubGlobal('fetch', fetchMock);

    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);

    fireEvent.click(screen.getByTestId('save-schedule-button'));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/v1/content/42/schedule',
        expect.objectContaining({
          method: 'PATCH',
          headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
        })
      );
    });
  });

  it('shows success message after successful save', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) });
    vi.stubGlobal('fetch', fetchMock);

    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    fireEvent.click(screen.getByTestId('save-schedule-button'));

    await waitFor(() => {
      expect(screen.getByTestId('schedule-success')).toBeInTheDocument();
    });
  });

  it('shows error message when API returns 400', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      json: async () => ({ error: 'Scheduled publish can only be set on Draft or Approved entries.' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    fireEvent.click(screen.getByTestId('save-schedule-button'));

    await waitFor(() => {
      expect(screen.getByTestId('schedule-error')).toBeInTheDocument();
      expect(screen.getByText(/Scheduled publish can only be set/)).toBeInTheDocument();
    });
  });

  it('disables save button while saving', async () => {
    // Never resolve to keep saving state
    const fetchMock = vi.fn().mockReturnValue(new Promise(() => {}));
    vi.stubGlobal('fetch', fetchMock);

    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    const btn = screen.getByTestId('save-schedule-button');
    fireEvent.click(btn);

    await waitFor(() => {
      expect(btn).toBeDisabled();
    });
  });

  it('calls onSaved callback after successful save', async () => {
    const onSaved = vi.fn();
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) });
    vi.stubGlobal('fetch', fetchMock);

    render(<SchedulePanel {...BASE_PROPS} status="Draft" onSaved={onSaved} />);
    fireEvent.click(screen.getByTestId('save-schedule-button'));

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalledOnce();
    });
  });

  // ── Accessibility ─────────────────────────────────────────────────────────

  it('Publish At input has aria-describedby pointing to hint', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    const input = screen.getByTestId('publish-at-input');
    expect(input).toHaveAttribute('aria-describedby', 'schedule-publish-at-hint');
  });

  it('Expire At input has aria-describedby pointing to hint', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Published" />);
    const input = screen.getByTestId('expire-at-input');
    expect(input).toHaveAttribute('aria-describedby', 'schedule-expire-at-hint');
  });

  it('Publish At input has an associated label', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" />);
    expect(screen.getByLabelText(/Publish at/i)).toBeInTheDocument();
  });

  it('Expire At input has an associated label', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Published" />);
    expect(screen.getByLabelText(/Expire at/i)).toBeInTheDocument();
  });

  // ── disabled prop ─────────────────────────────────────────────────────────

  it('disables all inputs when disabled prop is true', () => {
    render(<SchedulePanel {...BASE_PROPS} status="Draft" disabled />);
    expect(screen.getByTestId('publish-at-input')).toBeDisabled();
    expect(screen.getByTestId('save-schedule-button')).toBeDisabled();
  });
});
