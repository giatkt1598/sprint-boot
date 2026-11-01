import IconButton from '@mui/material/IconButton';
import Mention from '@tiptap/extension-mention';
import { EditorContent, useEditor, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import type { SuggestionKeyDownProps, SuggestionProps } from '@tiptap/suggestion';
import { Bold, Code, Italic, List, ListOrdered, Underline, type LucideIcon } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { radius, sem, shadow } from '../../theme/tokens';

export interface MentionUser {
  id?: string;
  name?: string | null;
}

interface RichTextEditorProps {
  label: string;
  initialValue?: string;
  people: MentionUser[];
  autoFocus?: boolean;
  onChange: (html: string) => void;
}

type MentionProps = SuggestionProps<MentionUser, { id: string; label: string }>;

/** A small accessible popup; rendered by hand because TipTap owns the editor DOM. */
function mentionRenderer() {
  let list: HTMLDivElement | null = null;
  let current: MentionProps | null = null;
  let active = 0;

  const paint = () => {
    if (!list || !current) {
      return;
    }
    list.replaceChildren(
      ...current.items.map((item, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = item.name ?? '';
        button.setAttribute('role', 'option');
        button.setAttribute('aria-selected', String(index === active));
        button.style.cssText = `display:block;width:100%;text-align:left;padding:8px;border:0;border-radius:${radius.control}px;cursor:pointer;background:${index === active ? sem('bg-selected') : 'transparent'};color:${sem('text-primary')}`;
        button.onmousedown = (event) => {
          event.preventDefault();
          choose(index);
        };
        return button;
      }),
    );
    const rect = current.clientRect?.();
    if (rect) {
      list.style.left = `${rect.left}px`;
      list.style.top = `${rect.bottom + 4}px`;
    }
  };

  const choose = (index: number) => {
    const item = current?.items[index];
    if (item?.id) {
      current?.command({ id: item.id, label: item.name ?? '' });
    }
  };

  return {
    onStart(props: MentionProps) {
      current = props;
      active = 0;
      list = document.createElement('div');
      list.setAttribute('role', 'listbox');
      list.style.cssText = `position:fixed;z-index:1400;width:240px;padding:8px;background:${sem('bg-surface')};border:1px solid ${sem('border-default')};border-radius:${radius.card}px;box-shadow:${shadow.popover}`;
      document.body.append(list);
      paint();
    },
    onUpdate(props: MentionProps) {
      current = props;
      active = 0;
      paint();
    },
    onKeyDown({ event }: SuggestionKeyDownProps) {
      const count = current?.items.length ?? 0;
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        active = count ? (active + (event.key === 'ArrowDown' ? 1 : count - 1)) % count : 0;
        paint();
        return true;
      }
      if (event.key === 'Enter') {
        choose(active);
        return true;
      }
      return event.key === 'Escape';
    },
    onExit() {
      list?.remove();
      list = null;
      current = null;
    },
  };
}

function ToolbarButton({ label, icon: Icon, active, onClick }: { label: string; icon: LucideIcon; active: boolean; onClick: () => void }) {
  return (
    <IconButton
      size="small"
      aria-label={label}
      aria-pressed={active}
      onClick={onClick}
      sx={{ borderRadius: `${radius.control}px`, color: sem(active ? 'text-accent' : 'text-secondary') }}
    >
      <Icon size={16} strokeWidth={1.5} aria-hidden />
    </IconButton>
  );
}

function Toolbar({ editor }: { editor: Editor }) {
  const { t } = useTranslation();
  const chain = () => editor.chain().focus();

  return (
    <div role="toolbar" aria-label={t('issues.description')} className="flex gap-1 p-1" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
      <ToolbarButton label={t('issues.toolbarBold')} icon={Bold} active={editor.isActive('bold')} onClick={() => chain().toggleBold().run()} />
      <ToolbarButton label={t('issues.toolbarItalic')} icon={Italic} active={editor.isActive('italic')} onClick={() => chain().toggleItalic().run()} />
      <ToolbarButton label={t('issues.toolbarUnderline')} icon={Underline} active={editor.isActive('underline')} onClick={() => chain().toggleUnderline().run()} />
      <ToolbarButton label={t('issues.toolbarBullets')} icon={List} active={editor.isActive('bulletList')} onClick={() => chain().toggleBulletList().run()} />
      <ToolbarButton label={t('issues.toolbarNumbers')} icon={ListOrdered} active={editor.isActive('orderedList')} onClick={() => chain().toggleOrderedList().run()} />
      <ToolbarButton label={t('issues.toolbarCode')} icon={Code} active={editor.isActive('codeBlock')} onClick={() => chain().toggleCodeBlock().run()} />
    </div>
  );
}

export function RichTextEditor({ label, initialValue = '', people, autoFocus, onChange }: RichTextEditorProps) {
  const extensions = useMemo(
    () => [
      StarterKit,
      Mention.configure({
        HTMLAttributes: { class: 'mention' },
        suggestion: {
          items: ({ query }: { query: string }) =>
            people.filter((person) => person.name?.toLowerCase().includes(query.toLowerCase())).slice(0, 6),
          render: mentionRenderer,
        },
      }),
    ],
    [people],
  );

  const editor = useEditor(
    {
      extensions,
      content: initialValue,
      autofocus: autoFocus ? 'end' : false,
      editorProps: {
        attributes: { 'aria-label': label, role: 'textbox', 'aria-multiline': 'true', class: 'rich-text p-3 outline-none' },
      },
      onUpdate: ({ editor: current }) => onChange(current.isEmpty ? '' : current.getHTML()),
    },
    [extensions],
  );

  return (
    <div style={{ border: `1px solid ${sem('border-strong')}`, borderRadius: radius.control }}>
      {editor && <Toolbar editor={editor} />}
      <EditorContent editor={editor} />
    </div>
  );
}

/** Backend-sanitized HTML, the only place user markup reaches the DOM. */
export function RichTextView({ html }: { html: string }) {
  return <div className="rich-text" dangerouslySetInnerHTML={{ __html: html }} />;
}
