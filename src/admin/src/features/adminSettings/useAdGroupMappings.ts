/**
 * API hooks for AD Group Role Mapping (story #67).
 *
 * Endpoints:
 *   GET    /api/v1/admin/settings/ad-group-mappings
 *   POST   /api/v1/admin/settings/ad-group-mappings
 *   DELETE /api/v1/admin/settings/ad-group-mappings/{id}
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../context/AuthContext';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface AdGroupRoleMappingRow {
  id: number;
  adGroup: string;
  roleId: number;
  roleName: string;
  createdById: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateMappingRequest {
  adGroup: string;
  roleId: number;
}

// ── Query keys ────────────────────────────────────────────────────────────────

const AD_GROUP_MAPPINGS_KEY = ['adGroupMappings'] as const;

// ── Hooks ─────────────────────────────────────────────────────────────────────

export function useAdGroupMappings() {
  const { authFetch } = useAuth();
  return useQuery<AdGroupRoleMappingRow[]>({
    queryKey: AD_GROUP_MAPPINGS_KEY,
    queryFn: async () => {
      const res = await authFetch('/api/v1/admin/settings/ad-group-mappings');
      if (!res.ok) throw new Error(`Failed to load mappings: ${res.status}`);
      return res.json() as Promise<AdGroupRoleMappingRow[]>;
    },
  });
}

export function useCreateAdGroupMapping() {
  const { authFetch } = useAuth();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: CreateMappingRequest) => {
      const res = await authFetch('/api/v1/admin/settings/ad-group-mappings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `Create failed: ${res.status}`);
      }
      return res.json();
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: AD_GROUP_MAPPINGS_KEY });
    },
  });
}

export function useDeleteAdGroupMapping() {
  const { authFetch } = useAuth();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const res = await authFetch(
        `/api/v1/admin/settings/ad-group-mappings/${id}`,
        { method: 'DELETE' },
      );
      if (!res.ok) throw new Error(`Delete failed: ${res.status}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: AD_GROUP_MAPPINGS_KEY });
    },
  });
}
