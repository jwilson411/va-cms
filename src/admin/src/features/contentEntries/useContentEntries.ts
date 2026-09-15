import { useQuery } from '@tanstack/react-query';
import type {
  ContentEntryAdminPageDto,
  ContentEntryListFilters,
  SortBy,
  SortDir,
  ContentTypeSummaryForPicker,
} from './types';

const ADMIN_API = '/api/v1/admin/content-entries';
const CONTENT_TYPES_API = '/api/v1/admin/content-types';

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} fetching ${url}`);
  return res.json() as Promise<T>;
}

export interface UseContentEntriesParams {
  filters: ContentEntryListFilters;
  sortBy: SortBy;
  sortDir: SortDir;
  page: number;
  pageSize: number;
}

/** Fetch the paginated admin content entry list. */
export function useContentEntries({
  filters,
  sortBy,
  sortDir,
  page,
  pageSize,
}: UseContentEntriesParams) {
  const params = new URLSearchParams();
  if (filters.contentTypeId !== undefined)
    params.set('contentTypeId', String(filters.contentTypeId));
  if (filters.status)         params.set('status', filters.status);
  if (filters.authorSearch)   params.set('authorSearch', filters.authorSearch);
  if (filters.dateFrom)       params.set('dateFrom', filters.dateFrom);
  if (filters.dateTo)         params.set('dateTo', filters.dateTo);
  params.set('sortBy',   sortBy);
  params.set('sortDir',  sortDir);
  params.set('page',     String(page));
  params.set('pageSize', String(pageSize));

  const url = `${ADMIN_API}?${params.toString()}`;

  return useQuery<ContentEntryAdminPageDto>({
    queryKey: ['content-entries', filters, sortBy, sortDir, page, pageSize],
    queryFn:  () => fetchJson<ContentEntryAdminPageDto>(url),
  });
}

/** Fetch content types for the "Create new" picker. */
export function useContentTypesForPicker() {
  return useQuery<ContentTypeSummaryForPicker[]>({
    queryKey: ['content-types-picker'],
    queryFn:  () => fetchJson<ContentTypeSummaryForPicker[]>(CONTENT_TYPES_API),
    staleTime: 60_000,
  });
}
