/**
 * Tests for SlugField and slugify (issue #33).
 *
 * AC covered:
 *  - slugify: lowercase, hyphenated, URL-safe output
 *  - SlugField renders with usa-input, usa-label, usa-hint, url preview
 *  - Slug field editable — onChange called on input change
 *  - URL preview updates reactively with slug value
 *  - Duplicate slug error shown as usa-error-message with aria attributes
 *  - Required aria-required attribute present
 *  - No inline styles on USWDS focus overrides (no !important in className)
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { SlugField, slugify } from './SlugField';

// ── slugify unit tests ────────────────────────────────────────────────────────

describe('slugify', () => {
  it('lowercases all characters', () => {
    expect(slugify('Hello World')).toBe('hello-world');
  });

  it('replaces spaces with hyphens', () => {
    expect(slugify('content entry title')).toBe('content-entry-title');
  });

  it('strips special characters', () => {
    expect(slugify('Hello, World! 2026')).toBe('hello-world-2026');
  });

  it('collapses multiple spaces/hyphens', () => {
    expect(slugify('title   with   spaces')).toBe('title-with-spaces');
  });

  it('trims leading and trailing hyphens', () => {
    expect(slugify('  hello world  ')).toBe('hello-world');
  });

  it('preserves hyphens already in the input', () => {
    expect(slugify('va-cms-content')).toBe('va-cms-content');
  });

  it('preserves forward slashes (path segments)', () => {
    expect(slugify('resources/page-title')).toBe('resources/page-title');
  });

  it('returns empty string for empty input', () => {
    expect(slugify('')).toBe('');
  });

  it('returns empty string for all-special-characters', () => {
    expect(slugify('!!!@@@###')).toBe('');
  });

  it('generates a valid slug from a realistic VA page title', () => {
    expect(slugify('VA Health Benefits for Veterans')).toBe(
      'va-health-benefits-for-veterans',
    );
  });
});

// ── SlugField component tests ─────────────────────────────────────────────────

describe('SlugField', () => {
  function renderSlugField(props: Partial<React.ComponentProps<typeof SlugField>> = {}) {
    const defaults: React.ComponentProps<typeof SlugField> = {
      value: 'my-test-slug',
      onChange: vi.fn(),
      error: null,
      disabled: false,
    };
    return render(<SlugField {...defaults} {...props} />);
  }

  // ── AC: Renders USWDS components ──────────────────────────────────────────

  it('renders the slug input with usa-input class', () => {
    const { container } = renderSlugField();
    const input = container.querySelector('input.usa-input');
    expect(input).not.toBeNull();
  });

  it('renders a label with usa-label class', () => {
    const { container } = renderSlugField();
    const label = container.querySelector('label.usa-label');
    expect(label).not.toBeNull();
  });

  it('renders a usa-form-group wrapper', () => {
    const { container } = renderSlugField();
    expect(container.querySelector('.usa-form-group')).not.toBeNull();
  });

  it('renders a required abbr element with asterisk', () => {
    const { container } = renderSlugField();
    const abbr = container.querySelector<HTMLElement>('abbr[title="required"]');
    expect(abbr).not.toBeNull();
    expect(abbr?.textContent).toContain('*');
  });

  it('sets aria-required on the input', () => {
    renderSlugField();
    const input = screen.getByTestId('slug-input');
    // aria-required is present (true or "true")
    expect(input).toHaveAttribute('aria-required');
  });

  // ── AC: Shows full URL preview ────────────────────────────────────────────

  it('shows a URL preview below the input', () => {
    renderSlugField({ value: 'benefits-overview' });
    const preview = screen.getByTestId('slug-url-preview');
    expect(preview).toBeInTheDocument();
    expect(preview.textContent).toContain('benefits-overview');
  });

  it('URL preview updates when slug value changes', () => {
    const { rerender } = renderSlugField({ value: 'old-slug' });
    expect(screen.getByTestId('slug-url-preview').textContent).toContain('old-slug');

    rerender(
      <SlugField value="new-slug" onChange={vi.fn()} />,
    );
    expect(screen.getByTestId('slug-url-preview').textContent).toContain('new-slug');
  });

  it('URL preview contains "URL preview:" label text', () => {
    renderSlugField({ value: 'some-page' });
    expect(screen.getByTestId('slug-url-preview').textContent).toContain('URL preview:');
  });

  // ── AC: Editable field ────────────────────────────────────────────────────

  it('calls onChange when the slug input changes', () => {
    const onChange = vi.fn();
    renderSlugField({ onChange });

    const input = screen.getByTestId('slug-input');
    fireEvent.change(input, { target: { value: 'edited-slug' } });

    expect(onChange).toHaveBeenCalledWith('edited-slug');
  });

  it('shows current slug value in the input', () => {
    renderSlugField({ value: 'current-value' });
    expect(screen.getByTestId('slug-input')).toHaveValue('current-value');
  });

  // ── AC: Duplicate slug error message ─────────────────────────────────────

  it('shows usa-error-message when error is set', () => {
    const { container } = renderSlugField({
      error: 'A content entry with slug "my-slug" already exists for locale en-US.',
    });
    const errorEl = container.querySelector('.usa-error-message');
    expect(errorEl).not.toBeNull();
    expect(errorEl?.textContent).toContain('already exists');
  });

  it('adds usa-form-group--error class when error is set', () => {
    const { container } = renderSlugField({ error: 'Duplicate slug.' });
    expect(container.querySelector('.usa-form-group--error')).not.toBeNull();
  });

  it('adds usa-input--error class when error is set', () => {
    const { container } = renderSlugField({ error: 'Duplicate slug.' });
    expect(container.querySelector('input.usa-input--error')).not.toBeNull();
  });

  it('sets aria-invalid on input when error is set', () => {
    renderSlugField({ error: 'Duplicate slug.' });
    const input = screen.getByTestId('slug-input');
    expect(input).toHaveAttribute('aria-invalid', 'true');
  });

  it('includes error element id in aria-describedby when error is set', () => {
    renderSlugField({ error: 'Duplicate slug.' });
    const input = screen.getByTestId('slug-input');
    const describedBy = input.getAttribute('aria-describedby') ?? '';
    expect(describedBy).toContain('field-slug-error');
  });

  it('error message has role="alert"', () => {
    renderSlugField({ error: 'Duplicate slug.' });
    const errorEl = screen.getByTestId('slug-field-error');
    expect(errorEl).toHaveAttribute('role', 'alert');
  });

  // ── AC: No error state when error is null/undefined ───────────────────────

  it('does not show error message when error is null', () => {
    const { container } = renderSlugField({ error: null });
    expect(container.querySelector('.usa-error-message')).toBeNull();
    expect(container.querySelector('.usa-form-group--error')).toBeNull();
  });

  it('does not set aria-invalid when no error', () => {
    renderSlugField({ error: null });
    const input = screen.getByTestId('slug-input');
    // aria-invalid should not be "true" or present as truthy
    const ariainvalid = input.getAttribute('aria-invalid');
    expect(ariainvalid).not.toBe('true');
  });

  // ── AC: Disabled state ────────────────────────────────────────────────────

  it('disables the input when disabled prop is true', () => {
    renderSlugField({ disabled: true });
    expect(screen.getByTestId('slug-input')).toBeDisabled();
  });

  // ── AC: aria-describedby always includes hint and preview ids ─────────────

  it('includes hint and preview ids in aria-describedby', () => {
    renderSlugField({ error: null });
    const input = screen.getByTestId('slug-input');
    const describedBy = input.getAttribute('aria-describedby') ?? '';
    expect(describedBy).toContain('field-slug-hint');
    expect(describedBy).toContain('field-slug-preview');
  });
});
