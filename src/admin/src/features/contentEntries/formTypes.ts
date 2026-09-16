/**
 * Types for the content entry create/edit form (issue #30, FR-AUTH-01, FR-AUTH-02).
 */

/** Definition of a single field in a content type schema. */
export interface FieldDefinitionDto {
  name: string;
  label: string;
  type: FieldTypeValue;
  required: boolean;
  maxLength: number | null;
  hint?: string;
}

/**
 * All built-in field types the form renderer supports.
 * Maps to FieldType enum values from the API.
 */
export type FieldTypeValue =
  | 'ShortText'
  | 'LongText'
  | 'RichText'
  | 'Number'
  | 'DateTime'
  | 'Boolean'
  | 'MediaReference'
  | 'TaxonomyReference'
  | 'MultiTaxonomyReference'
  | 'RelatedEntry'
  | 'MultiRelatedEntry'
  | 'RepeatableGroup'
  | string; // allow custom types

/** Content type definition returned by GET /api/v1/admin/content-types/{name} */
export interface ContentTypeFormDefinitionDto {
  name: string;
  displayName: string;
  description: string | null;
  templateId: string | null;
  allowWorkflow: boolean;
  fields: FieldDefinitionDto[];
}

/** Map of field name → raw value (string, boolean, null, etc.) */
export type FieldValues = Record<string, string | boolean | number | null>;

/** Result of a save attempt. */
export interface SaveResult {
  ok: boolean;
  error?: string;
  savedAt?: string; // ISO-8601
}

/** Shape of API PATCH /api/v1/content/{id} body. */
export interface ContentEntryUpdateBody {
  fieldsJson: string;
  changeNote?: string;
}

/**
 * Shape of API POST /api/v1/content body (create).
 * The API resolves contentTypeName to its ContentType row (creating it from the
 * registry on first use) and stores fieldsJson as the entry's initial version.
 */
export interface ContentEntryCreateBody {
  contentTypeName: string;
  slug: string;
  fieldsJson: string;
  locale?: string;
}

/** Shape of a validation error map: fieldName → message */
export type ValidationErrors = Record<string, string>;
