/**
 * RichTextEditor — TipTap WYSIWYG editor with USWDS-safe toolbar (issue #31).
 *
 * Acceptance criteria:
 *  - Toolbar: Bold, Italic, H2-H4 only, Ordered List, Unordered List, Link, Block Quote
 *  - H1 is disabled in toolbar (page title is the H1)
 *  - Inline color picker and font size controls are absent
 *  - Output HTML passes axe-core with zero critical violations
 *  - Media insertion opens media library modal
 */

import React, { useState, useCallback } from 'react';
import { useEditor, EditorContent } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Link from '@tiptap/extension-link';
import Heading from '@tiptap/extension-heading';
import Blockquote from '@tiptap/extension-blockquote';

import { MediaLibraryModal } from './MediaLibraryModal';

export interface RichTextEditorProps {
  /**
   * HTML string value. Editor is initialized with this content.
   */
  value: string;
  /**
   * Called with the current HTML whenever the editor content changes.
   */
  onChange: (html: string) => void;
  /**
   * Called when the editor loses focus.
   */
  onBlur?: () => void;
  /**
   * Id used for the editor wrapper div (data-testid / aria hooks).
   * The contenteditable ProseMirror node is labelled via aria-labelledby
   * pointing to labelId.  A <div> is non-labellable (HTML spec), so the
   * surrounding <label> must NOT use htmlFor targeting the editor.
   * Instead the label element should carry its own id (labelId) and the
   * editor contenteditable uses aria-labelledby={labelId}.
   */
  editorId: string;
  /**
   * Id of the <label> element. The contenteditable uses aria-labelledby
   * pointing here so screen readers announce the field name correctly.
   * If omitted, defaults to `${editorId}-label`.
   */
  labelId?: string;
  /**
   * aria-describedby value forwarded from the parent form group.
   */
  ariaDescribedby?: string;
  /**
   * aria-invalid forwarded from parent.
   */
  ariaInvalid?: boolean;
  /**
   * aria-required forwarded from parent.
   */
  ariaRequired?: boolean;
}

/**
 * Returns true when the Link mark is currently active.
 */
function useIsLinkActive(editor: ReturnType<typeof useEditor>): boolean {
  if (!editor) return false;
  return editor.isActive('link');
}

export function RichTextEditor({
  value,
  onChange,
  onBlur,
  editorId,
  labelId,
  ariaDescribedby,
  ariaInvalid,
  ariaRequired,
}: RichTextEditorProps): JSX.Element {
  const [isMediaModalOpen, setIsMediaModalOpen] = useState(false);
  const resolvedLabelId = labelId ?? `${editorId}-label`;

  const editor = useEditor({
    extensions: [
      // StarterKit includes Bold, Italic, BulletList, OrderedList, Paragraph, etc.
      // Disable heading (replaced below with H2-H4 only), blockquote (replaced below),
      // link (added explicitly below), and formatting not in USWDS-safe toolbar.
      StarterKit.configure({
        heading: false,       // replaced by explicit Heading extension (H2-H4 only)
        blockquote: false,    // replaced by explicit Blockquote extension
        link: false,          // not part of StarterKit, but guard against future inclusion
        code: false,          // not in USWDS-safe toolbar
        codeBlock: false,     // not in USWDS-safe toolbar
        horizontalRule: false,
        strike: false,
        // BRD requires: Bold, Italic, BulletList, OrderedList — included from StarterKit
      }),
      // Allow H2, H3, H4 only — H1 is intentionally excluded (page title is H1)
      Heading.configure({ levels: [2, 3, 4] }),
      Blockquote,
      Link.configure({
        openOnClick: false,
        HTMLAttributes: {
          rel: 'noopener noreferrer',
        },
      }),
    ],
    content: value || '',
    onUpdate({ editor: ed }) {
      onChange(ed.getHTML());
    },
    onBlur() {
      onBlur?.();
    },
    editorProps: {
      attributes: {
        // Use aria-labelledby pointing to the <label> element's id.
        // A contenteditable <div> is non-labellable (HTML spec), so we cannot use
        // <label htmlFor> here. aria-labelledby is the correct ARIA pattern.
        'aria-labelledby': resolvedLabelId,
        'aria-multiline': 'true',
        ...(ariaDescribedby ? { 'aria-describedby': ariaDescribedby } : {}),
        ...(ariaInvalid ? { 'aria-invalid': 'true' } : {}),
        ...(ariaRequired ? { 'aria-required': 'true' } : {}),
        class: 'usa-prose va-cms-rich-editor__content',
      },
    },
  });

  const isLinkActive = useIsLinkActive(editor);

  const handleInsertLink = useCallback(() => {
    if (!editor) return;
    if (isLinkActive) {
      editor.chain().focus().unsetLink().run();
      return;
    }
    const url = window.prompt('Enter URL');
    if (url) {
      editor.chain().focus().setLink({ href: url }).run();
    }
  }, [editor, isLinkActive]);

  const handleMediaSelect = useCallback(
    (url: string, altText: string) => {
      if (!editor) return;
      // Insert image as HTML — editor will render it via ProseMirror
      editor
        .chain()
        .focus()
        .insertContent(`<img src="${url}" alt="${altText}" class="usa-media-link__img" />`)
        .run();
      setIsMediaModalOpen(false);
    },
    [editor],
  );

  if (!editor) return <></>;

  return (
    <div className="va-cms-rich-editor" data-testid="rich-text-editor">
      {/* ── Toolbar ─────────────────────────────────────────────────────────── */}
      <div
        className="va-cms-rich-editor__toolbar"
        role="toolbar"
        aria-label="Rich text formatting options"
        aria-controls={editorId}
        data-testid="rich-text-toolbar"
      >
        {/* Bold */}
        <button
          type="button"
          className={`usa-button usa-button--unstyled va-cms-toolbar__btn${editor.isActive('bold') ? ' is-active' : ''}`}
          onClick={() => editor.chain().focus().toggleBold().run()}
          aria-label="Bold"
          aria-pressed={editor.isActive('bold')}
          data-testid="toolbar-bold"
        >
          <strong>B</strong>
        </button>

        {/* Italic */}
        <button
          type="button"
          className={`usa-button usa-button--unstyled va-cms-toolbar__btn${editor.isActive('italic') ? ' is-active' : ''}`}
          onClick={() => editor.chain().focus().toggleItalic().run()}
          aria-label="Italic"
          aria-pressed={editor.isActive('italic')}
          data-testid="toolbar-italic"
        >
          <em>I</em>
        </button>

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        {/* H2 — H4: H1 intentionally omitted (page title is H1) */}
        {([2, 3, 4] as (2 | 3 | 4)[]).map((level) => (
          <button
            key={level}
            type="button"
            className={`usa-button usa-button--unstyled va-cms-toolbar__btn${editor.isActive('heading', { level }) ? ' is-active' : ''}`}
            onClick={() => editor.chain().focus().toggleHeading({ level }).run()}
            aria-label={`Heading ${level}`}
            aria-pressed={editor.isActive('heading', { level })}
            data-testid={`toolbar-h${level}`}
          >
            H{level}
          </button>
        ))}

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        {/* Ordered list */}
        <button
          type="button"
          className={`usa-button usa-button--unstyled va-cms-toolbar__btn${editor.isActive('orderedList') ? ' is-active' : ''}`}
          onClick={() => editor.chain().focus().toggleOrderedList().run()}
          aria-label="Ordered list"
          aria-pressed={editor.isActive('orderedList')}
          data-testid="toolbar-ordered-list"
        >
          OL
        </button>

        {/* Unordered list */}
        <button
          type="button"
          className={`usa-button usa-button--unstyled va-cms-toolbar__btn${editor.isActive('bulletList') ? ' is-active' : ''}`}
          onClick={() => editor.chain().focus().toggleBulletList().run()}
          aria-label="Unordered list"
          aria-pressed={editor.isActive('bulletList')}
          data-testid="toolbar-bullet-list"
        >
          UL
        </button>

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        {/* Link */}
        <button
          type="button"
          className={`usa-button usa-button--unstyled va-cms-toolbar__btn${isLinkActive ? ' is-active' : ''}`}
          onClick={handleInsertLink}
          aria-label={isLinkActive ? 'Remove link' : 'Insert link'}
          aria-pressed={isLinkActive}
          data-testid="toolbar-link"
        >
          Link
        </button>

        {/* Block quote */}
        <button
          type="button"
          className={`usa-button usa-button--unstyled va-cms-toolbar__btn${editor.isActive('blockquote') ? ' is-active' : ''}`}
          onClick={() => editor.chain().focus().toggleBlockquote().run()}
          aria-label="Block quote"
          aria-pressed={editor.isActive('blockquote')}
          data-testid="toolbar-blockquote"
        >
          &ldquo;&rdquo;
        </button>

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        {/* Media insertion */}
        <button
          type="button"
          className="usa-button usa-button--unstyled va-cms-toolbar__btn"
          onClick={() => setIsMediaModalOpen(true)}
          aria-label="Insert media from library"
          aria-haspopup="dialog"
          data-testid="toolbar-media"
        >
          Media
        </button>
      </div>

      {/* ── Editor content area ──────────────────────────────────────────────── */}
      <EditorContent
        editor={editor}
        className="va-cms-rich-editor__body"
      />

      {/* ── Media library modal ──────────────────────────────────────────────── */}
      {isMediaModalOpen && (
        <MediaLibraryModal
          onSelect={handleMediaSelect}
          onClose={() => setIsMediaModalOpen(false)}
        />
      )}
    </div>
  );
}
