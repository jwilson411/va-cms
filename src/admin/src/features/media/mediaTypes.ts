/**
 * Media library type definitions (issue #42).
 */

/** Summary item in the GET /api/v1/media list response. */
export interface MediaAssetSummary {
  id:                number;
  fileName:          string;
  storagePath:       string;
  mimeType:          string;
  fileSizeBytes:     number;
  altText:           string | null;
  title:             string | null;
  width:             number | null;
  height:            number | null;
  webPStoragePath:   string | null;
  isVirusScanPassed: boolean | null;
  createdAt:         string;
}

/** Paginated list response from GET /api/v1/media. */
export interface MediaListDto {
  items:      MediaAssetSummary[];
  page:       number;
  pageSize:   number;
  totalItems: number;
}

/** Usage record within the detail response. */
export interface MediaUsageSummary {
  contentEntryId: number;
  fieldName:      string;
  slug:           string;
  status:         string;
  contentTypeId:  number;
}

/** Detail response from GET /api/v1/media/{id}. */
export interface MediaDetailDto {
  id:                number;
  fileName:          string;
  storagePath:       string;
  storageBackend:    string;
  mimeType:          string;
  fileSizeBytes:     number;
  altText:           string | null;
  title:             string | null;
  description:       string | null;
  tags:              string | null;
  width:             number | null;
  height:            number | null;
  webPStoragePath:   string | null;
  isVirusScanPassed: boolean | null;
  uploadedById:      number;
  createdAt:         string;
  updatedAt:         string;
  usages:            MediaUsageSummary[];
}

/** PATCH body for /api/v1/media/{id}. */
export interface MediaPatchBody {
  altText?:     string | null;
  title?:       string | null;
  description?: string | null;
  tags?:        string | null;
}
