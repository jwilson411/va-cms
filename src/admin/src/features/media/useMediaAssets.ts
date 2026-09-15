/**
 * TanStack Query hooks for media library (issue #42).
 */

import { useQuery } from '@tanstack/react-query';
import type { MediaListDto, MediaDetailDto } from './mediaTypes';

const MEDIA_API = '/api/v1/media';

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url, { credentials: 'include' });
  if (!res.ok) throw new Error(`HTTP ${res.status} fetching ${url}`);
  return res.json() as Promise<T>;
}

export interface UseMediaAssetsParams {
  q?:        string;
  mimeType?: string;
  page?:     number;
  pageSize?: number;
}

/**
 * Fetch paginated media asset list.
 * AC2: search by filename/alt text via q param.
 * AC3: filter by MIME type prefix via mimeType param.
 */
export function useMediaAssets({
  q,
  mimeType,
  page = 1,
  pageSize = 48,
}: UseMediaAssetsParams) {
  const params = new URLSearchParams();
  if (q)        params.set('q', q);
  if (mimeType) params.set('mimeType', mimeType);
  params.set('page',     String(page));
  params.set('pageSize', String(pageSize));

  const url = `${MEDIA_API}?${params.toString()}`;

  return useQuery<MediaListDto>({
    queryKey: ['media-assets', q, mimeType, page, pageSize],
    queryFn:  () => fetchJson<MediaListDto>(url),
  });
}

/**
 * Fetch full asset detail (with usage list).
 * AC4: preview, alt text, usage list, metadata.
 */
export function useMediaDetail(id: number | null) {
  return useQuery<MediaDetailDto>({
    queryKey: ['media-detail', id],
    queryFn:  () => fetchJson<MediaDetailDto>(`${MEDIA_API}/${id!}`),
    enabled:  id !== null,
  });
}
