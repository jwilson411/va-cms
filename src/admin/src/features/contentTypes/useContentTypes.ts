import { useQuery } from '@tanstack/react-query';
import type { ContentTypeSummaryDto, ContentTypeDetailDto } from './types';
import { authorizedFetch } from '../../lib/authorizedFetch';

const API_BASE = '/api/v1/admin/content-types';

async function fetchJson<T>(url: string): Promise<T> {
  const res = await authorizedFetch(url, {
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} fetching ${url}`);
  return res.json() as Promise<T>;
}

/** Fetches the list of all registered content types. */
export function useContentTypes() {
  return useQuery<ContentTypeSummaryDto[]>({
    queryKey: ['content-types'],
    queryFn: () => fetchJson<ContentTypeSummaryDto[]>(API_BASE),
  });
}

/** Fetches the full field schema for a single content type by machine name. */
export function useContentTypeDetail(name: string) {
  return useQuery<ContentTypeDetailDto>({
    queryKey: ['content-types', name],
    queryFn: () => fetchJson<ContentTypeDetailDto>(`${API_BASE}/${encodeURIComponent(name)}`),
    enabled: Boolean(name),
  });
}
