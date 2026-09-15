/**
 * useContentVersions — TanStack Query hooks for content versioning (issue #32).
 *
 * Provides:
 *   useContentVersions(entryId)  — list all versions, newest first
 *   useRestoreVersion()           — mutation: POST .../versions/{versionId}/restore
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface ContentVersionSummary {
  id: number;
  versionNumber: number;
  authorName: string;
  createdAt: string;   // ISO 8601
  changeNote: string | null;
  status: string;
}

export interface ContentVersionRestoreResponse {
  newVersionId: number;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    ...init,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} fetching ${url}`);
  return res.json() as Promise<T>;
}

// ── Hooks ─────────────────────────────────────────────────────────────────────

/** Fetch the version list for a content entry. */
export function useContentVersions(entryId: number) {
  return useQuery<ContentVersionSummary[]>({
    queryKey: ['content-versions', entryId],
    queryFn: () =>
      fetchJson<ContentVersionSummary[]>(
        `/api/v1/content/${entryId}/versions?pageSize=100`
      ),
    enabled: entryId > 0,
  });
}

/** Restore a prior version. Invalidates the version list on success. */
export function useRestoreVersion(entryId: number) {
  const queryClient = useQueryClient();
  return useMutation<ContentVersionRestoreResponse, Error, number>({
    mutationFn: (versionId: number) =>
      fetchJson<ContentVersionRestoreResponse>(
        `/api/v1/content/${entryId}/versions/${versionId}/restore`,
        { method: 'POST' }
      ),
    onSuccess: () => {
      // Re-fetch the version list and the entry itself
      void queryClient.invalidateQueries({ queryKey: ['content-versions', entryId] });
      void queryClient.invalidateQueries({ queryKey: ['content-entry', entryId] });
    },
  });
}
