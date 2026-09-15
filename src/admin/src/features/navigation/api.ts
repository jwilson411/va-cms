/**
 * Navigation API hooks — TanStack Query v5.
 * Issue #46 — BRD FR-NAV-01, FR-NAV-03.
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  NavItemAdminDto,
  NavItemsResponse,
  NavigationMenu,
  NavigationMenuPreviewResponse,
  ReorderItemRequest,
  UpsertItemRequest,
} from './types';

const BASE = '/api/v1/navigation';

async function apiFetch<T>(
  path: string,
  options?: RequestInit
): Promise<T> {
  const token = sessionStorage.getItem('cms_access_token') ?? '';
  const res = await fetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
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

// ── Menus ─────────────────────────────────────────────────────────────────────

export function useNavigationMenus() {
  return useQuery<NavigationMenu[]>({
    queryKey: ['navigation', 'menus'],
    queryFn: () => apiFetch<NavigationMenu[]>(BASE),
  });
}

export function useCreateMenu() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, handle }: { name: string; handle: string }) =>
      apiFetch<NavigationMenu>(BASE, {
        method: 'POST',
        body: JSON.stringify({ name, handle }),
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['navigation'] }),
  });
}

export function useUpdateMenu(handle: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (name: string) =>
      apiFetch<void>(`${BASE}/${handle}`, {
        method: 'PATCH',
        body: JSON.stringify({ name }),
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['navigation'] }),
  });
}

export function useDeleteMenu(handle: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<void>(`${BASE}/${handle}`, { method: 'DELETE' }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['navigation'] }),
  });
}

// ── Items ─────────────────────────────────────────────────────────────────────

export function useMenuItems(handle: string) {
  return useQuery<NavItemsResponse>({
    queryKey: ['navigation', handle, 'items'],
    queryFn: () => apiFetch<NavItemsResponse>(`${BASE}/${handle}/items`),
    enabled: Boolean(handle),
  });
}

export function useCreateItem(handle: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpsertItemRequest) =>
      apiFetch<NavItemAdminDto>(`${BASE}/${handle}/items`, {
        method: 'POST',
        body: JSON.stringify(req),
      }),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: ['navigation', handle] }),
  });
}

export function useUpdateItem(handle: string, id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpsertItemRequest) =>
      apiFetch<void>(`${BASE}/${handle}/items/${id}`, {
        method: 'PATCH',
        body: JSON.stringify(req),
      }),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: ['navigation', handle] }),
  });
}

export function useDeleteItem(handle: string, id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<void>(`${BASE}/${handle}/items/${id}`, { method: 'DELETE' }),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: ['navigation', handle] }),
  });
}

export function useBulkReorder(handle: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (items: ReorderItemRequest[]) =>
      apiFetch<void>(`${BASE}/${handle}/reorder`, {
        method: 'POST',
        body: JSON.stringify(items),
      }),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: ['navigation', handle] }),
  });
}

// ── Preview ───────────────────────────────────────────────────────────────────

export function useMenuPreview(handle: string, enabled: boolean) {
  return useQuery<NavigationMenuPreviewResponse>({
    queryKey: ['navigation', handle, 'preview'],
    queryFn: () =>
      apiFetch<NavigationMenuPreviewResponse>(`${BASE}/${handle}/preview`),
    enabled: Boolean(handle) && enabled,
  });
}
