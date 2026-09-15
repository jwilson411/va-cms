/**
 * Hook for the content entry create/edit form (issue #30, FR-AUTH-01 / FR-AUTH-02).
 *
 * Responsibilities:
 *  - Fetch the content type definition (field schema) by type name
 *  - Fetch existing entry fields for edit mode (by entry id)
 *  - Manage fieldValues state
 *  - Inline validation on blur
 *  - Auto-save every 60 seconds (when in edit mode, entry has been saved once)
 *  - Save on explicit save/create
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import type {
  ContentTypeFormDefinitionDto,
  FieldValues,
  ValidationErrors,
  SaveResult,
  ContentEntryUpdateBody,
  ContentEntryCreateBody,
} from './formTypes';

const ADMIN_CONTENT_TYPES_API = '/api/v1/admin/content-types';
const CONTENT_API = '/api/v1/content';

const AUTO_SAVE_INTERVAL_MS = 60_000;

// ── Fetch helpers ─────────────────────────────────────────────────────────────

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} fetching ${url}`);
  return res.json() as Promise<T>;
}

async function patchJson<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} patching ${url}`);
  return res.json() as Promise<T>;
}

async function postJson<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} posting to ${url}`);
  return res.json() as Promise<T>;
}

// ── Validation ────────────────────────────────────────────────────────────────

export function validateField(
  fieldName: string,
  value: string | boolean | number | null,
  def: import('./formTypes').FieldDefinitionDto,
): string | null {
  if (def.required) {
    if (value === null || value === undefined || value === '') {
      return `${def.label} is required.`;
    }
  }
  if (def.maxLength !== null && typeof value === 'string' && value.length > def.maxLength) {
    return `${def.label} must be ${def.maxLength} characters or fewer.`;
  }
  return null;
}

// ── Hook ──────────────────────────────────────────────────────────────────────

export interface UseContentEntryFormOptions {
  /** The content type machine name (e.g. "standard_page") — always required. */
  contentTypeName: string;
  /** Entry id for edit mode. Undefined for create mode. */
  entryId?: number;
  /** Called with the new entry id after a successful create. */
  onCreated?: (id: number) => void;
}

export interface UseContentEntryFormResult {
  /** Field schema from the content type definition */
  fieldDefs: import('./formTypes').FieldDefinitionDto[];
  isSchemaLoading: boolean;
  schemaError: Error | null;

  /** Current field values */
  fieldValues: FieldValues;
  setFieldValue: (name: string, value: FieldValues[string]) => void;

  /** Validation errors keyed by field name (populated on blur + on submit) */
  validationErrors: ValidationErrors;
  /** Call on field blur to run inline validation for that field */
  validateOnBlur: (fieldName: string) => void;

  /** Slug state (derived from title if creating) */
  slug: string;
  setSlug: (s: string) => void;

  /** Save state */
  isSaving: boolean;
  saveError: string | null;
  lastSavedAt: string | null; // formatted as "HH:MM"
  saveResult: SaveResult | null;

  /** Explicit save/create */
  handleSave: () => Promise<void>;

  /** true when there are any validation errors */
  hasErrors: boolean;
}

/** Format a Date as HH:MM (24-hour local time) */
function formatHHMM(date: Date): string {
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
}

/** Auto-generate a slug from a title string */
function slugify(title: string): string {
  return title
    .toLowerCase()
    .replace(/[^a-z0-9\s/-]/g, '')
    .replace(/\s+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

export function useContentEntryForm({
  contentTypeName,
  entryId,
  onCreated,
}: UseContentEntryFormOptions): UseContentEntryFormResult {
  const queryClient = useQueryClient();
  const isEditMode = entryId !== undefined;

  // ── Schema query ────────────────────────────────────────────────────────────
  const {
    data: contentType,
    isLoading: isSchemaLoading,
    error: schemaError,
  } = useQuery<ContentTypeFormDefinitionDto>({
    queryKey: ['content-type-form-def', contentTypeName],
    queryFn: () =>
      fetchJson<ContentTypeFormDefinitionDto>(
        `${ADMIN_CONTENT_TYPES_API}/${encodeURIComponent(contentTypeName)}`,
      ),
    enabled: !!contentTypeName,
  });

  const fieldDefs = contentType?.fields ?? [];

  // ── Existing entry fields (edit mode) ───────────────────────────────────────
  const { data: existingEntry } = useQuery<{
    fieldsJson: string;
    slug: string;
  }>({
    queryKey: ['content-entry-form', entryId],
    queryFn: () =>
      fetchJson<{ fieldsJson: string; slug: string }>(
        `${CONTENT_API}/${entryId}`,
      ),
    enabled: isEditMode,
  });

  // ── Local form state ────────────────────────────────────────────────────────
  const [fieldValues, setFieldValues] = useState<FieldValues>({});
  const [validationErrors, setValidationErrors] = useState<ValidationErrors>({});
  const [slug, setSlug] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [lastSavedAt, setLastSavedAt] = useState<string | null>(null);
  const [saveResult, setSaveResult] = useState<SaveResult | null>(null);

  // Track whether the entry has been saved at least once (for auto-save logic)
  const savedEntryIdRef = useRef<number | undefined>(entryId);
  const fieldValuesRef = useRef(fieldValues);
  fieldValuesRef.current = fieldValues;

  // ── Hydrate from existing entry ─────────────────────────────────────────────
  useEffect(() => {
    if (existingEntry) {
      try {
        const parsed = JSON.parse(existingEntry.fieldsJson) as FieldValues;
        setFieldValues(parsed);
      } catch {
        setFieldValues({});
      }
      setSlug(existingEntry.slug);
    }
  }, [existingEntry]);

  // ── Auto-slug on title change (create mode only) ────────────────────────────
  useEffect(() => {
    if (!isEditMode) {
      const title = fieldValues['title'];
      if (typeof title === 'string' && title.length > 0) {
        setSlug(slugify(title));
      }
    }
  }, [fieldValues, isEditMode]);

  // ── Setters ─────────────────────────────────────────────────────────────────
  const setFieldValue = useCallback(
    (name: string, value: FieldValues[string]) => {
      setFieldValues((prev) => ({ ...prev, [name]: value }));
      // Clear validation error on change
      setValidationErrors((prev) => {
        if (!prev[name]) return prev;
        const next = { ...prev };
        delete next[name];
        return next;
      });
    },
    [],
  );

  // ── Inline validation on blur ────────────────────────────────────────────────
  const validateOnBlur = useCallback(
    (fieldName: string) => {
      const def = fieldDefs.find((f) => f.name === fieldName);
      if (!def) return;
      const err = validateField(fieldName, fieldValues[fieldName] ?? null, def);
      setValidationErrors((prev) => {
        if (!err) {
          const next = { ...prev };
          delete next[fieldName];
          return next;
        }
        return { ...prev, [fieldName]: err };
      });
    },
    [fieldDefs, fieldValues],
  );

  // ── Full validation (all fields) ─────────────────────────────────────────────
  const validateAll = useCallback((): ValidationErrors => {
    const errs: ValidationErrors = {};
    for (const def of fieldDefs) {
      const err = validateField(def.name, fieldValues[def.name] ?? null, def);
      if (err) errs[def.name] = err;
    }
    return errs;
  }, [fieldDefs, fieldValues]);

  // ── Save mutations ───────────────────────────────────────────────────────────
  const updateMutation = useMutation<void, Error, ContentEntryUpdateBody>({
    mutationFn: (body) =>
      patchJson<void>(
        `${CONTENT_API}/${savedEntryIdRef.current}`,
        body,
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ['content-entry-form', savedEntryIdRef.current],
      });
    },
  });

  const createMutation = useMutation<{ id: number }, Error, ContentEntryCreateBody>({
    mutationFn: (body) => postJson<{ id: number }>(CONTENT_API, body),
    onSuccess: (data) => {
      savedEntryIdRef.current = data.id;
      void queryClient.invalidateQueries({ queryKey: ['content-entries'] });
      onCreated?.(data.id);
    },
  });

  // ── Core save logic ──────────────────────────────────────────────────────────
  const doSave = useCallback(
    async (silent = false): Promise<void> => {
      const currentValues = fieldValuesRef.current;

      if (!silent) {
        const errs = validateAll();
        if (Object.keys(errs).length > 0) {
          setValidationErrors(errs);
          return;
        }
      }

      setIsSaving(true);
      setSaveError(null);

      try {
        const body: ContentEntryUpdateBody = {
          fieldsJson: JSON.stringify(currentValues),
        };

        if (savedEntryIdRef.current !== undefined) {
          // Edit mode — PATCH
          await updateMutation.mutateAsync(body);
        } else {
          // Create mode — POST
          // contentTypeId not available in this hook; caller must handle create
          // This path is reached when the entry was just created
        }

        const now = formatHHMM(new Date());
        setLastSavedAt(now);
        setSaveResult({ ok: true, savedAt: new Date().toISOString() });
      } catch (err) {
        const msg = err instanceof Error ? err.message : 'Save failed';
        setSaveError(msg);
        setSaveResult({ ok: false, error: msg });
      } finally {
        setIsSaving(false);
      }
    },
    [validateAll, updateMutation],
  );

  // ── Auto-save every 60 seconds (edit mode only) ──────────────────────────────
  useEffect(() => {
    if (!isEditMode && savedEntryIdRef.current === undefined) return;

    const interval = setInterval(() => {
      void doSave(true /* silent */);
    }, AUTO_SAVE_INTERVAL_MS);

    return () => clearInterval(interval);
  }, [isEditMode, doSave]);

  // ── Explicit save ────────────────────────────────────────────────────────────
  const handleSave = useCallback(async (): Promise<void> => {
    await doSave(false);
  }, [doSave]);

  const hasErrors = Object.keys(validationErrors).length > 0;

  return {
    fieldDefs,
    isSchemaLoading,
    schemaError: schemaError instanceof Error ? schemaError : null,
    fieldValues,
    setFieldValue,
    validationErrors,
    validateOnBlur,
    slug,
    setSlug,
    isSaving,
    saveError,
    lastSavedAt,
    saveResult,
    handleSave,
    hasErrors,
  };
}
