/**
 * Redirect management types.
 * Issue #48 — BRD FR-NAV-06.
 */

export interface RedirectAdminDto {
  id: number;
  fromPath: string;
  toPath: string;
  statusCode: number;
  isActive: boolean;
  createdById: number;
  createdByEmail: string | null;
  createdByDisplayName: string | null;
  createdAt: string;
}

export interface RedirectListResponse {
  items: RedirectAdminDto[];
  totalRows: number;
  page: number;
  pageSize: number;
}

export interface CreateRedirectRequest {
  fromPath: string;
  toPath: string;
  statusCode: number;
}

export interface UpdateRedirectRequest {
  fromPath: string;
  toPath: string;
  statusCode: number;
}
