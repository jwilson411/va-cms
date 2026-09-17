/**
 * Tests for AuditLogPage — issue #57 (BRD FR-USERS-06).
 */

import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AuditLogPage } from './AuditLogPage';
import * as hooks from './useAuditLog';

// Site settings (epic #141): render with the code defaults, no QueryClient needed.
vi.mock('../siteSettings/useClientSettings', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../siteSettings/useClientSettings')>();
  return {
    ...actual,
    useClientSettings: () => actual.buildClientSettings({}, { isLoading: false, isError: false }),
  };
});


// ── Mock hooks ────────────────────────────────────────────────────────────────

vi.mock('./useAuditLog');

const mockUseAuditLog   = vi.mocked(hooks.useAuditLog);
const mockBuildExportUrl = vi.mocked(hooks.buildExportUrl);

// ── Helpers ───────────────────────────────────────────────────────────────────

function buildRow(overrides: Partial<hooks.AuditLogRow> = {}): hooks.AuditLogRow {
  return {
    id:               1,
    actorId:          10,
    actorEmail:       'alice@va.gov',
    actorDisplayName: 'Alice Admin',
    entityType:       'ContentEntry',
    entityId:         '42',
    action:           'Publish',
    diffJson:         null,
    ipAddress:        '10.20.30.40',
    userAgent:        'Mozilla/5.0',
    correlationId:    'corr-1',
    outcome:          'Success',
    createdAt:        '2026-09-15T12:00:00Z',
    ...overrides,
  };
}

function buildPage(
  items: hooks.AuditLogRow[],
  totalItems = items.length,
  page = 1,
): hooks.AuditLogPage {
  return { items, totalItems, page, pageSize: 50 };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <AuditLogPage />
    </MemoryRouter>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('AuditLogPage — loading state', () => {
  it('shows loading indicator', () => {
    mockUseAuditLog.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');

    renderPage();

    expect(screen.getByText(/loading audit log/i)).toBeTruthy();
  });
});

describe('AuditLogPage — error state', () => {
  it('shows error alert when fetch fails', () => {
    mockUseAuditLog.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');

    renderPage();

    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText(/failed to load/i)).toBeTruthy();
  });
});

describe('AuditLogPage — empty results', () => {
  beforeEach(() => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([]),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');
  });

  it('shows empty state message', () => {
    renderPage();
    expect(screen.getByText(/no audit log entries/i)).toBeTruthy();
  });

  it('does not render a table when there are no rows', () => {
    renderPage();
    expect(screen.queryByRole('table')).toBeNull();
  });
});

describe('AuditLogPage — rows rendered', () => {
  beforeEach(() => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([
        buildRow({ id: 1, entityType: 'ContentEntry', action: 'Publish' }),
        buildRow({ id: 2, entityType: 'User', action: 'Deactivate',
          actorDisplayName: 'Bob Admin', actorEmail: 'bob@va.gov' }),
      ], 2),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');
  });

  it('renders the audit log table', () => {
    renderPage();
    expect(screen.getByRole('table')).toBeTruthy();
  });

  it('renders column headers', () => {
    renderPage();
    expect(screen.getByText('Date/Time')).toBeTruthy();
    expect(screen.getByText('Actor')).toBeTruthy();
    // Use getAllByText because the header and data rows may both contain these strings
    expect(screen.getAllByText('Entity Type').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Entity ID').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Action').length).toBeGreaterThanOrEqual(1);
  });

  it('shows actor display name for each row', () => {
    renderPage();
    expect(screen.getByText('Alice Admin')).toBeTruthy();
    expect(screen.getByText('Bob Admin')).toBeTruthy();
  });

  it('shows entity type for each row', () => {
    renderPage();
    expect(screen.getByText('ContentEntry')).toBeTruthy();
    expect(screen.getByText('User')).toBeTruthy();
  });

  it('shows action for each row', () => {
    renderPage();
    expect(screen.getByText('Publish')).toBeTruthy();
    expect(screen.getByText('Deactivate')).toBeTruthy();
  });

  // #165: outcome and source IP columns
  it('shows outcome and source IP for each row', () => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([
        buildRow({ id: 1, outcome: 'Success', ipAddress: '10.20.30.40' }),
        buildRow({ id: 2, action: 'LogonFailure', outcome: 'Failure', ipAddress: null }),
      ], 2),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    renderPage();
    expect(screen.getAllByText('Outcome').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Source IP').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('10.20.30.40')).toBeTruthy();
    expect(screen.getAllByText('Failure').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Success').length).toBeGreaterThanOrEqual(1);
  });

  it('applies outcome and IP filters to the query (#165)', async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Outcome'), { target: { value: 'Failure' } });
    fireEvent.change(screen.getByLabelText('Source IP'), { target: { value: ' 10.20.30.40 ' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Audit log filters' }));

    await waitFor(() =>
      expect(mockUseAuditLog).toHaveBeenLastCalledWith(
        expect.objectContaining({ outcome: 'Failure', ipAddress: '10.20.30.40' }),
        1,
        50,
      ),
    );
    expect(mockBuildExportUrl).toHaveBeenLastCalledWith(
      expect.objectContaining({ outcome: 'Failure', ipAddress: '10.20.30.40' }),
    );
  });
});

describe('AuditLogPage — filter form', () => {
  beforeEach(() => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([buildRow()]),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');
  });

  it('renders the filter form with all filter fields', () => {
    renderPage();
    expect(screen.getByLabelText(/user id/i)).toBeTruthy();
    expect(screen.getByLabelText(/action type/i)).toBeTruthy();
    expect(screen.getByLabelText(/entity type/i)).toBeTruthy();
    expect(screen.getByLabelText(/from date/i)).toBeTruthy();
    expect(screen.getByLabelText(/to date/i)).toBeTruthy();
  });

  it('renders the Apply filters button', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /apply filters/i })).toBeTruthy();
  });

  it('renders the Clear filters button', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /clear filters/i })).toBeTruthy();
  });

  it('clears all filter inputs when Clear is clicked', async () => {
    renderPage();
    const actionInput = screen.getByLabelText(/action type/i) as HTMLInputElement;

    fireEvent.change(actionInput, { target: { value: 'Publish' } });
    expect(actionInput.value).toBe('Publish');

    fireEvent.click(screen.getByRole('button', { name: /clear filters/i }));

    await waitFor(() => {
      expect(actionInput.value).toBe('');
    });
  });
});

describe('AuditLogPage — CSV export link', () => {
  it('renders an export CSV anchor link', () => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([buildRow()]),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');

    renderPage();

    const exportLink = screen.getByRole('link', { name: /export.*csv/i });
    expect(exportLink).toBeTruthy();
    expect((exportLink as HTMLAnchorElement).href).toContain('export.csv');
  });
});

describe('AuditLogPage — pagination', () => {
  it('does not render pagination when total is within one page', () => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([buildRow()], 1),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');

    renderPage();

    expect(screen.queryByRole('navigation', { name: /pagination/i })).toBeNull();
  });

  it('renders pagination when total exceeds page size', () => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage(
        Array.from({ length: 50 }, (_, i) => buildRow({ id: i + 1 })),
        150,
        1,
      ),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');

    renderPage();

    expect(screen.getByRole('navigation', { name: /pagination/i })).toBeTruthy();
  });
});

describe('AuditLogPage — null actor fallback', () => {
  it('shows "System" when actor is null', () => {
    mockUseAuditLog.mockReturnValue({
      data: buildPage([
        buildRow({ actorId: null, actorEmail: null, actorDisplayName: null }),
      ]),
      isLoading: false,
      isError: false,
    } as ReturnType<typeof hooks.useAuditLog>);
    mockBuildExportUrl.mockReturnValue('/api/v1/admin/audit/export.csv');

    renderPage();

    expect(screen.getByText('System')).toBeTruthy();
  });
});
