/**
 * Types for the content type browser (issue #26, FR-SCHEMA-06).
 */

export interface FieldSchemaDto {
  name: string;
  label: string;
  type: string;
  required: boolean;
  maxLength: number | null;
}

export interface ContentTypeSummaryDto {
  name: string;
  displayName: string;
  description: string | null;
  fieldCount: number;
  allowWorkflow: boolean;
  fields: FieldSchemaDto[];
}

export interface ContentTypeDetailDto {
  name: string;
  displayName: string;
  description: string | null;
  templateId: string | null;
  allowWorkflow: boolean;
  fields: FieldSchemaDto[];
}
