/**
 * Types for the content entry list screen (issue #29, FR-AUTH-01).
 */

export interface ContentEntryAdminRowDto {
  id: number;
  slug: string;
  title: string;
  contentTypeName: string;
  authorDisplayName: string;
  status: string;
  lastModified: string; // ISO-8601 UTC
}

export interface ContentEntryAdminPageDto {
  totalRows: number;
  totalPages: number;
  page: number;
  pageSize: number;
  items: ContentEntryAdminRowDto[];
}

export interface ContentEntryListFilters {
  contentTypeId?: number;
  status?: string;
  authorSearch?: string;
  dateFrom?: string;
  dateTo?: string;
}

export type SortBy = 'Title' | 'Status' | 'UpdatedAt';
export type SortDir = 'ASC' | 'DESC';

export interface ContentTypeSummaryForPicker {
  id: number;
  name: string;
  displayName: string;
  description: string | null;
}
