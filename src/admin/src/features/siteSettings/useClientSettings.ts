/**
 * useClientSettings — runtime settings for the admin SPA (issue #148, epic #141).
 *
 * Loads GET /api/v1/settings/client once per session (react-query, long staleTime) and
 * exposes typed getters. Until the request resolves — or if it fails — the getters answer
 * with the same code defaults the API declares in SiteSettingDefinitions.cs, so nothing
 * in the UI has to wait on configuration.
 *
 * Keys here are the Admin/Public-scoped subset the SPA actually reads.
 */

import { useQuery } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

export const CLIENT_SETTINGS_API = '/api/v1/settings/client';

export const clientSettingKeys = {
  featureNotifications: 'features.notifications',
  featureSearchAnalytics: 'features.searchAnalytics',
  featureMediaUpload: 'features.mediaUpload',
  workflowRequireReturnComment: 'workflow.requireReturnComment',
  mediaMaxUploadBytes: 'media.maxUploadBytes',
  mediaAllowedMimeTypes: 'media.allowedMimeTypes',
  notificationsPollIntervalSeconds: 'notifications.pollIntervalSeconds',
  notificationsPanelLimit: 'notifications.panelLimit',
  adminAutoSaveIntervalSeconds: 'admin.autoSaveIntervalSeconds',
  adminContentListPageSize: 'admin.contentListPageSize',
  adminAuditLogPageSize: 'admin.auditLogPageSize',
  adminSearchAnalyticsPageSize: 'admin.searchAnalyticsPageSize',
  authIdleTimeoutMinutes: 'auth.idleTimeoutMinutes',
} as const;

export type ClientSettingKey = (typeof clientSettingKeys)[keyof typeof clientSettingKeys];

/** Code defaults — must match SiteSettingDefinitions.cs. */
export const CLIENT_SETTING_DEFAULTS: Record<ClientSettingKey, string> = {
  'features.notifications': 'true',
  'features.searchAnalytics': 'true',
  'features.mediaUpload': 'true',
  'workflow.requireReturnComment': 'true',
  'media.maxUploadBytes': '104857600',
  'media.allowedMimeTypes': '[]',
  'notifications.pollIntervalSeconds': '30',
  'notifications.panelLimit': '20',
  'admin.autoSaveIntervalSeconds': '60',
  'admin.contentListPageSize': '25',
  'admin.auditLogPageSize': '50',
  'admin.searchAnalyticsPageSize': '50',
  'auth.idleTimeoutMinutes': '15',
};

export type ClientSettingsMap = Record<string, string | null>;

export const clientSettingsQueryKey = ['clientSettings'] as const;

export interface ClientSettings {
  /** Raw map from the API (empty until loaded). */
  values: ClientSettingsMap;
  isLoading: boolean;
  isError: boolean;
  getString: (key: ClientSettingKey) => string;
  getInt: (key: ClientSettingKey) => number;
  getBool: (key: ClientSettingKey) => boolean;
  getStringList: (key: ClientSettingKey) => string[];
}

export function parseBool(raw: string | null | undefined, fallback: boolean): boolean {
  if (raw == null) return fallback;
  const v = raw.trim().toLowerCase();
  if (v === 'true' || v === '1' || v === 'yes' || v === 'on') return true;
  if (v === 'false' || v === '0' || v === 'no' || v === 'off') return false;
  return fallback;
}

export function parseInt10(raw: string | null | undefined, fallback: number): number {
  if (raw == null) return fallback;
  const n = Number.parseInt(raw.trim(), 10);
  return Number.isFinite(n) ? n : fallback;
}

export function parseStringList(raw: string | null | undefined, fallback: string[]): string[] {
  if (raw == null) return fallback;
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : fallback;
  } catch {
    return fallback;
  }
}

/** Build typed getters over a raw map; exported so tests and non-hook code can reuse it. */
export function buildClientSettings(
  values: ClientSettingsMap,
  state: { isLoading: boolean; isError: boolean },
): ClientSettings {
  const lookup = (key: ClientSettingKey): string | null =>
    Object.prototype.hasOwnProperty.call(values, key) ? values[key] : null;

  return {
    values,
    isLoading: state.isLoading,
    isError: state.isError,
    getString: (key) => lookup(key) ?? CLIENT_SETTING_DEFAULTS[key],
    getInt: (key) => parseInt10(lookup(key), parseInt10(CLIENT_SETTING_DEFAULTS[key], 0)),
    getBool: (key) => parseBool(lookup(key), parseBool(CLIENT_SETTING_DEFAULTS[key], false)),
    getStringList: (key) => parseStringList(lookup(key), parseStringList(CLIENT_SETTING_DEFAULTS[key], [])),
  };
}

export async function fetchClientSettings(): Promise<ClientSettingsMap> {
  const res = await authorizedFetch(CLIENT_SETTINGS_API);
  if (!res.ok) throw new Error(`Settings fetch failed: ${res.status}`);
  return (await res.json()) as ClientSettingsMap;
}

export function useClientSettings(): ClientSettings {
  const { data, isLoading, isError } = useQuery<ClientSettingsMap>({
    queryKey: clientSettingsQueryKey,
    queryFn: fetchClientSettings,
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  });

  return buildClientSettings(data ?? {}, { isLoading, isError });
}
