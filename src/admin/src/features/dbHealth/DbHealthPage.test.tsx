/**
 * Tests for DbHealthPage (#172, BRD NFR-OPS-01) — the DB health dashboard
 * lives in the admin SPA, behind the real login, not on the public host.
 */

import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { DbHealthPage } from './DbHealthPage';
import { fetchDbHealth, DbHealthApiError, DB_HEALTH_URL, type DbHealthResponse } from './useDbHealth';
import { setAuthToken } from '../../lib/authorizedFetch';

const SAMPLE: DbHealthResponse = {
  indexFragmentation: [
    { tableName: 'ContentEntry', indexName: 'IX_ContentEntry_Slug', fragmentationPct: 42.5, pageCount: 1200 },
    { tableName: 'AuditLog', indexName: 'PK_AuditLog', fragmentationPct: 3.2, pageCount: 800 },
  ],
  tableSizes: [{ tableName: 'ContentEntry', rowCount: 15234, totalSizeMB: 120, usedSizeMB: 98 }],
  longRunningQueries: [
    {
      sessionId: 61,
      status: 'running',
      startTime: '2026-09-18T06:00:00Z',
      durationSec: 12,
      command: 'SELECT',
      queryText: 'SELECT * FROM dbo.ContentEntry WHERE ...',
      waitType: 'PAGEIOLATCH_SH',
      blockingSessionId: null,
    },
  ],
  collectedAt: '2026-09-18T06:01:00Z',
};

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DbHealthPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DbHealthPage', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    setAuthToken('test-jwt');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    setAuthToken(null);
  });

  it('calls the admin health endpoint with the bearer token (no DevBypass header)', async () => {
    fetchMock.mockResolvedValue(jsonResponse(SAMPLE));
    await fetchDbHealth();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe(DB_HEALTH_URL);
    const headers = new Headers(init?.headers);
    expect(headers.get('Authorization')).toBe('Bearer test-jwt');
    expect(headers.has('X-Dev-User')).toBe(false);
  });

  it('renders all three sections with the returned rows', async () => {
    fetchMock.mockResolvedValue(jsonResponse(SAMPLE));
    renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Database health' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 2, name: 'Index fragmentation' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Table sizes' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Long-running queries' })).toBeInTheDocument();

    expect(screen.getByText('IX_ContentEntry_Slug')).toBeInTheDocument();
    expect(screen.getByText('42.5 %')).toBeInTheDocument();
    expect(screen.getByText('15,234')).toBeInTheDocument();
    expect(screen.getByText('PAGEIOLATCH_SH')).toBeInTheDocument();
    expect(screen.getByText(/SELECT \* FROM dbo\.ContentEntry/)).toBeInTheDocument();
  });

  it('shows placeholders when a section is empty', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ ...SAMPLE, indexFragmentation: [], tableSizes: [], longRunningQueries: [] }),
    );
    renderPage();
    expect(await screen.findByText('No indexes over the page threshold.')).toBeInTheDocument();
    expect(screen.getByText('No tables reported.')).toBeInTheDocument();
    expect(screen.getByText('No long-running queries.')).toBeInTheDocument();
  });

  it('explains a 403 as a role problem', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ title: 'Forbidden' }, 403));
    renderPage();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Access denied');
    expect(alert).toHaveTextContent('Developer or SystemAdmin');
  });

  it('surfaces other failures with the API message', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ detail: 'Database unreachable' }, 503));
    renderPage();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Unable to load health data');
    expect(alert).toHaveTextContent('Database unreachable');
  });

  it('fetchDbHealth throws DbHealthApiError with the status', async () => {
    fetchMock.mockResolvedValue(new Response('nope', { status: 500 }));
    await expect(fetchDbHealth()).rejects.toMatchObject({ name: 'DbHealthApiError', status: 500 });
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(new DbHealthApiError(404, 'x').status).toBe(404);
  });
});
