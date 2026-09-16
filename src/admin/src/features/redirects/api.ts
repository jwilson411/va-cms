/**
 * Redirect management API hooks — TanStack Query v5.
 * Issue #48 — BRD FR-NAV-06.
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';
import type {
  CreateRedirectRequest,
  RedirectAdminDto,
  RedirectListResponse,
  UpdateRedirectRequest,
} from './types';

const BASE = '/api/v1/redirects';

async function apiFetch<T>(
  path: string,
  options?: RequestInit
): Promise<T> {
  // authorizedFetch attaches the in-memory JWT (the token is never in
  // sessionStorage — see AuthContext); without it every call was a bare 401.
  const res = await authorizedFetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options?.headers ?? {}),
    },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`API error ${res.status}: ${body}`);
  }
  if (res.status === 204) return undefined as unknown as T;
  return res.json() as Promise<T>;
}

// ── Queries ───────────────────────────────────────────────────────────────────

export function useRedirects(params?: {
  isActive?: boolean;
  page?: number;
  pageSize?: number;
}) {
  const sp = new URLSearchParams();
  if (params?.isActive !== undefined)
    sp.set('isActive', String(params.isActive));
  if (params?.page !== undefined)
    sp.set('page', String(params.page));
  if (params?.pageSize !== undefined)
    sp.set('pageSize', String(params.pageSize));

  const qs = sp.toString() ? `?${sp.toString()}` : '';

  return useQuery<RedirectListResponse>({
    queryKey: ['redirects', params],
    queryFn: () => apiFetch<RedirectListResponse>(`${BASE}${qs}`),
  });
}

export function useRedirect(id: number) {
  return useQuery<RedirectAdminDto>({
    queryKey: ['redirects', id],
    queryFn: () => apiFetch<RedirectAdminDto>(`${BASE}/${id}`),
    enabled: id > 0,
  });
}

// ── Mutations ─────────────────────────────────────────────────────────────────

export function useCreateRedirect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateRedirectRequest) =>
      apiFetch<RedirectAdminDto>(BASE, {
        method: 'POST',
        body: JSON.stringify(req),
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['redirects'] }),
  });
}

export function useUpdateRedirect(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateRedirectRequest) =>
      apiFetch<void>(`${BASE}/${id}`, {
        method: 'PATCH',
        body: JSON.stringify(req),
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['redirects'] }),
  });
}

export function useDeactivateRedirect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiFetch<void>(`${BASE}/${id}`, { method: 'DELETE' }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['redirects'] }),
  });
}
