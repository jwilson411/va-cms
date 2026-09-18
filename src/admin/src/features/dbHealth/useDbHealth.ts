/**
 * useDbHealth — TanStack Query hook for the database health endpoint (#172).
 *
 * GET /api/v1/admin/health/db — index fragmentation, table sizes and queries
 * running longer than 5 s (DbHealthController, policy CanDevelop: Developer or
 * SystemAdmin). Previously rendered by a page inside the public Next.js site
 * with a hard-coded X-Dev-User header; it now lives here, behind the real login.
 */

import { useQuery } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

export const DB_HEALTH_URL = '/api/v1/admin/health/db';

// ── Types matching DbHealthResponse from the API ───────────────────────────────

export interface IndexFragmentationRow {
  tableName: string;
  indexName: string;
  fragmentationPct: number;
  pageCount: number;
}

export interface TableSizeRow {
  tableName: string;
  rowCount: number;
  totalSizeMB: number;
  usedSizeMB: number;
}

export interface LongRunningQueryRow {
  sessionId: number;
  status: string;
  startTime: string;
  durationSec: number;
  command: string;
  queryText: string | null;
  waitType: string | null;
  blockingSessionId: number | null;
}

export interface DbHealthResponse {
  indexFragmentation: IndexFragmentationRow[];
  tableSizes: TableSizeRow[];
  longRunningQueries: LongRunningQueryRow[];
  collectedAt: string;
}

export class DbHealthApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'DbHealthApiError';
  }
}

export async function fetchDbHealth(): Promise<DbHealthResponse> {
  const res = await authorizedFetch(DB_HEALTH_URL, { cache: 'no-store' });
  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const body = (await res.json()) as { error?: string; detail?: string; title?: string };
      message = body.error ?? body.detail ?? body.title ?? message;
    } catch {
      /* non-JSON body */
    }
    throw new DbHealthApiError(res.status, message);
  }
  return res.json() as Promise<DbHealthResponse>;
}

/** Health data is a point-in-time snapshot — never served from the query cache. */
export function useDbHealth() {
  return useQuery<DbHealthResponse, DbHealthApiError>({
    queryKey: ['admin', 'health', 'db'],
    queryFn: fetchDbHealth,
    staleTime: 0,
    gcTime: 0,
    retry: false,
  });
}
