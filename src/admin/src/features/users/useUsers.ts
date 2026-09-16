/**
 * useUsers — TanStack Query hooks for the user directory admin API (issue #56).
 *
 * API endpoints:
 *   GET  /api/v1/admin/users               — list active users
 *   GET  /api/v1/admin/users/{id}           — user detail with roles
 *   POST /api/v1/admin/users/{id}/roles     — assign role
 *   DELETE /api/v1/admin/users/{id}/roles/{roleId} — revoke role
 *   POST /api/v1/admin/users/{id}/deactivate — deactivate user
 *   GET  /api/v1/admin/roles               — list all roles
 *   GET  /api/v1/admin/sections            — list content sections
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

const API_BASE = '/api/v1/admin';

// ── Types ──────────────────────────────────────────────────────────────────────

export interface UserRow {
  id: number;
  email: string;
  displayName: string;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
}

export interface UserRoleDetail {
  roleId: number;
  roleName: string;
  roleDisplayName: string;
  sectionId: number | null;
  sectionName: string | null;
  sectionSlugPrefix: string | null;
}

export interface UserDetail {
  id: number;
  email: string;
  displayName: string;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  roles: UserRoleDetail[];
}

export interface RoleRow {
  id: number;
  name: string;
  displayName: string;
  isSystemRole: boolean;
}

export interface ContentSectionRow {
  id: number;
  name: string;
  slugPrefix: string;
  parentSectionId: number | null;
}

// ── Helper ─────────────────────────────────────────────────────────────────────

async function apiFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await authorizedFetch(url, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(`API error ${res.status}: ${text}`);
  }
  if (res.status === 204) return undefined as unknown as T;
  return res.json() as Promise<T>;
}

// ── Query hooks ────────────────────────────────────────────────────────────────

export function useUsers(search?: string): ReturnType<typeof useQuery<UserRow[]>> {
  const params = search ? `?search=${encodeURIComponent(search)}&pageSize=100` : '?pageSize=100';
  return useQuery<UserRow[]>({
    queryKey: ['admin-users', search],
    queryFn: () => apiFetch<UserRow[]>(`${API_BASE}/users${params}`),
  });
}

export function useUserDetail(userId: number | null): ReturnType<typeof useQuery<UserDetail>> {
  return useQuery<UserDetail>({
    queryKey: ['admin-user', userId],
    queryFn: () => apiFetch<UserDetail>(`${API_BASE}/users/${userId}`),
    enabled: userId !== null,
  });
}

export function useRoles(): ReturnType<typeof useQuery<RoleRow[]>> {
  return useQuery<RoleRow[]>({
    queryKey: ['admin-roles'],
    queryFn: () => apiFetch<RoleRow[]>(`${API_BASE}/roles`),
    staleTime: 60_000, // roles change rarely
  });
}

export function useSections(): ReturnType<typeof useQuery<ContentSectionRow[]>> {
  return useQuery<ContentSectionRow[]>({
    queryKey: ['admin-sections'],
    queryFn: () => apiFetch<ContentSectionRow[]>(`${API_BASE}/sections`),
    staleTime: 60_000,
  });
}

// ── Mutation hooks ─────────────────────────────────────────────────────────────

export function useAssignRole(userId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ roleId, sectionId }: { roleId: number; sectionId: number | null }) =>
      apiFetch<void>(`${API_BASE}/users/${userId}/roles`, {
        method: 'POST',
        body: JSON.stringify({ roleId, sectionId }),
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['admin-user', userId] });
    },
  });
}

export function useRevokeRole(userId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ roleId, sectionId }: { roleId: number; sectionId: number | null }) => {
      const qs = sectionId !== null ? `?sectionId=${sectionId}` : '';
      return apiFetch<void>(`${API_BASE}/users/${userId}/roles/${roleId}${qs}`, {
        method: 'DELETE',
      });
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['admin-user', userId] });
    },
  });
}

export function useDeactivateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (userId: number) =>
      apiFetch<void>(`${API_BASE}/users/${userId}/deactivate`, { method: 'POST' }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['admin-users'] });
    },
  });
}
