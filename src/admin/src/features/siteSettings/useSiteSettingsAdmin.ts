/**
 * Admin hooks for managing site settings (issue #143, epic #141).
 *
 * Endpoints (SystemAdmin):
 *   GET  /api/v1/admin/settings
 *   PUT  /api/v1/admin/settings/{key}        body: { value }
 *   POST /api/v1/admin/settings/{key}/reset
 *
 * Every mutation invalidates both the admin list and the client settings query so a
 * change is visible everywhere in the SPA without a reload.
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';
import { clientSettingsQueryKey } from './useClientSettings';

export const ADMIN_SETTINGS_API = '/api/v1/admin/settings';

export type SiteSettingDataType = 'string' | 'int' | 'bool' | 'json';
export type SiteSettingScope = 'Server' | 'Admin' | 'Public';

export interface SiteSettingDto {
  key: string;
  value: string | null;
  defaultValue: string | null;
  effectiveValue: string | null;
  isOverridden: boolean;
  dataType: SiteSettingDataType;
  category: string;
  scope: SiteSettingScope;
  description: string | null;
  sortOrder: number;
  updatedById: number | null;
  updatedByName: string | null;
  updatedAt: string;
}

export interface SiteSettingsListResponse {
  items: SiteSettingDto[];
  snapshotLoadedAtUtc: string | null;
}

export const adminSettingsQueryKey = ['adminSiteSettings'] as const;

async function readError(res: Response, fallback: string): Promise<string> {
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string') return body.error;
  } catch {
    /* not json */
  }
  return fallback;
}

export function useSiteSettingsAdmin() {
  return useQuery<SiteSettingsListResponse>({
    queryKey: adminSettingsQueryKey,
    queryFn: async () => {
      const res = await authorizedFetch(ADMIN_SETTINGS_API);
      if (!res.ok) throw new Error(`Failed to load settings: ${res.status}`);
      return (await res.json()) as SiteSettingsListResponse;
    },
  });
}

export function useSetSiteSetting() {
  const queryClient = useQueryClient();
  return useMutation<SiteSettingDto, Error, { key: string; value: string }>({
    mutationFn: async ({ key, value }) => {
      const res = await authorizedFetch(`${ADMIN_SETTINGS_API}/${encodeURIComponent(key)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ value }),
      });
      if (!res.ok) throw new Error(await readError(res, `Save failed: ${res.status}`));
      return (await res.json()) as SiteSettingDto;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminSettingsQueryKey });
      void queryClient.invalidateQueries({ queryKey: clientSettingsQueryKey });
    },
  });
}

export function useResetSiteSetting() {
  const queryClient = useQueryClient();
  return useMutation<SiteSettingDto, Error, string>({
    mutationFn: async (key) => {
      const res = await authorizedFetch(`${ADMIN_SETTINGS_API}/${encodeURIComponent(key)}/reset`, {
        method: 'POST',
      });
      if (!res.ok) throw new Error(await readError(res, `Reset failed: ${res.status}`));
      return (await res.json()) as SiteSettingDto;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminSettingsQueryKey });
      void queryClient.invalidateQueries({ queryKey: clientSettingsQueryKey });
    },
  });
}
