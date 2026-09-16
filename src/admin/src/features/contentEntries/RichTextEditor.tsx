/**
 * RichTextEditor — TipTap WYSIWYG editor that stores Markdown (issue #115).
 *
 * Replaces the Milkdown split-view editor from #65. The editor IS the live
 * preview: no preview pane, no preview toggle, no raw-Markdown mode.
 *
 * Acceptance criteria:
 *  - Word-style USWDS toolbar: Bold, Italic, H2-H4, ordered/unordered list,
 *    link, block quote, image insert (H1 is absent — the page title is the H1)
 *  - Value in/out is a Markdown string, serialised with tiptap-markdown.
 *    Markdig on the public site (#66) runs with DisableHtml(), so the
 *    serialiser is configured with `html: false` — nothing the editor emits
 *    falls back to raw HTML.
 *  - Image insert opens the existing MediaLibraryModal picker
 *  - Section 508: the contenteditable is labelled via aria-labelledby, the
 *    toolbar is a single tab stop with arrow-key navigation (WAI-ARIA APG
 *    toolbar pattern), and toggle buttons expose aria-pressed.
 */

import React, { useState, useCallback, useEffect, useRef } from 'react';
import { useEditor, EditorContent, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Link from '@tiptap/extension-link';
import Heading from '@tiptap/extension-heading';
import Blockquote from '@tiptap/extension-blockquote';
import Image from '@tiptap/extension-image';
import { Markdown, type MarkdownNodeSpec, type MarkdownStorage } from 'tiptap-markdown';

import { MediaLibraryModal } from './MediaLibraryModal';

// tiptap-markdown registers itself under editor.storage.markdown but doesn't
// augment TipTap's Storage interface; do it here so getMarkdown() is typed.
declare module '@tiptap/core' {
  interface Storage {
    markdown: MarkdownStorage;
  }
}

export interface RichTextEditorProps {
  /**
   * Markdown string. The editor is initialised with this content and re-synced
   * when it changes from outside the editor (async entry load, version restore).
   */
  value: string;
  /**
   * Called with the current Markdown whenever the editor content changes.
   */
  onChange: (markdown: string) => void;
  /**
   * Called when the editor loses focus.
   */
  onBlur?: () => void;
  /**
   * Id applied to the contenteditable ProseMirror node.
   */
  editorId: string;
  /**
   * Id of the <label>-like element. A contenteditable <div> is non-labellable
   * (HTML spec), so the label must NOT use htmlFor; the contenteditable points
   * back at it with aria-labelledby instead. Defaults to `${editorId}-label`.
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
 * Block-level image. tiptap-markdown ships prosemirror-markdown's *inline* image
 * serializer, which writes `![alt](src)` with no block separator; with a block
 * image that runs straight into the next block (`![a](b)## Heading`), which
 * Markdig then reads as one paragraph. Serialise it as its own block instead.
 */
const escapeMarkdown = (text: string): string => text.replace(/[\\[\]()]/g, (c) => `\\${c}`);

const BlockImage = Image.extend({
  addStorage() {
    return {
      ...this.parent?.(),
      markdown: {
        serialize(state, node) {
          const alt = escapeMarkdown(String(node.attrs.alt ?? ''));
          const src = String(node.attrs.src ?? '').replace(/[()\s]/g, (c) => encodeURIComponent(c));
          const title = node.attrs.title
            ? ` "${String(node.attrs.title).replace(/"/g, '\\"')}"`
            : '';
          state.write(`![${alt}](${src}${title})`);
          state.closeBlock(node);
        },
        parse: {}, // markdown-it handles image parsing
      } satisfies MarkdownNodeSpec,
    };
  },
});

/** Reads the current document back out as Markdown via tiptap-markdown. */
export function getMarkdown(editor: Editor): string {
  return editor.storage.markdown.getMarkdown();
}

/**
 * Word-style toolbar button. Toggle buttons expose aria-pressed; buttons that
 * open a dialog expose aria-haspopup instead. tabIndex is managed by the
 * toolbar's roving-tabindex handler.
 */
interface ToolbarButtonProps {
  label: string;
  testId: string;
  onClick: () => void;
  active?: boolean;
  hasPopup?: boolean;
  children: React.ReactNode;
}

function ToolbarButton({ label, testId, onClick, active, hasPopup, children }: ToolbarButtonProps): JSX.Element {
  return (
    <button
      type="button"
      className={`usa-button usa-button--unstyled va-cms-toolbar__btn${active ? ' is-active' : ''}`}
      onClick={onClick}
      aria-label={label}
      title={label}
      {...(hasPopup ? { 'aria-haspopup': 'dialog' as const } : { 'aria-pressed': !!active })}
      data-testid={testId}
    >
      {children}
    </button>
  );
}

const TOOLBAR_BUTTON_SELECTOR = 'button:not([disabled])';

/**
 * WAI-ARIA toolbar keyboard pattern: one tab stop, Left/Right (and Home/End)
 * move focus between buttons. Roving tabindex keeps only the current button
 * in the tab sequence.
 */
function useToolbarKeyboardNav(toolbarRef: React.RefObject<HTMLDivElement>) {
  const setRoving = useCallback((focused: HTMLElement) => {
    const toolbar = toolbarRef.current;
    if (!toolbar) return;
    toolbar.querySelectorAll<HTMLElement>(TOOLBAR_BUTTON_SELECTOR).forEach((btn) => {
      btn.tabIndex = btn === focused ? 0 : -1;
    });
  }, [toolbarRef]);

  // Initial state: first button is the tab stop.
  useEffect(() => {
    const toolbar = toolbarRef.current;
    if (!toolbar) return;
    const buttons = toolbar.querySelectorAll<HTMLElement>(TOOLBAR_BUTTON_SELECTOR);
    if (buttons.length === 0) return;
    const current = Array.from(buttons).find((b) => b.tabIndex === 0) ?? buttons[0];
    setRoving(current);
  });

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      const toolbar = toolbarRef.current;
      if (!toolbar) return;
      const buttons = Array.from(toolbar.querySelectorAll<HTMLElement>(TOOLBAR_BUTTON_SELECTOR));
      const index = buttons.indexOf(document.activeElement as HTMLElement);
      if (index === -1) return;

      let next: number | null = null;
      switch (e.key) {
        case 'ArrowRight':
          next = (index + 1) % buttons.length;
          break;
        case 'ArrowLeft':
          next = (index - 1 + buttons.length) % buttons.length;
          break;
        case 'Home':
          next = 0;
          break;
        case 'End':
          next = buttons.length - 1;
          break;
        default:
          return;
      }
      e.preventDefault();
      const target = buttons[next];
      setRoving(target);
      target.focus();
    },
    [toolbarRef, setRoving],
  );

  const onFocus = useCallback(
    (e: React.FocusEvent<HTMLDivElement>) => {
      const target = e.target as HTMLElement;
      if (target.matches(TOOLBAR_BUTTON_SELECTOR)) setRoving(target);
    },
    [setRoving],
  );

  return { onKeyDown, onFocus };
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
  const toolbarRef = useRef<HTMLDivElement>(null);
  const toolbarNav = useToolbarKeyboardNav(toolbarRef);

  // Last Markdown the editor itself emitted. Lets the sync effect below tell
  // "parent echoed our own change back" (ignore) from "value changed elsewhere"
  // (push into the editor) without re-creating the editor or moving the caret.
  const lastEmittedRef = useRef(value ?? '');
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const onBlurRef = useRef(onBlur);
  onBlurRef.current = onBlur;

  const editor = useEditor({
    extensions: [
      // StarterKit includes Bold, Italic, BulletList, OrderedList, Paragraph, etc.
      // Disable what we replace below and formatting outside the USWDS-safe toolbar.
      StarterKit.configure({
        heading: false,       // replaced by explicit Heading extension (H2-H4 only)
        blockquote: false,    // replaced by explicit Blockquote extension
        link: false,          // replaced by explicit Link extension
        code: false,
        codeBlock: false,
        horizontalRule: false,
        strike: false,
        underline: false,     // no Markdown equivalent
      }),
      // H2, H3, H4 only — H1 is intentionally excluded (page title is H1)
      Heading.configure({ levels: [2, 3, 4] }),
      Blockquote,
      Link.configure({
        openOnClick: false,
        HTMLAttributes: { rel: 'noopener noreferrer' },
      }),
      BlockImage.configure({ inline: false }),
      Markdown.configure({
        // Markdig renders with DisableHtml() — never emit raw HTML.
        html: false,
        tightLists: true,
        bulletListMarker: '-',
        linkify: false,
        breaks: false,
        // Pasting Markdown text renders it rather than leaving literal syntax.
        transformPastedText: true,
        transformCopiedText: false,
      }),
    ],
    // tiptap-markdown overrides content parsing, so a Markdown string is accepted here.
    content: value || '',
    onUpdate({ editor: ed }) {
      const md = getMarkdown(ed);
      lastEmittedRef.current = md;
      onChangeRef.current(md);
    },
    onBlur() {
      onBlurRef.current?.();
    },
    editorProps: {
      attributes: {
        id: editorId,
        // A contenteditable <div> is non-labellable, so <label htmlFor> can't be
        // used; aria-labelledby pointing at the label element is the ARIA pattern.
        'aria-labelledby': resolvedLabelId,
        'aria-multiline': 'true',
        ...(ariaDescribedby ? { 'aria-describedby': ariaDescribedby } : {}),
        ...(ariaInvalid ? { 'aria-invalid': 'true' } : {}),
        ...(ariaRequired ? { 'aria-required': 'true' } : {}),
        // usa-prose: the editor body uses the same typography as the public site.
        class: 'usa-prose va-cms-rich-editor__content',
      },
    },
  });

  // Push external value changes into the live editor (entry body arriving after
  // mount in edit mode, version restore). Skipped when the parent is just
  // echoing what we emitted, which would otherwise reset the caret on every keystroke.
  useEffect(() => {
    if (!editor) return;
    const md = value ?? '';
    if (md === lastEmittedRef.current) return;
    lastEmittedRef.current = md;
    editor.commands.setContent(md, { emitUpdate: false });
  }, [editor, value]);

  const isLinkActive = editor?.isActive('link') ?? false;

  const handleLink = useCallback(() => {
    if (!editor) return;
    const previous = (editor.getAttributes('link').href as string | undefined) ?? '';
    const url = window.prompt('Enter URL', previous)?.trim();
    if (url === undefined) return; // cancelled
    if (url === '') {
      editor.chain().focus().extendMarkRange('link').unsetLink().run();
      return;
    }
    const { empty } = editor.state.selection;
    if (empty && !isLinkActive) {
      // Nothing selected: insert the URL itself as the link text.
      editor
        .chain()
        .focus()
        .insertContent({ type: 'text', text: url, marks: [{ type: 'link', attrs: { href: url } }] })
        .run();
      return;
    }
    editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run();
  }, [editor, isLinkActive]);

  const handleMediaSelect = useCallback(
    (url: string, altText: string) => {
      if (!editor) return;
      editor.chain().focus().setImage({ src: url, alt: altText }).run();
      setIsMediaModalOpen(false);
    },
    [editor],
  );

  if (!editor) return <></>;

  return (
    <div className="va-cms-rich-editor" data-testid="rich-text-editor">
      {/* ── Toolbar ─────────────────────────────────────────────────────────── */}
      <div
        ref={toolbarRef}
        className="va-cms-rich-editor__toolbar"
        role="toolbar"
        aria-label="Text formatting"
        aria-controls={editorId}
        data-testid="rich-text-toolbar"
        onKeyDown={toolbarNav.onKeyDown}
        onFocus={toolbarNav.onFocus}
      >
        <ToolbarButton
          label="Bold"
          testId="toolbar-bold"
          active={editor.isActive('bold')}
          onClick={() => editor.chain().focus().toggleBold().run()}
        >
          <strong>B</strong>
        </ToolbarButton>

        <ToolbarButton
          label="Italic"
          testId="toolbar-italic"
          active={editor.isActive('italic')}
          onClick={() => editor.chain().focus().toggleItalic().run()}
        >
          <em>I</em>
        </ToolbarButton>

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        {/* H2 — H4: H1 intentionally omitted (page title is H1) */}
        {([2, 3, 4] as const).map((level) => (
          <ToolbarButton
            key={level}
            label={`Heading ${level}`}
            testId={`toolbar-h${level}`}
            active={editor.isActive('heading', { level })}
            onClick={() => editor.chain().focus().toggleHeading({ level }).run()}
          >
            H{level}
          </ToolbarButton>
        ))}

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        <ToolbarButton
          label="Ordered list"
          testId="toolbar-ordered-list"
          active={editor.isActive('orderedList')}
          onClick={() => editor.chain().focus().toggleOrderedList().run()}
        >
          OL
        </ToolbarButton>

        <ToolbarButton
          label="Unordered list"
          testId="toolbar-bullet-list"
          active={editor.isActive('bulletList')}
          onClick={() => editor.chain().focus().toggleBulletList().run()}
        >
          UL
        </ToolbarButton>

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        <ToolbarButton
          label={isLinkActive ? 'Edit link' : 'Insert link'}
          testId="toolbar-link"
          active={isLinkActive}
          onClick={handleLink}
        >
          Link
        </ToolbarButton>

        <ToolbarButton
          label="Block quote"
          testId="toolbar-blockquote"
          active={editor.isActive('blockquote')}
          onClick={() => editor.chain().focus().toggleBlockquote().run()}
        >
          &ldquo;&rdquo;
        </ToolbarButton>

        <span className="va-cms-toolbar__separator" aria-hidden="true">|</span>

        <ToolbarButton
          label="Insert image from library"
          testId="toolbar-image"
          hasPopup
          onClick={() => setIsMediaModalOpen(true)}
        >
          Image
        </ToolbarButton>
      </div>

      {/* ── Editor content area — this is the live preview ───────────────────── */}
      <EditorContent editor={editor} className="va-cms-rich-editor__body" />

      {/* ── Media library picker ─────────────────────────────────────────────── */}
      {isMediaModalOpen && (
        <MediaLibraryModal
          onSelect={handleMediaSelect}
          onClose={() => setIsMediaModalOpen(false)}
        />
      )}
    </div>
  );
}
