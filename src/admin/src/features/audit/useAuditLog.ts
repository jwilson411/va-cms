/**
 * useAuditLog — TanStack Query hooks for the audit log admin API (issue #57).
 *
 * API endpoints:
 *   GET  /api/v1/admin/audit              — paged list with filters
 *   GET  /api/v1/admin/audit/export.csv   — CSV export of filtered results
 */

import { useQuery } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

const API_BASE = '/api/v1/admin';

// ── Types ──────────────────────────────────────────────────────────────────────

export interface AuditLogRow {
  id: number;
  actorId: number | null;
  actorEmail: string | null;
  actorDisplayName: string | null;
  entityType: string;
  entityId: string;
  action: string;
  diffJson: string | null;
  /** #165 (NIST AU-3): where the request came from and whether it succeeded. */
  ipAddress: string | null;
  userAgent: string | null;
  correlationId: string | null;
  outcome: 'Success' | 'Failure';
  createdAt: string;
}

export interface AuditLogPage {
  items: AuditLogRow[];
  totalItems: number;
  page: number;
  pageSize: number;
}

export interface AuditLogFilters {
  actorId?: number | null;
  action?: string;
  entityType?: string;
  fromDate?: string;
  toDate?: string;
  outcome?: 'Success' | 'Failure';
  ipAddress?: string;
}

// ── Helper ─────────────────────────────────────────────────────────────────────

async function apiFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await authorizedFetch(url, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(`API error ${res.status}: ${text}`);
  }
  return res.json() as Promise<T>;
}

function filterParams(filters: AuditLogFilters): URLSearchParams {
  const p = new URLSearchParams();
  if (filters.actorId != null) p.set('actorId', String(filters.actorId));
  if (filters.action)          p.set('action', filters.action);
  if (filters.entityType)      p.set('entityType', filters.entityType);
  if (filters.fromDate)        p.set('fromDate', filters.fromDate);
  if (filters.toDate)          p.set('toDate', filters.toDate);
  if (filters.outcome)         p.set('outcome', filters.outcome);
  if (filters.ipAddress)       p.set('ipAddress', filters.ipAddress);
  return p;
}

function buildParams(filters: AuditLogFilters, page: number, pageSize: number): URLSearchParams {
  const p = filterParams(filters);
  p.set('page', String(page));
  p.set('pageSize', String(pageSize));
  return p;
}

// ── Query hook ─────────────────────────────────────────────────────────────────

export function useAuditLog(
  filters: AuditLogFilters,
  page: number,
  pageSize: number = 50,
): ReturnType<typeof useQuery<AuditLogPage>> {
  const params = buildParams(filters, page, pageSize);
  return useQuery<AuditLogPage>({
    queryKey: ['admin-audit', filters, page, pageSize],
    queryFn: () => apiFetch<AuditLogPage>(`${API_BASE}/audit?${params.toString()}`),
  });
}

// ── CSV export helper ─────────────────────────────────────────────────────────

export function buildExportUrl(filters: AuditLogFilters): string {
  const qs = filterParams(filters).toString();
  return `${API_BASE}/audit/export.csv${qs ? '?' + qs : ''}`;
}
