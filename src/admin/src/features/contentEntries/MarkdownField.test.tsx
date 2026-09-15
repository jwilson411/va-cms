/**
 * Tests for issue #65: Milkdown WYSIWYG Markdown editor with USWDS split preview.
 *
 * AC covered:
 *  - Milkdown editor renders in usa-form-group wrapper
 *  - Toolbar: Bold, Italic, H2, H3, H4, Ordered List, Unordered List, Link, Block Quote, Insert Image
 *  - H1 is absent from toolbar
 *  - Split view: editor left, live preview right
 *  - Preview pane uses usa-prose class
 *  - "Preview only" mode collapses editor to full-width rendered view
 *  - Stored value is plain Markdown string (confirmed via onChange callbacks)
 */

import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

import { MarkdownField } from './MarkdownField';
import type { FieldDefinitionDto } from './formTypes';

// ── Milkdown mock ─────────────────────────────────────────────────────────────
// Milkdown requires a real DOM ProseMirror environment (not available in jsdom).
// We mock the Milkdown components to test the surrounding USWDS wrapper and
// split-view logic without a full ProseMirror initialisation.

vi.mock('@milkdown/react', () => ({
  MilkdownProvider: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="milkdown-provider">{children}</div>
  ),
  Milkdown: () => (
    <div
      data-testid="milkdown-contenteditable"
      contentEditable="true"
      role="textbox"
      aria-multiline="true"
    />
  ),
  useEditor: () => ({ loading: false, get: () => undefined }),
}));

vi.mock('@milkdown/kit/core', () => ({
  Editor: { make: () => ({ config: () => ({ use: () => ({ use: () => ({}) }) }) }) },
  rootCtx: 'rootCtx',
  defaultValueCtx: 'defaultValueCtx',
}));

vi.mock('@milkdown/preset-commonmark', () => ({
  commonmark: [],
}));

vi.mock('@milkdown/plugin-listener', () => ({
  listener: [],
  listenerCtx: 'listenerCtx',
}));

vi.mock('@milkdown/utils', () => ({
  replaceAll: () => () => undefined,
}));

// ── fetch mock for POST /api/v1/preview/render ────────────────────────────────

const MOCK_PREVIEW_HTML = '<h2>Hello</h2><p>World</p>';

function setupFetchMock(html: string = MOCK_PREVIEW_HTML) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ html }),
  } as unknown as Response);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeDef(overrides: Partial<FieldDefinitionDto> = {}): FieldDefinitionDto {
  return {
    name: 'body',
    label: 'Body',
    type: 'RichText',
    required: false,
    maxLength: null,
    ...overrides,
  };
}

function renderField(
  overrides: Partial<FieldDefinitionDto> = {},
  value: string = '',
  onChange = vi.fn(),
  onBlur = vi.fn(),
) {
  const def = makeDef(overrides);
  return render(
    <MarkdownField
      def={def}
      value={value}
      onChange={onChange}
      onBlur={onBlur}
    />,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

beforeEach(() => {
  setupFetchMock();
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('MarkdownField — USWDS form-group wrapper', () => {
  it('renders usa-form-group wrapper', () => {
    const { container } = renderField();
    expect(container.querySelector('.usa-form-group')).not.toBeNull();
  });

  it('renders field label with given text', () => {
    renderField({ label: 'Article Body' });
    expect(screen.getByText(/Article Body/)).toBeInTheDocument();
  });

  it('renders required abbr when required', () => {
    const { container } = renderField({ required: true });
    expect(container.querySelector('abbr[title="required"]')).not.toBeNull();
  });

  it('does NOT render required abbr when not required', () => {
    const { container } = renderField({ required: false });
    expect(container.querySelector('abbr[title="required"]')).toBeNull();
  });

  it('adds usa-form-group--error class when error present', () => {
    const { container } = render(
      <MarkdownField
        def={makeDef()}
        value=""
        error="Body is required."
        onChange={vi.fn()}
        onBlur={vi.fn()}
      />,
    );
    expect(container.querySelector('.usa-form-group--error')).not.toBeNull();
  });

  it('renders usa-error-message when error provided', () => {
    const { container } = render(
      <MarkdownField
        def={makeDef()}
        value=""
        error="Body is required."
        onChange={vi.fn()}
        onBlur={vi.fn()}
      />,
    );
    const errEl = container.querySelector('.usa-error-message');
    expect(errEl).not.toBeNull();
    expect(errEl?.textContent).toContain('Body is required.');
  });
});

describe('MarkdownField — toolbar', () => {
  it('renders toolbar region', () => {
    renderField();
    expect(screen.getByRole('toolbar')).toBeInTheDocument();
    expect(screen.getByTestId('md-toolbar')).toBeInTheDocument();
  });

  it('renders Bold button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-bold')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bold' })).toBeInTheDocument();
  });

  it('renders Italic button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-italic')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Italic' })).toBeInTheDocument();
  });

  it('renders H2 button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-h2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Heading 2' })).toBeInTheDocument();
  });

  it('renders H3 button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-h3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Heading 3' })).toBeInTheDocument();
  });

  it('renders H4 button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-h4')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Heading 4' })).toBeInTheDocument();
  });

  it('renders Ordered List button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-ordered-list')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ordered list' })).toBeInTheDocument();
  });

  it('renders Unordered List button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-unordered-list')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unordered list' })).toBeInTheDocument();
  });

  it('renders Link button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-link')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Insert link' })).toBeInTheDocument();
  });

  it('renders Block Quote button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-blockquote')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Block quote' })).toBeInTheDocument();
  });

  it('renders Insert Image button', () => {
    renderField();
    expect(screen.getByTestId('md-toolbar-image')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Insert image from library' })).toBeInTheDocument();
  });

  // AC: H1 must NOT appear in the toolbar
  it('does NOT render H1 toolbar button', () => {
    renderField();
    expect(screen.queryByRole('button', { name: /Heading 1/i })).toBeNull();
    expect(screen.queryByTestId('md-toolbar-h1')).toBeNull();
  });

  it('Bold button calls onChange with bold Markdown syntax', () => {
    const onChange = vi.fn();
    renderField({}, '', onChange);
    fireEvent.click(screen.getByTestId('md-toolbar-bold'));
    expect(onChange).toHaveBeenCalledWith('**bold text**');
  });

  it('H2 button calls onChange with ## heading syntax', () => {
    const onChange = vi.fn();
    renderField({}, '', onChange);
    fireEvent.click(screen.getByTestId('md-toolbar-h2'));
    expect(onChange).toHaveBeenCalledWith('## Heading 2');
  });

  it('Block Quote button calls onChange with > syntax', () => {
    const onChange = vi.fn();
    renderField({}, '', onChange);
    fireEvent.click(screen.getByTestId('md-toolbar-blockquote'));
    expect(onChange).toHaveBeenCalledWith('> Block quote text');
  });

  it('Image button opens media library modal', () => {
    renderField();
    fireEvent.click(screen.getByTestId('md-toolbar-image'));
    expect(screen.getByTestId('media-library-modal')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: /Media Library/i })).toBeInTheDocument();
  });
});

describe('MarkdownField — split view', () => {
  it('renders split container', () => {
    renderField();
    expect(screen.getByTestId('md-split-container')).toBeInTheDocument();
  });

  it('renders Milkdown editor on the left', () => {
    renderField();
    expect(screen.getByTestId('milkdown-provider')).toBeInTheDocument();
  });

  it('renders preview pane on the right', () => {
    renderField();
    expect(screen.getByTestId('md-preview-pane')).toBeInTheDocument();
  });

  it('preview pane contains usa-prose div', () => {
    const { container } = renderField();
    const previewPane = container.querySelector('[data-testid="md-preview-pane"]');
    expect(previewPane?.querySelector('.usa-prose')).not.toBeNull();
  });

  it('calls POST /api/v1/preview/render debounced 500ms after toolbar action', async () => {
    const onChange = vi.fn();
    renderField({}, '', onChange);

    // Trigger a toolbar action that calls onChange
    fireEvent.click(screen.getByTestId('md-toolbar-bold'));

    // Debounce has not fired yet
    expect((globalThis as any).fetch).not.toHaveBeenCalled();

    // Advance timers past the 500ms debounce
    await act(async () => {
      vi.advanceTimersByTime(600);
    });

    expect((globalThis as any).fetch).toHaveBeenCalledWith(
      '/api/v1/preview/render',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('preview HTML is injected into usa-prose div after render', async () => {
    renderField();
    // Simulate toolbar bold so there is content to render
    fireEvent.click(screen.getByTestId('md-toolbar-bold'));

    // Flush the debounce timer and any pending promises
    await act(async () => {
      vi.runAllTimers();
      // Allow fetch promise to resolve
      await Promise.resolve();
      await Promise.resolve();
    });

    const previewHtml = screen.getByTestId('md-preview-html');
    // After fake timers flush + mock fetch resolves, HTML should be injected
    expect(previewHtml).toBeInTheDocument();
    // The mock fetch returns '<h2>Hello</h2><p>World</p>'
    expect(previewHtml.innerHTML).toContain('Hello');
  });
});

describe('MarkdownField — preview-only mode', () => {
  it('clicking the Preview button switches to preview-only mode', () => {
    renderField();
    // Initial state is split
    expect(screen.queryByTestId('markdown-field-preview-only')).toBeNull();

    fireEvent.click(screen.getByTestId('md-toolbar-preview-only'));

    expect(screen.getByTestId('markdown-field-preview-only')).toBeInTheDocument();
    // Editor is no longer visible
    expect(screen.queryByTestId('md-split-container')).toBeNull();
  });

  it('preview-only mode shows full-width usa-prose preview pane', () => {
    renderField();
    fireEvent.click(screen.getByTestId('md-toolbar-preview-only'));
    const previewPane = screen.getByTestId('md-preview-pane');
    expect(previewPane.classList.contains('usa-prose') ||
      previewPane.classList.contains('va-cms-md-preview--full')).toBe(true);
  });

  it('clicking ← Edit in preview-only mode returns to split view', () => {
    renderField();
    fireEvent.click(screen.getByTestId('md-toolbar-preview-only'));
    expect(screen.getByTestId('markdown-field-preview-only')).toBeInTheDocument();

    // The button text changes to "← Edit"; click it to go back
    fireEvent.click(screen.getByTestId('md-toolbar-preview-only'));
    expect(screen.queryByTestId('markdown-field-preview-only')).toBeNull();
    expect(screen.getByTestId('md-split-container')).toBeInTheDocument();
  });

  it('preview-only toolbar button has aria-pressed=true when active', () => {
    renderField();
    fireEvent.click(screen.getByTestId('md-toolbar-preview-only'));
    const btn = screen.getByTestId('md-toolbar-preview-only');
    expect(btn.getAttribute('aria-pressed')).toBe('true');
  });
});

describe('MarkdownField — Markdown storage (no raw HTML)', () => {
  it('onChange is called with Markdown string from toolbar actions', () => {
    const onChange = vi.fn();
    renderField({}, '', onChange);
    fireEvent.click(screen.getByTestId('md-toolbar-italic'));
    expect(onChange).toHaveBeenCalledWith('*italic text*');
  });

  it('onChange result does not contain raw HTML tags', () => {
    const onChange = vi.fn();
    renderField({}, '', onChange);
    fireEvent.click(screen.getByTestId('md-toolbar-bold'));
    const calledWith: string = onChange.mock.calls[0][0] as string;
    // Should be Markdown, not HTML
    expect(calledWith).not.toMatch(/<strong>|<b>/);
    expect(calledWith).toMatch(/\*\*/);
  });
});
