/**
 * Tests for issue #115: TipTap WYSIWYG RichText field with tiptap-markdown export.
 *
 * AC covered:
 *  - Toolbar: Bold, Italic, H2-H4 (no H1), ordered/unordered list, link, image insert
 *  - No split view or preview toggle — the editor is the live preview
 *  - Value is Markdown in and Markdown out; no raw HTML is ever emitted
 *  - Image insert opens the existing MediaLibraryModal and inserts `![alt](url)`
 *  - Section 508: aria-labelledby on the contenteditable, aria-pressed on toggles,
 *    toolbar is a single tab stop with arrow-key navigation
 */

import React from 'react';
import { render, screen, fireEvent, within, act } from '@testing-library/react';
import { describe, it, expect, vi, afterEach } from 'vitest';

import { RichTextEditor } from './RichTextEditor';
import { RichTextField, FieldRenderer } from './FieldRenderers';
import type { FieldDefinitionDto } from './formTypes';

// ── Helpers ───────────────────────────────────────────────────────────────────

type EditorProps = React.ComponentProps<typeof RichTextEditor>;

function renderEditor(overrides: Partial<EditorProps> = {}) {
  const props: EditorProps = {
    editorId: 'test-body',
    labelId: 'test-body-label',
    value: '',
    onChange: vi.fn(),
    onBlur: vi.fn(),
    ...overrides,
  };
  const utils = render(<RichTextEditor {...props} />);
  const rerenderWith = (next: Partial<EditorProps>) =>
    utils.rerender(<RichTextEditor {...props} {...next} />);
  return { ...utils, props, rerenderWith };
}

function contentEditable(container: HTMLElement): HTMLElement {
  const el = container.querySelector<HTMLElement>('[contenteditable="true"]');
  if (!el) throw new Error('contenteditable not rendered');
  return el;
}

function lastMarkdown(onChange: ReturnType<typeof vi.fn>): string {
  const calls = onChange.mock.calls;
  return calls.length ? (calls[calls.length - 1][0] as string) : '';
}

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

afterEach(() => {
  vi.restoreAllMocks();
});

// ── AC: Word-style toolbar ────────────────────────────────────────────────────

describe('RichTextEditor toolbar', () => {
  it('renders a toolbar with Bold, Italic, H2-H4, lists, link, block quote and image', () => {
    renderEditor();
    const toolbar = screen.getByRole('toolbar', { name: 'Text formatting' });
    expect(toolbar).toBe(screen.getByTestId('rich-text-toolbar'));
    for (const name of [
      'Bold',
      'Italic',
      'Heading 2',
      'Heading 3',
      'Heading 4',
      'Ordered list',
      'Unordered list',
      'Insert link',
      'Block quote',
      'Insert image from library',
    ]) {
      expect(within(toolbar).getByRole('button', { name })).toBeInTheDocument();
    }
  });

  it('does NOT render an H1 button (page title is the H1)', () => {
    renderEditor();
    expect(screen.queryByRole('button', { name: /Heading 1/i })).toBeNull();
    expect(screen.queryByTestId('toolbar-h1')).toBeNull();
  });

  it('does NOT render a split view, preview pane or preview toggle', () => {
    renderEditor({ value: '## Hello' });
    expect(screen.queryByRole('button', { name: /preview/i })).toBeNull();
    expect(screen.queryByTestId('md-split-container')).toBeNull();
    expect(screen.queryByTestId('md-preview-pane')).toBeNull();
    expect(screen.queryByText(/Raw Markdown/i)).toBeNull();
  });

  it('does NOT render color or font-size controls', () => {
    const { container } = renderEditor();
    expect(container.querySelector('input[type="color"]')).toBeNull();
    expect(screen.queryByRole('button', { name: /color|font size/i })).toBeNull();
    expect(screen.queryByRole('combobox')).toBeNull();
  });

  it('toggle buttons expose aria-pressed; the image button exposes aria-haspopup instead', () => {
    renderEditor();
    const toolbar = screen.getByTestId('rich-text-toolbar');
    for (const btn of within(toolbar).getAllByRole('button')) {
      if (btn.getAttribute('aria-haspopup')) {
        expect(btn).toHaveAttribute('aria-haspopup', 'dialog');
        expect(btn).not.toHaveAttribute('aria-pressed');
      } else {
        expect(btn).toHaveAttribute('aria-pressed');
      }
    }
  });

  it('reflects the active heading level in aria-pressed', () => {
    renderEditor({ value: '## Title' });
    // Caret starts at the beginning of the document, inside the H2.
    expect(screen.getByTestId('toolbar-h2')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('toolbar-h3')).toHaveAttribute('aria-pressed', 'false');
  });
});

// ── AC: Markdown in / Markdown out ────────────────────────────────────────────

describe('RichTextEditor Markdown round-trip', () => {
  it('renders an initial Markdown value as WYSIWYG HTML', () => {
    const { container } = renderEditor({
      value: '## Hello\n\nSome **bold** and *italic* text.\n\n- one\n- two\n\n> quoted',
    });
    const ce = contentEditable(container);
    expect(ce.querySelector('h2')?.textContent).toBe('Hello');
    expect(ce.querySelector('strong')?.textContent).toBe('bold');
    expect(ce.querySelector('em')?.textContent).toBe('italic');
    expect(ce.querySelectorAll('ul li')).toHaveLength(2);
    expect(ce.querySelector('blockquote')?.textContent).toBe('quoted');
    // No literal Markdown syntax in the WYSIWYG view.
    expect(ce.textContent).not.toContain('##');
    expect(ce.textContent).not.toContain('**');
  });

  it('emits Markdown (not HTML) from onChange when a toolbar action changes content', () => {
    const onChange = vi.fn();
    renderEditor({ value: 'Plain paragraph', onChange });

    fireEvent.click(screen.getByTestId('toolbar-h2'));

    const md = lastMarkdown(onChange);
    expect(md).toBe('## Plain paragraph');
    expect(md).not.toMatch(/<[a-z]/i);
  });

  it('toggles a bullet list and serialises with the "-" marker', () => {
    const onChange = vi.fn();
    renderEditor({ value: 'item', onChange });
    fireEvent.click(screen.getByTestId('toolbar-bullet-list'));
    expect(lastMarkdown(onChange)).toBe('- item');
  });

  it('toggles an ordered list', () => {
    const onChange = vi.fn();
    renderEditor({ value: 'item', onChange });
    fireEvent.click(screen.getByTestId('toolbar-ordered-list'));
    expect(lastMarkdown(onChange)).toBe('1. item');
  });

  it('toggles a block quote', () => {
    const onChange = vi.fn();
    renderEditor({ value: 'wise words', onChange });
    fireEvent.click(screen.getByTestId('toolbar-blockquote'));
    expect(lastMarkdown(onChange)).toBe('> wise words');
  });

  it('treats raw HTML in the Markdown as literal text (html: false, matches Markdig DisableHtml)', () => {
    const onChange = vi.fn();
    const { container } = renderEditor({
      value: '<script>alert(1)</script> <u>plain</u> text',
      onChange,
    });
    const ce = contentEditable(container);
    expect(ce.querySelector('script')).toBeNull();
    expect(ce.querySelector('u')).toBeNull();
    expect(ce.textContent).toContain('<script>alert(1)</script>');

    fireEvent.click(screen.getByTestId('toolbar-h3'));
    const md = lastMarkdown(onChange);
    expect(md.startsWith('### ')).toBe(true);
    // Serialiser escapes it so it stays text on the public site too.
    expect(md).not.toMatch(/<script>/);
  });

  it('syncs an external value change into the editor without echoing it back', async () => {
    const onChange = vi.fn();
    const { container, rerenderWith } = renderEditor({ value: '', onChange });
    expect(contentEditable(container).textContent).toBe('');

    // Edit mode: the saved body arrives after mount.
    await act(async () => {
      rerenderWith({ value: '### Loaded later\n\nBody copy.' });
    });

    const ce = contentEditable(container);
    expect(ce.querySelector('h3')?.textContent).toBe('Loaded later');
    expect(ce.querySelector('p')?.textContent).toBe('Body copy.');
    // Sync must not fire onChange — that would mark a pristine form dirty.
    expect(onChange).not.toHaveBeenCalled();
  });

  it('ignores the parent echoing back the Markdown the editor just emitted', async () => {
    const onChange = vi.fn();
    const { container, rerenderWith } = renderEditor({ value: 'hello', onChange });
    fireEvent.click(screen.getByTestId('toolbar-h2'));
    const emitted = lastMarkdown(onChange);
    const before = contentEditable(container).innerHTML;

    await act(async () => {
      rerenderWith({ value: emitted });
    });

    // No re-parse: DOM identical, no additional onChange.
    expect(contentEditable(container).innerHTML).toBe(before);
    expect(onChange).toHaveBeenCalledTimes(1);
  });
});

// ── AC: Image insert via existing media library picker ────────────────────────

describe('RichTextEditor image insert', () => {
  it('opens the media library modal from the Image button', () => {
    renderEditor();
    fireEvent.click(screen.getByTestId('toolbar-image'));
    expect(screen.getByRole('dialog', { name: /Media Library/i })).toBeInTheDocument();
  });

  it('inserts the picked asset as a Markdown image on its own block and closes the modal', () => {
    const onChange = vi.fn();
    const { container } = renderEditor({ value: '## Heading\n\nIntro', onChange });

    fireEvent.click(screen.getByTestId('toolbar-image'));
    fireEvent.click(screen.getByTestId('media-asset-1'));

    expect(screen.queryByTestId('media-library-modal')).toBeNull();

    const img = contentEditable(container).querySelector('img');
    expect(img).toHaveAttribute('src', '/media/placeholder-hero.jpg');
    expect(img).toHaveAttribute('alt', 'Placeholder hero image');

    const md = lastMarkdown(onChange);
    expect(md).toContain('![Placeholder hero image](/media/placeholder-hero.jpg)');
    // Block image must be separated from neighbouring blocks so Markdig
    // doesn't fold the heading into the image paragraph.
    expect(md).not.toMatch(/\)## /);
    expect(md).toMatch(/!\[Placeholder hero image\]\(\/media\/placeholder-hero\.jpg\)\n\n## Heading/);
    expect(md).not.toContain('<img');
  });

  it('closes the modal on Escape without inserting', () => {
    const onChange = vi.fn();
    renderEditor({ onChange });
    fireEvent.click(screen.getByTestId('toolbar-image'));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByTestId('media-library-modal')).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });
});

// ── AC: Link insert ───────────────────────────────────────────────────────────

describe('RichTextEditor links', () => {
  it('inserts the URL as link text when nothing is selected', () => {
    vi.spyOn(window, 'prompt').mockReturnValue('https://www.va.gov/');
    const onChange = vi.fn();
    renderEditor({ value: '', onChange });

    fireEvent.click(screen.getByTestId('toolbar-link'));

    expect(window.prompt).toHaveBeenCalledWith('Enter URL', '');
    // Text === href serialises as a CommonMark autolink, which Markdig renders as <a>.
    expect(lastMarkdown(onChange)).toBe('<https://www.va.gov/>');
  });

  it('wraps the selected text in a Markdown link', () => {
    vi.spyOn(window, 'prompt').mockReturnValue('https://www.va.gov/');
    const onChange = vi.fn();
    const { container } = renderEditor({ value: 'Visit VA today', onChange });

    // Select "VA" in the DOM; ProseMirror reads the DOM selection while focused.
    const ce = contentEditable(container);
    ce.focus();
    const textNode = ce.querySelector('p')!.firstChild as Text;
    const range = document.createRange();
    range.setStart(textNode, 6);
    range.setEnd(textNode, 8);
    const sel = window.getSelection()!;
    sel.removeAllRanges();
    sel.addRange(range);
    document.dispatchEvent(new Event('selectionchange'));

    fireEvent.click(screen.getByTestId('toolbar-link'));

    expect(lastMarkdown(onChange)).toBe('Visit [VA](https://www.va.gov/) today');
  });

  it('does nothing when the prompt is cancelled', () => {
    vi.spyOn(window, 'prompt').mockReturnValue(null);
    const onChange = vi.fn();
    renderEditor({ value: 'VA', onChange });
    fireEvent.click(screen.getByTestId('toolbar-link'));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('removes the link when the prompt is submitted empty', () => {
    vi.spyOn(window, 'prompt').mockReturnValue('');
    const onChange = vi.fn();
    renderEditor({ value: '[VA](https://www.va.gov/)', onChange });
    expect(screen.getByTestId('toolbar-link')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Edit link' })).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('toolbar-link'));

    expect(window.prompt).toHaveBeenCalledWith('Enter URL', 'https://www.va.gov/');
    expect(lastMarkdown(onChange)).toBe('VA');
  });
});

// ── AC: Section 508 / keyboard navigation ─────────────────────────────────────

describe('RichTextEditor accessibility', () => {
  it('contenteditable carries id, aria-labelledby, aria-multiline and forwarded ARIA', () => {
    const { container } = renderEditor({
      editorId: 'body-field',
      labelId: 'body-label',
      ariaDescribedby: 'body-hint body-error',
      ariaInvalid: true,
      ariaRequired: true,
    });
    const ce = contentEditable(container);
    expect(ce).toHaveAttribute('id', 'body-field');
    expect(ce).toHaveAttribute('aria-labelledby', 'body-label');
    expect(ce).toHaveAttribute('aria-multiline', 'true');
    expect(ce).toHaveAttribute('aria-describedby', 'body-hint body-error');
    expect(ce).toHaveAttribute('aria-invalid', 'true');
    expect(ce).toHaveAttribute('aria-required', 'true');
    expect(screen.getByRole('toolbar')).toHaveAttribute('aria-controls', 'body-field');
  });

  it('toolbar is a single tab stop (roving tabindex)', () => {
    renderEditor();
    const buttons = within(screen.getByTestId('rich-text-toolbar')).getAllByRole('button');
    const tabbable = buttons.filter((b) => b.tabIndex === 0);
    expect(tabbable).toHaveLength(1);
    expect(tabbable[0]).toBe(screen.getByTestId('toolbar-bold'));
    for (const b of buttons.slice(1)) expect(b.tabIndex).toBe(-1);
  });

  it('ArrowRight / ArrowLeft / Home / End move focus between toolbar buttons', () => {
    renderEditor();
    const toolbar = screen.getByTestId('rich-text-toolbar');
    const buttons = within(toolbar).getAllByRole('button');
    const [bold, italic] = buttons;
    const last = buttons[buttons.length - 1];

    bold.focus();
    fireEvent.keyDown(toolbar, { key: 'ArrowRight' });
    expect(document.activeElement).toBe(italic);
    expect(italic.tabIndex).toBe(0);
    expect(bold.tabIndex).toBe(-1);

    fireEvent.keyDown(toolbar, { key: 'ArrowLeft' });
    expect(document.activeElement).toBe(bold);

    // Wraps from first to last.
    fireEvent.keyDown(toolbar, { key: 'ArrowLeft' });
    expect(document.activeElement).toBe(last);

    fireEvent.keyDown(toolbar, { key: 'Home' });
    expect(document.activeElement).toBe(bold);

    fireEvent.keyDown(toolbar, { key: 'End' });
    expect(document.activeElement).toBe(last);
  });

  it('every toolbar button has an accessible name', () => {
    renderEditor();
    for (const b of within(screen.getByTestId('rich-text-toolbar')).getAllByRole('button')) {
      expect(b.getAttribute('aria-label')).toBeTruthy();
    }
  });

  it('calls onBlur when the editor loses focus', () => {
    const onBlur = vi.fn();
    const { container } = renderEditor({ onBlur });
    fireEvent.blur(contentEditable(container));
    expect(onBlur).toHaveBeenCalledTimes(1);
  });
});

// ── RichTextField USWDS wrapper ───────────────────────────────────────────────

describe('RichTextField (FieldRenderers)', () => {
  function renderField(
    overrides: Partial<FieldDefinitionDto> = {},
    extra: Partial<React.ComponentProps<typeof RichTextField>> = {},
  ) {
    return render(
      <RichTextField
        def={makeDef(overrides)}
        value=""
        onChange={vi.fn()}
        onBlur={vi.fn()}
        {...extra}
      />,
    );
  }

  it('renders the TipTap editor inside a usa-form-group', () => {
    const { container } = renderField();
    const group = container.querySelector('.usa-form-group');
    expect(group).not.toBeNull();
    expect(group).toHaveAttribute('data-testid', 'rich-text-field');
    expect(within(group as HTMLElement).getByTestId('rich-text-editor')).toBeInTheDocument();
    expect(container.querySelector('[data-testid="markdown-field"]')).toBeNull();
  });

  it('labels the contenteditable via aria-labelledby → #field-{name}-label', () => {
    const { container } = renderField({ label: 'Article Body' });
    const label = container.querySelector('#field-body-label');
    expect(label).not.toBeNull();
    expect(label?.textContent).toContain('Article Body');
    expect(contentEditable(container)).toHaveAttribute('aria-labelledby', 'field-body-label');
    expect(contentEditable(container)).toHaveAttribute('id', 'field-body');
  });

  it('marks required fields with the USWDS required abbr and aria-required', () => {
    const { container } = renderField({ required: true });
    expect(container.querySelector('abbr[title="required"]')?.textContent).toContain('*');
    expect(contentEditable(container)).toHaveAttribute('aria-required', 'true');
  });

  it('wires hint and error into aria-describedby and shows usa-error-message', () => {
    const { container } = renderField(
      { hint: 'Use headings to structure the page.' },
      { error: 'Body is required.' },
    );
    expect(container.querySelector('.usa-form-group--error')).not.toBeNull();
    expect(container.querySelector('.usa-error-message')?.textContent).toBe('Body is required.');
    const ce = contentEditable(container);
    expect(ce).toHaveAttribute('aria-describedby', 'field-body-hint field-body-error');
    expect(ce).toHaveAttribute('aria-invalid', 'true');
  });

  it('passes Markdown through onChange and reports blur with the field name', () => {
    const onChange = vi.fn();
    const onBlur = vi.fn();
    const { container } = render(
      <RichTextField def={makeDef()} value="text" onChange={onChange} onBlur={onBlur} />,
    );
    fireEvent.click(screen.getByTestId('toolbar-bold'));
    fireEvent.click(screen.getByTestId('toolbar-h2'));
    expect(onChange).toHaveBeenLastCalledWith('## text');
    fireEvent.blur(contentEditable(container));
    expect(onBlur).toHaveBeenCalledWith('body');
  });

  it('FieldRenderer routes RichText to the TipTap editor', () => {
    render(
      <FieldRenderer def={makeDef()} value="" onChange={vi.fn()} onBlur={vi.fn()} />,
    );
    expect(screen.getByTestId('rich-text-editor')).toBeInTheDocument();
  });
});
