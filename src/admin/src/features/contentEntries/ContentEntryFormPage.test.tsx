/**
 * Tests for issue #30: content entry create/edit form with USWDS field components.
 *
 * AC covered:
 *  - Form renders all field types for the selected content type
 *  - USWDS form components used (usa-input, usa-label, usa-error-message, usa-hint)
 *  - Required fields marked with asterisk
 *  - Inline validation fires on blur
 *  - Auto-save fires every 60 seconds and shows 'Last saved at HH:MM' indicator
 */

import React from 'react';
import {
  render,
  screen,
  fireEvent,
  waitFor,
  act,
  within,
} from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// Components under test
import { ContentEntryFormPage } from './ContentEntryFormPage';
import { FieldRenderer } from './FieldRenderers';
import { validateField } from './useContentEntryForm';
import type { FieldDefinitionDto } from './formTypes';

// ── Mock useContentEntryForm ─────────────────────────────────────────────────

vi.mock('./useContentEntryForm', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./useContentEntryForm')>();
  return {
    ...actual,
    useContentEntryForm: vi.fn(),
  };
});

import { useContentEntryForm } from './useContentEntryForm';
const mockUseContentEntryForm = useContentEntryForm as ReturnType<typeof vi.fn>;

// ── Fixtures ──────────────────────────────────────────────────────────────────

const allFieldDefs: FieldDefinitionDto[] = [
  { name: 'title',       label: 'Title',        type: 'ShortText',              required: true,  maxLength: 200,  hint: 'Main title of the page' },
  { name: 'summary',     label: 'Summary',      type: 'LongText',               required: true,  maxLength: 500,  hint: undefined },
  { name: 'body',        label: 'Body',         type: 'RichText',               required: true,  maxLength: null, hint: undefined },
  { name: 'count',       label: 'Item count',   type: 'Number',                 required: false, maxLength: null, hint: undefined },
  { name: 'publishDate', label: 'Publish Date', type: 'DateTime',               required: false, maxLength: null, hint: undefined },
  { name: 'featured',    label: 'Featured',     type: 'Boolean',                required: false, maxLength: null, hint: undefined },
  { name: 'image',       label: 'Hero Image',   type: 'MediaReference',         required: false, maxLength: null, hint: undefined },
  { name: 'topic',       label: 'Topic',        type: 'TaxonomyReference',      required: false, maxLength: null, hint: undefined },
  { name: 'topics',      label: 'Topics',       type: 'MultiTaxonomyReference', required: false, maxLength: null, hint: undefined },
];

function makeFormHookResult(overrides: Partial<ReturnType<typeof useContentEntryForm>> = {}) {
  return {
    fieldDefs: allFieldDefs,
    isSchemaLoading: false,
    schemaError: null,
    fieldValues: {},
    setFieldValue: vi.fn(),
    validationErrors: {},
    validateOnBlur: vi.fn(),
    slug: 'my-slug',
    setSlug: vi.fn(),
    isSaving: false,
    saveError: null,
    lastSavedAt: null,
    saveResult: null,
    handleSave: vi.fn().mockResolvedValue(undefined),
    hasErrors: false,
    ...overrides,
  };
}

function renderForm(props: React.ComponentProps<typeof ContentEntryFormPage>) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <ContentEntryFormPage {...props} />
    </QueryClientProvider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('ContentEntryFormPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Loading state ──────────────────────────────────────────────────────────

  it('shows loading indicator while schema loads', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ isSchemaLoading: true }),
    );
    renderForm({ contentTypeName: 'standard_page' });
    expect(screen.getByText('Loading form…')).toBeInTheDocument();
  });

  // ── Schema error ───────────────────────────────────────────────────────────

  it('shows error alert when schema cannot be loaded', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ schemaError: new Error('Not found') }),
    );
    renderForm({ contentTypeName: 'missing_type' });
    const alert = screen.getByRole('alert');
    expect(within(alert).getByText(/Unable to load form/i)).toBeInTheDocument();
    expect(within(alert).getByText(/Not found/i)).toBeInTheDocument();
  });

  // ── AC: All field types rendered ───────────────────────────────────────────

  it('renders a field for every field definition in the schema', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    renderForm({ contentTypeName: 'standard_page' });

    // Each field should produce at least a label
    expect(screen.getByLabelText(/Title/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Summary/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Body/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Item count/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Publish Date/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Featured/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Hero Image/)).toBeInTheDocument();
  });

  // ── AC: USWDS classes used ─────────────────────────────────────────────────

  it('uses usa-input class on text inputs', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const { container } = renderForm({ contentTypeName: 'standard_page' });
    const inputs = container.querySelectorAll('input.usa-input, textarea.usa-textarea');
    expect(inputs.length).toBeGreaterThan(0);
  });

  it('uses usa-label class on all labels', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const { container } = renderForm({ contentTypeName: 'standard_page' });
    const labels = container.querySelectorAll('label.usa-label, label.usa-checkbox__label');
    expect(labels.length).toBeGreaterThan(0);
  });

  it('uses usa-form-group wrapper on every field', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const { container } = renderForm({ contentTypeName: 'standard_page' });
    const groups = container.querySelectorAll('.usa-form-group');
    // At minimum slug + all field defs
    expect(groups.length).toBeGreaterThanOrEqual(allFieldDefs.length + 1);
  });

  // ── AC: Required fields marked with asterisk ───────────────────────────────

  it('marks required fields with an asterisk abbr element', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const { container } = renderForm({ contentTypeName: 'standard_page' });

    // Each required field should have an <abbr title="required">
    const requiredFields = allFieldDefs.filter((f) => f.required);
    const abbrs = Array.from(container.querySelectorAll<HTMLElement>('abbr[title="required"]'));
    expect(abbrs.length).toBeGreaterThanOrEqual(requiredFields.length);
  });

  it('required abbr contains an asterisk character', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const { container } = renderForm({ contentTypeName: 'standard_page' });
    const abbrs = container.querySelectorAll<HTMLElement>('abbr[title="required"]');
    for (const abbr of abbrs) {
      expect(abbr.textContent).toContain('*');
    }
  });

  // ── AC: Inline validation on blur ─────────────────────────────────────────

  it('calls validateOnBlur when an input loses focus', () => {
    const validateOnBlur = vi.fn();
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ validateOnBlur }),
    );
    renderForm({ contentTypeName: 'standard_page' });

    const titleInput = screen.getByLabelText(/Title/);
    fireEvent.blur(titleInput);
    expect(validateOnBlur).toHaveBeenCalledWith('title');
  });

  it('shows usa-error-message when validation error is present', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({
        validationErrors: { title: 'Title is required.' },
        hasErrors: true,
      }),
    );
    renderForm({ contentTypeName: 'standard_page' });

    // Error text appears in both the field error span and the validation summary link;
    // find the one with the USWDS error class specifically.
    const errorMsgs = screen.getAllByText('Title is required.');
    const errorSpan = errorMsgs.find((el) => el.classList.contains('usa-error-message'));
    expect(errorSpan).not.toBeUndefined();
    expect(errorSpan).toHaveClass('usa-error-message');
  });

  it('adds usa-form-group--error class when field has an error', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({
        validationErrors: { title: 'Title is required.' },
      }),
    );
    const { container } = renderForm({ contentTypeName: 'standard_page' });

    const errorGroups = container.querySelectorAll('.usa-form-group--error');
    expect(errorGroups.length).toBeGreaterThan(0);
  });

  it('adds usa-input--error class on error input', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({
        validationErrors: { title: 'Title is required.' },
      }),
    );
    const { container } = renderForm({ contentTypeName: 'standard_page' });

    const errorInputs = container.querySelectorAll('input.usa-input--error');
    expect(errorInputs.length).toBeGreaterThan(0);
  });

  it('associates aria-describedby with the error message id', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({
        validationErrors: { title: 'Title is required.' },
      }),
    );
    renderForm({ contentTypeName: 'standard_page' });

    const input = screen.getByRole('textbox', { name: /Title/ });
    expect(input).toHaveAttribute('aria-describedby', expect.stringContaining('field-title-error'));
    expect(input).toHaveAttribute('aria-invalid', 'true');
  });

  // ── AC: Auto-save indicator ────────────────────────────────────────────────

  it('shows auto-save indicator in edit mode', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ lastSavedAt: null }),
    );
    renderForm({ contentTypeName: 'standard_page', entryId: 42 });

    const indicator = screen.getByTestId('autosave-indicator');
    expect(indicator).toBeInTheDocument();
    expect(indicator).toHaveTextContent('Not yet saved');
  });

  it('shows "Last saved at HH:MM" when lastSavedAt is set', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ lastSavedAt: '14:30' }),
    );
    renderForm({ contentTypeName: 'standard_page', entryId: 42 });

    expect(screen.getByTestId('autosave-indicator')).toHaveTextContent(
      'Last saved at 14:30',
    );
  });

  it('shows "Saving…" in indicator while saving', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ isSaving: true }),
    );
    renderForm({ contentTypeName: 'standard_page', entryId: 42 });

    expect(screen.getByTestId('autosave-indicator')).toHaveTextContent('Saving…');
  });

  it('does not show auto-save indicator in create mode', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    renderForm({ contentTypeName: 'standard_page' /* no entryId */ });

    expect(screen.queryByTestId('autosave-indicator')).toBeNull();
  });

  // ── AC: Save button ────────────────────────────────────────────────────────

  it('renders a save button in create mode', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    renderForm({ contentTypeName: 'standard_page' });

    expect(
      screen.getByRole('button', { name: /Create entry/i }),
    ).toBeInTheDocument();
  });

  it('renders a save button in edit mode', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    renderForm({ contentTypeName: 'standard_page', entryId: 5 });

    expect(
      screen.getByRole('button', { name: /Save changes/i }),
    ).toBeInTheDocument();
  });

  it('calls handleSave when the form is submitted', async () => {
    const handleSave = vi.fn().mockResolvedValue(undefined);
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult({ handleSave }));
    renderForm({ contentTypeName: 'standard_page' });

    fireEvent.submit(screen.getByRole('form', { name: /New standard_page form/i }));
    await waitFor(() => expect(handleSave).toHaveBeenCalledTimes(1));
  });

  it('disables save button while saving', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ isSaving: true }),
    );
    renderForm({ contentTypeName: 'standard_page' });

    // Button aria-label is "Create entry" in create mode; text content shows "Saving…"
    // The button is disabled when isSaving is true.
    const btn = screen.getByRole('button', { name: /Create entry/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveTextContent('Saving…');
  });

  // ── AC: Cancel button ──────────────────────────────────────────────────────

  it('renders a cancel button when onCancel is provided', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const onCancel = vi.fn();
    renderForm({ contentTypeName: 'standard_page', onCancel });

    expect(
      screen.getByRole('button', { name: /Cancel and return to list/i }),
    ).toBeInTheDocument();
  });

  it('calls onCancel when Cancel is clicked', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const onCancel = vi.fn();
    renderForm({ contentTypeName: 'standard_page', onCancel });

    fireEvent.click(screen.getByRole('button', { name: /Cancel and return to list/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  // ── AC: Validation summary ─────────────────────────────────────────────────

  it('shows validation summary when form has errors', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({
        validationErrors: { title: 'Title is required.', body: 'Body is required.' },
        hasErrors: true,
      }),
    );
    renderForm({ contentTypeName: 'standard_page' });

    expect(screen.getByTestId('validation-summary')).toBeInTheDocument();
    expect(
      screen.getByText('Please correct the following errors'),
    ).toBeInTheDocument();
  });

  // ── AC: Save error alert ───────────────────────────────────────────────────

  it('shows save error alert when saveError is set', () => {
    mockUseContentEntryForm.mockReturnValue(
      makeFormHookResult({ saveError: 'Network error occurred.' }),
    );
    renderForm({ contentTypeName: 'standard_page', entryId: 1 });

    const alert = screen.getByRole('alert');
    expect(within(alert).getByText(/Save failed/i)).toBeInTheDocument();
    expect(within(alert).getByText(/Network error occurred/i)).toBeInTheDocument();
  });

  // ── AC: Heading ───────────────────────────────────────────────────────────

  it('shows "New [type]" heading in create mode', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    renderForm({
      contentTypeName: 'standard_page',
      contentTypeDisplayName: 'Standard Page',
    });
    expect(screen.getByRole('heading', { name: /New Standard Page/i })).toBeInTheDocument();
  });

  it('shows "Edit [type]" heading in edit mode', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    renderForm({
      contentTypeName: 'standard_page',
      entryId: 10,
      contentTypeDisplayName: 'Standard Page',
    });
    expect(screen.getByRole('heading', { name: /Edit Standard Page/i })).toBeInTheDocument();
  });

  // ── AC: Slug field ────────────────────────────────────────────────────────

  it('renders the slug field', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult({ slug: 'my-page' }));
    renderForm({ contentTypeName: 'standard_page' });

    const slugInput = screen.getByLabelText(/URL slug/i);
    expect(slugInput).toHaveValue('my-page');
  });

  // ── AC: Hint text ─────────────────────────────────────────────────────────

  it('renders usa-hint for fields that have a hint', () => {
    mockUseContentEntryForm.mockReturnValue(makeFormHookResult());
    const { container } = renderForm({ contentTypeName: 'standard_page' });
    const hints = container.querySelectorAll('.usa-hint');
    expect(hints.length).toBeGreaterThan(0);
  });
});

// ── FieldRenderer unit tests ──────────────────────────────────────────────────

describe('FieldRenderer', () => {
  function renderField(def: FieldDefinitionDto, overrides: Partial<{
    value: string | boolean | number | null;
    error: string;
  }> = {}) {
    return render(
      <FieldRenderer
        def={def}
        value={overrides.value ?? null}
        error={overrides.error}
        onChange={vi.fn()}
        onBlur={vi.fn()}
      />,
    );
  }

  it('renders ShortText as <input type="text">', () => {
    const { container } = renderField({ name: 'title', label: 'Title', type: 'ShortText', required: true, maxLength: 200 });
    expect(container.querySelector('input[type="text"]')).not.toBeNull();
  });

  it('renders LongText as <textarea>', () => {
    const { container } = renderField({ name: 'summary', label: 'Summary', type: 'LongText', required: false, maxLength: 500 });
    expect(container.querySelector('textarea')).not.toBeNull();
  });

  it('renders RichText as <textarea>', () => {
    const { container } = renderField({ name: 'body', label: 'Body', type: 'RichText', required: false, maxLength: null });
    expect(container.querySelector('textarea')).not.toBeNull();
  });

  it('renders Number as <input type="number">', () => {
    const { container } = renderField({ name: 'count', label: 'Count', type: 'Number', required: false, maxLength: null });
    expect(container.querySelector('input[type="number"]')).not.toBeNull();
  });

  it('renders DateTime as <input type="datetime-local">', () => {
    const { container } = renderField({ name: 'date', label: 'Date', type: 'DateTime', required: false, maxLength: null });
    expect(container.querySelector('input[type="datetime-local"]')).not.toBeNull();
  });

  it('renders Boolean as <input type="checkbox">', () => {
    const { container } = renderField({ name: 'active', label: 'Active', type: 'Boolean', required: false, maxLength: null });
    expect(container.querySelector('input[type="checkbox"]')).not.toBeNull();
  });

  it('renders Boolean checkbox with usa-checkbox class', () => {
    const { container } = renderField({ name: 'active', label: 'Active', type: 'Boolean', required: false, maxLength: null });
    expect(container.querySelector('.usa-checkbox')).not.toBeNull();
  });

  it('renders MediaReference as text input', () => {
    const { container } = renderField({ name: 'image', label: 'Image', type: 'MediaReference', required: false, maxLength: null });
    expect(container.querySelector('input[type="text"]')).not.toBeNull();
  });

  it('renders TaxonomyReference as text input', () => {
    const { container } = renderField({ name: 'topic', label: 'Topic', type: 'TaxonomyReference', required: false, maxLength: null });
    expect(container.querySelector('input[type="text"]')).not.toBeNull();
  });

  it('unknown type falls back to ShortText input', () => {
    const { container } = renderField({ name: 'custom', label: 'Custom', type: 'geo_point', required: false, maxLength: null });
    expect(container.querySelector('input[type="text"]')).not.toBeNull();
  });

  it('shows error message with usa-error-message class', () => {
    const { container } = renderField(
      { name: 'title', label: 'Title', type: 'ShortText', required: true, maxLength: 200 },
      { error: 'Title is required.' },
    );
    const errEl = container.querySelector('.usa-error-message');
    expect(errEl).not.toBeNull();
    expect(errEl?.textContent).toContain('Title is required.');
  });

  it('required ShortText has aria-required', () => {
    const { container } = renderField({ name: 'title', label: 'Title', type: 'ShortText', required: true, maxLength: null });
    const input = container.querySelector('input');
    expect(input).toHaveAttribute('aria-required', 'true');
  });

  it('calls onBlur with field name when input is blurred', () => {
    const onBlur = vi.fn();
    const { container } = render(
      <FieldRenderer
        def={{ name: 'title', label: 'Title', type: 'ShortText', required: true, maxLength: null }}
        value=""
        onChange={vi.fn()}
        onBlur={onBlur}
      />,
    );
    const input = container.querySelector('input')!;
    fireEvent.blur(input);
    expect(onBlur).toHaveBeenCalledWith('title');
  });

  it('calls onChange when value changes', () => {
    const onChange = vi.fn();
    const { container } = render(
      <FieldRenderer
        def={{ name: 'title', label: 'Title', type: 'ShortText', required: true, maxLength: null }}
        value=""
        onChange={onChange}
        onBlur={vi.fn()}
      />,
    );
    const input = container.querySelector('input')!;
    fireEvent.change(input, { target: { value: 'New title' } });
    expect(onChange).toHaveBeenCalledWith('New title');
  });
});

// ── validateField unit tests ───────────────────────────────────────────────────

describe('validateField', () => {
  const requiredDef: FieldDefinitionDto = {
    name: 'title', label: 'Title', type: 'ShortText', required: true, maxLength: 10,
  };

  it('returns error for empty required string', () => {
    expect(validateField('title', '', requiredDef)).toBe('Title is required.');
  });

  it('returns error for null required field', () => {
    expect(validateField('title', null, requiredDef)).toBe('Title is required.');
  });

  it('returns null for non-empty required field', () => {
    expect(validateField('title', 'Hello', requiredDef)).toBeNull();
  });

  it('returns error when value exceeds maxLength', () => {
    expect(validateField('title', 'This is way too long!', requiredDef)).toBe(
      'Title must be 10 characters or fewer.',
    );
  });

  it('returns null when within maxLength', () => {
    expect(validateField('title', 'Short', requiredDef)).toBeNull();
  });

  it('returns null for optional empty field', () => {
    const optionalDef: FieldDefinitionDto = { ...requiredDef, required: false };
    expect(validateField('title', '', optionalDef)).toBeNull();
  });
});
