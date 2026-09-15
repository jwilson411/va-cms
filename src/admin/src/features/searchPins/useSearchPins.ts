/**
 * API hooks for search pins management (issue #52 — FR-SEARCH-04).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../context/AuthContext';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface SearchPinItem {
  id: number;
  queryString: string;
  contentEntryId: number;
  createdAt: string;
  entrySlug: string | null;
  entryStatus: string | null;
  entryContentTypeId: number | null;
  entryTitle: string | null;
}

export interface SearchPinsList {
  items: SearchPinItem[];
}

export interface CreatePinPayload {
  queryString: string;
  contentEntryId: number;
}

// ── Query keys ────────────────────────────────────────────────────────────────

export const searchPinsKeys = {
  all: () => ['search-pins'] as const,
};

// ── useSearchPins ─────────────────────────────────────────────────────────────

export function useSearchPins() {
  const { authFetch } = useAuth();
  return useQuery<SearchPinsList>({
    queryKey: searchPinsKeys.all(),
    queryFn: async () => {
      const res = await authFetch('/api/v1/admin/search/pins');
      if (!res.ok) throw new Error(`Search pins fetch failed: ${res.status}`);
      return res.json() as Promise<SearchPinsList>;
    },
    staleTime: 30 * 1000,
  });
}

// ── useCreateSearchPin ────────────────────────────────────────────────────────

export function useCreateSearchPin() {
  const { authFetch } = useAuth();
  const queryClient = useQueryClient();

  return useMutation<{ id: number }, Error, CreatePinPayload>({
    mutationFn: async (payload) => {
      const res = await authFetch('/api/v1/admin/search/pins', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error((body as { error?: string }).error ?? `Create pin failed: ${res.status}`);
      }
      return res.json() as Promise<{ id: number }>;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: searchPinsKeys.all() });
    },
  });
}

// ── useDeleteSearchPin ────────────────────────────────────────────────────────

export function useDeleteSearchPin() {
  const { authFetch } = useAuth();
  const queryClient = useQueryClient();

  return useMutation<void, Error, number>({
    mutationFn: async (id) => {
      const res = await authFetch(`/api/v1/admin/search/pins/${id}`, {
        method: 'DELETE',
      });
      if (!res.ok && res.status !== 204) {
        throw new Error(`Delete pin failed: ${res.status}`);
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: searchPinsKeys.all() });
    },
  });
}
