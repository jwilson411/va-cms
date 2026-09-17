/**
 * Tests for useClientSettings (issue #148, epic #141).
 *
 *   - Getters answer with the code defaults before the API responds or when it fails.
 *   - Values from GET /api/v1/settings/client override the defaults with correct typing.
 */

import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import type { ReactNode } from 'react';
import {
  buildClientSettings,
  clientSettingKeys,
  CLIENT_SETTINGS_API,
  useClientSettings,
} from './useClientSettings';

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('buildClientSettings', () => {
  it('returns code defaults for missing keys', () => {
    const s = buildClientSettings({}, { isLoading: false, isError: false });
    expect(s.getInt(clientSettingKeys.notificationsPollIntervalSeconds)).toBe(30);
    expect(s.getInt(clientSettingKeys.adminContentListPageSize)).toBe(25);
    expect(s.getBool(clientSettingKeys.featureNotifications)).toBe(true);
    expect(s.getString(clientSettingKeys.mediaMaxUploadBytes)).toBe('104857600');
  });

  it('parses overrides by type and falls back on garbage', () => {
    const s = buildClientSettings(
      {
        'features.notifications': 'false',
        'notifications.pollIntervalSeconds': '5',
        'admin.auditLogPageSize': 'lots',
        'media.allowedMimeTypes': '["image/png"]',
      },
      { isLoading: false, isError: false },
    );
    expect(s.getBool(clientSettingKeys.featureNotifications)).toBe(false);
    expect(s.getInt(clientSettingKeys.notificationsPollIntervalSeconds)).toBe(5);
    expect(s.getInt(clientSettingKeys.adminAuditLogPageSize)).toBe(50);
    expect(s.getStringList(clientSettingKeys.mediaAllowedMimeTypes)).toEqual(['image/png']);
  });
});

describe('useClientSettings', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    globalThis.fetch = vi.fn();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('fetches /api/v1/settings/client and exposes the values', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue({
      ok: true,
      json: async () => ({ 'admin.contentListPageSize': '40', 'features.mediaUpload': 'false' }),
    } as Response);

    const { result } = renderHook(() => useClientSettings(), { wrapper });

    // Defaults while loading
    expect(result.current.getInt(clientSettingKeys.adminContentListPageSize)).toBe(25);

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(vi.mocked(globalThis.fetch)).toHaveBeenCalledWith(CLIENT_SETTINGS_API, undefined);
    expect(result.current.getInt(clientSettingKeys.adminContentListPageSize)).toBe(40);
    expect(result.current.getBool(clientSettingKeys.featureMediaUpload)).toBe(false);
  });

  it('keeps defaults when the request fails', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue({ ok: false, status: 500 } as Response);

    const { result } = renderHook(() => useClientSettings(), { wrapper });
    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.getBool(clientSettingKeys.featureNotifications)).toBe(true);
    expect(result.current.getInt(clientSettingKeys.adminAutoSaveIntervalSeconds)).toBe(60);
  });
});
