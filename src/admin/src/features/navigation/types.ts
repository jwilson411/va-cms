/**
 * Navigation menu editor types.
 * Issue #46 — BRD FR-NAV-01, FR-NAV-03.
 */

export interface NavigationMenu {
  id: number;
  name: string;
  handle: string;
  createdAt: string;
  updatedAt: string;
}

export interface NavItemAdminDto {
  id: number;
  menuId: number;
  parentItemId: number | null;
  label: string;
  url: string | null;
  contentEntryId: number | null;
  target: string;
  sortOrder: number;
  isVisible: boolean;
  depth: number;
}

export interface NavItemsResponse {
  menuId: number;
  handle: string;
  items: NavItemAdminDto[];
}

export interface UpsertItemRequest {
  label: string;
  url?: string | null;
  contentEntryId?: number | null;
  target?: string;
  sortOrder: number;
  isVisible?: boolean;
  parentItemId?: number | null;
}

export interface ReorderItemRequest {
  id: number;
  parentItemId: number | null;
  sortOrder: number;
}

export interface NavPreviewItemDto {
  id: number;
  label: string;
  url: string;
  target: string;
  isVisible: boolean;
  depth: number;
  children: NavPreviewItemDto[];
}

export interface NavigationMenuPreviewResponse {
  handle: string;
  name: string;
  items: NavPreviewItemDto[];
}

/** A nav item in tree form used by the editor component */
export interface NavTreeNode extends NavItemAdminDto {
  children: NavTreeNode[];
}
