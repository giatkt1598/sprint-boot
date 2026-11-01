import { EditorContent, useEditor, type Editor } from '@tiptap/react';
import hljs from 'highlight.js/lib/common';
import {
  AtSign, Code2, Heading1, Heading2, Heading3, ImageIcon, Lightbulb, List, ListChecks, ListOrdered, Minus, Quote, Table, Type,
  type LucideIcon,
} from 'lucide-react';
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import Typography from '@mui/material/Typography';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { TextField } from '../../components/TextField';
import { font, radius, sem, shadow } from '../../theme/tokens';
import { EditorMenus, EditorToolbar, normalizeUrl } from './RichTextMenus';
import { createExtensions, type MentionUser, type PopupState, type SlashCommand } from './richTextExtensions';

export type { MentionUser } from './richTextExtensions';

interface RichTextEditorProps {
  label: string;
  initialValue?: string;
  people: MentionUser[];
  autoFocus?: boolean;
  /** Shows the label above the box, for use among other labelled fields. */
  showLabel?: boolean;
  onChange: (html: string) => void;
}

const COMMIT_DELAY_MILLISECONDS = 200;
const POPUP_WIDTH = 280;
const POPUP_MAX_HEIGHT = 300;

const SLASH_ICONS: Record<string, LucideIcon> = {
  text: Type,
  heading1: Heading1,
  heading2: Heading2,
  heading3: Heading3,
  bullets: List,
  numbers: ListOrdered,
  tasks: ListChecks,
  quote: Quote,
  callout: Lightbulb,
  codeBlock: Code2,
  divider: Minus,
  table: Table,
  image: ImageIcon,
  mention: AtSign,
};

/** Portaled so a dialog cannot clip it; it never takes focus, so typing keeps filtering. */
function SuggestionPopup<Item>({ popup, rowKey, render }: { popup: PopupState<Item>; rowKey: (item: Item) => string; render: (item: Item) => React.ReactNode }) {
  const { t } = useTranslation();
  const list = useRef<HTMLDivElement>(null);

  useEffect(() => {
    list.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [popup.active]);

  const rect = popup.rect;
  const above = rect !== null && rect.bottom + POPUP_MAX_HEIGHT > window.innerHeight && rect.top > POPUP_MAX_HEIGHT;

  return createPortal(
    <div
      ref={list}
      role="listbox"
      aria-label={t('editor.suggestions')}
      className="flex flex-col gap-1 p-1"
      style={{
        position: 'fixed',
        zIndex: 1400,
        left: rect?.left ?? 0,
        ...(above ? { bottom: window.innerHeight - (rect?.top ?? 0) + 4 } : { top: (rect?.bottom ?? 0) + 4 }),
        width: POPUP_WIDTH,
        maxHeight: POPUP_MAX_HEIGHT,
        overflowY: 'auto',
        backgroundColor: sem('bg-surface'),
        border: `1px solid ${sem('border-default')}`,
        borderRadius: radius.card,
        boxShadow: shadow.popover,
      }}
    >
      {popup.items.length === 0 && <Typography variant="body2" color="textSecondary" className="p-2">{t('editor.noResults')}</Typography>}
      {popup.items.map((item, index) => (
        <button
          key={rowKey(item)}
          type="button"
          role="option"
          aria-selected={index === popup.active}
          onMouseDown={(event) => {
            event.preventDefault();
            popup.choose(index);
          }}
          className="flex w-full cursor-pointer items-center gap-2 border-0 p-2 text-left"
          style={{
            borderRadius: radius.control,
            color: sem('text-primary'),
            backgroundColor: index === popup.active ? sem('bg-selected') : 'transparent',
          }}
        >
          {render(item)}
        </button>
      ))}
    </div>,
    document.body,
  );
}

function ImageDialog({ onClose, onInsert }: { onClose: () => void; onInsert: (url: string) => void }) {
  const { t } = useTranslation();
  const [url, setUrl] = useState('');
  const href = normalizeUrl(url);

  return (
    <Dialog
      open
      title={t('editor.insertImage')}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('editor.insert')} confirmDisabled={!href} onCancel={onClose} onConfirm={() => href && onInsert(href)} />}
    >
      <TextField label={t('editor.imageUrl')} autoFocus value={url} onChange={(event) => setUrl(event.target.value)} />
    </Dialog>
  );
}

export function RichTextEditor({ label, initialValue = '', people, autoFocus, showLabel, onChange }: RichTextEditorProps) {
  const { t } = useTranslation();
  const [mentionPopup, setMentionPopup] = useState<PopupState<MentionUser> | null>(null);
  const [slashPopup, setSlashPopup] = useState<PopupState<SlashCommand> | null>(null);
  const [imageOpen, setImageOpen] = useState(false);
  const [container, setContainer] = useState<HTMLDivElement | null>(null);

  // Parents rebuild `people` on every render. Rebuilding the editor with it wiped the text after each
  // keystroke, so the editor is created once and reads the latest values from here.
  const latest = useRef({ people, t, onChange });
  useLayoutEffect(() => {
    latest.current = { people, t, onChange };
  });

  // A parent form re-renders on each commit, so typing hands over the text only in pauses and on blur.
  // A pause is still needed: Save buttons enable from this value while the focus is in the editor.
  const pending = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const commit = useCallback((current: Editor) => {
    clearTimeout(pending.current);
    latest.current.onChange(current.isEmpty ? '' : current.getHTML());
  }, []);
  useEffect(() => () => clearTimeout(pending.current), []);

  const slashCommands = useCallback((): SlashCommand[] => {
    const tr = latest.current.t;
    const start = (editor: Editor, range: { from: number; to: number }) => editor.chain().focus().deleteRange(range);
    const row = (id: string, keywords: string, run: SlashCommand['run']): SlashCommand => ({ id, label: tr(`editor.${id}`), keywords, run });
    return [
      row('text', 'paragraph', (editor, range) => start(editor, range).setParagraph().run()),
      row('heading1', 'h1 title', (editor, range) => start(editor, range).setHeading({ level: 1 }).run()),
      row('heading2', 'h2', (editor, range) => start(editor, range).setHeading({ level: 2 }).run()),
      row('heading3', 'h3', (editor, range) => start(editor, range).setHeading({ level: 3 }).run()),
      row('bullets', 'ul list', (editor, range) => start(editor, range).toggleBulletList().run()),
      row('numbers', 'ol list', (editor, range) => start(editor, range).toggleOrderedList().run()),
      row('tasks', 'todo checkbox list', (editor, range) => start(editor, range).toggleTaskList().run()),
      row('quote', 'blockquote', (editor, range) => start(editor, range).toggleBlockquote().run()),
      row('callout', 'note info', (editor, range) => start(editor, range).wrapIn('callout').run()),
      row('codeBlock', 'pre snippet', (editor, range) => start(editor, range).setCodeBlock().run()),
      row('divider', 'hr line', (editor, range) => start(editor, range).setHorizontalRule().run()),
      row('table', 'grid', (editor, range) => start(editor, range).insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()),
      row('image', 'picture photo', (editor, range) => {
        start(editor, range).run();
        setImageOpen(true);
      }),
      row('mention', 'person user', (editor, range) => start(editor, range).insertContent('@').run()),
    ];
  }, []);

  const [extensions] = useState(() =>
    createExtensions({
      placeholder: () => latest.current.t('editor.placeholder'),
      people: () => latest.current.people,
      commands: slashCommands,
      onMentionPopup: setMentionPopup,
      onSlashPopup: setSlashPopup,
    }),
  );

  const editor = useEditor({
    extensions,
    content: initialValue,
    autofocus: autoFocus ? 'end' : false,
    editorProps: {
      attributes: { 'aria-label': label, role: 'textbox', 'aria-multiline': 'true', class: 'rich-text outline-none' },
    },
    onUpdate: ({ editor: current }) => {
      clearTimeout(pending.current);
      pending.current = setTimeout(() => commit(current), COMMIT_DELAY_MILLISECONDS);
    },
    onBlur: ({ editor: current }) => commit(current),
  });

  return (
    <div className="flex flex-col gap-1">
    {showLabel && (
      <span aria-hidden style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>{label}</span>
    )}
    <div ref={setContainer} className="rich-text-editor">
      {editor && <EditorToolbar editor={editor} onImage={() => setImageOpen(true)} />}
      <EditorContent editor={editor} />
      {editor && <EditorMenus editor={editor} container={container} />}
      {slashPopup && (
        <SuggestionPopup
          popup={slashPopup}
          rowKey={(item) => item.id}
          render={(item) => {
            const Icon = SLASH_ICONS[item.id] ?? Type;
            return (
              <>
                <Icon size={16} strokeWidth={1.5} aria-hidden />
                {item.label}
              </>
            );
          }}
        />
      )}
      {mentionPopup && <SuggestionPopup popup={mentionPopup} rowKey={(item) => item.id ?? ''} render={(item) => item.name} />}
      {imageOpen && (
        <ImageDialog
          onClose={() => {
            setImageOpen(false);
            editor?.commands.focus();
          }}
          onInsert={(src) => {
            setImageOpen(false);
            editor?.chain().focus().setImage({ src }).run();
          }}
        />
      )}
    </div>
    </div>
  );
}

/** Backend-sanitized HTML, the only place user markup reaches the DOM. */
export function RichTextView({ html }: { html: string }) {
  const view = useRef<HTMLDivElement>(null);

  useEffect(() => {
    view.current?.querySelectorAll<HTMLElement>('pre code[class*="language-"]').forEach((block) => hljs.highlightElement(block));

    // The editor names checkboxes only while editing; screen readers need a name in saved text too.
    view.current?.querySelectorAll('input[type="checkbox"]').forEach((box) => {
      box.setAttribute('aria-label', box.closest('li')?.textContent.trim() ?? '');
    });
  }, [html]);

  return <div ref={view} className="rich-text" dangerouslySetInnerHTML={{ __html: html }} />;
}
