import IconButton from '@mui/material/IconButton';
import type { Editor } from '@tiptap/core';
import { DragHandle } from '@tiptap/extension-drag-handle-react';
import { useEditorState } from '@tiptap/react';
import { BubbleMenu } from '@tiptap/react/menus';
import { NodeSelection } from '@tiptap/pm/state';
import {
  AlignCenter, AlignLeft, AlignRight, Bold, Code, Columns2, GripVertical, Highlighter, Italic, Link2, Palette, Plus,
  RemoveFormatting, Rows2, Strikethrough, Trash2, Underline, type LucideIcon,
} from 'lucide-react';
import { useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { editorColors, font, radius, sem, shadow } from '../../theme/tokens';

const CODE_LANGUAGES = ['', 'typescript', 'javascript', 'csharp', 'sql', 'json', 'bash', 'xml', 'css', 'python', 'yaml'];

const menuStyle = {
  display: 'flex',
  alignItems: 'center',
  flexWrap: 'wrap',
  gap: 2,
  maxWidth: 420,
  padding: 4,
  backgroundColor: sem('bg-surface'),
  border: `1px solid ${sem('border-default')}`,
  borderRadius: radius.card,
  boxShadow: shadow.popover,
} as const;

const floating = { placement: 'top-start' } as const;

function Tool({ label, icon: Icon, active = false, onClick }: { label: string; icon: LucideIcon; active?: boolean; onClick: () => void }) {
  return (
    <IconButton
      size="small"
      title={label}
      aria-label={label}
      aria-pressed={active}
      // Keeps the text selection, which a focus change would collapse.
      onMouseDown={(event) => event.preventDefault()}
      onClick={onClick}
      sx={{ borderRadius: `${radius.control}px`, color: sem(active ? 'text-accent' : 'text-secondary') }}
    >
      <Icon size={16} strokeWidth={1.5} aria-hidden />
    </IconButton>
  );
}

const Divider = () => <span aria-hidden className="mx-1 self-stretch" style={{ borderLeft: `1px solid ${sem('border-default')}` }} />;

function Swatches({ label, colors, onPick }: { label: string; colors: { name: string; value: string | null }[]; onPick: (value: string | null) => void }) {
  const { t } = useTranslation();

  return (
    <div role="group" aria-label={label} className="flex w-full gap-1 p-1">
      {colors.map((color) => (
        <button
          key={color.name}
          type="button"
          title={t(`editor.color.${color.name}`)}
          aria-label={t(`editor.color.${color.name}`)}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => onPick(color.value)}
          className="cursor-pointer"
          style={{
            width: 24,
            height: 24,
            backgroundColor: color.value ?? sem('bg-surface'),
            border: `1px solid ${sem('border-strong')}`,
            borderRadius: radius.control,
          }}
        />
      ))}
    </div>
  );
}

/** Accepts what people paste; anything but web and mail links is refused. */
export function normalizeUrl(text: string) {
  const value = text.trim();
  const withScheme = /^[a-z][a-z0-9+.-]*:/i.test(value) ? value : `https://${value}`;
  try {
    return ['http:', 'https:', 'mailto:'].includes(new URL(withScheme).protocol) ? withScheme : null;
  } catch {
    return null;
  }
}

function LinkField({ editor, onDone }: { editor: Editor; onDone: () => void }) {
  const { t } = useTranslation();
  const [url, setUrl] = useState((editor.getAttributes('link').href as string | undefined) ?? '');

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      onDone();
      editor.commands.focus();
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      const href = normalizeUrl(url);
      if (url.trim() === '') {
        editor.chain().focus().extendMarkRange('link').unsetLink().run();
      } else if (href) {
        editor.chain().focus().extendMarkRange('link').setLink({ href }).run();
      }
      onDone();
    }
  };

  return (
    <input
      autoFocus
      aria-label={t('editor.linkUrl')}
      placeholder={t('editor.linkPlaceholder')}
      value={url}
      onChange={(event) => setUrl(event.target.value)}
      onKeyDown={onKeyDown}
      className="min-w-0 flex-1 p-1 outline-none"
      style={{ width: 280, fontSize: font.size.md, color: sem('text-primary'), backgroundColor: 'transparent' }}
    />
  );
}

function TextMenu({ editor, container }: { editor: Editor; container: HTMLElement | null }) {
  const { t } = useTranslation();
  const [panel, setPanel] = useState<'link' | 'color' | 'highlight' | null>(null);
  const chain = () => editor.chain().focus();
  const state = useEditorState({
    editor,
    selector: ({ editor: current }) => ({
      block: current.isActive('heading', { level: 1 }) ? 'h1'
        : current.isActive('heading', { level: 2 }) ? 'h2'
          : current.isActive('heading', { level: 3 }) ? 'h3' : 'p',
      bold: current.isActive('bold'),
      italic: current.isActive('italic'),
      underline: current.isActive('underline'),
      strike: current.isActive('strike'),
      code: current.isActive('code'),
      link: current.isActive('link'),
      center: current.isActive({ textAlign: 'center' }),
      right: current.isActive({ textAlign: 'right' }),
    }),
  });
  const toggle = (next: NonNullable<typeof panel>) => setPanel(panel === next ? null : next);
  const setBlock = (value: string) => {
    if (value === 'p') {
      chain().setParagraph().run();
    } else {
      chain().setHeading({ level: Number(value[1]) as 1 | 2 | 3 }).run();
    }
  };

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="textMenu"
      appendTo={() => container ?? document.body}
      options={{ ...floating, onHide: () => setPanel(null) }}
      shouldShow={({ editor: current, state: view, element }) =>
        current.isEditable
        && !(view.selection instanceof NodeSelection)
        && !view.selection.empty
        && !current.isActive('codeBlock')
        && (current.isFocused || element.contains(document.activeElement))}
    >
      <div role="toolbar" aria-label={t('editor.format')} style={menuStyle}>
        <select
          aria-label={t('editor.turnInto')}
          value={state.block}
          onChange={(event) => setBlock(event.target.value)}
          className="cursor-pointer bg-transparent p-1"
          style={{ fontSize: font.size.md, color: sem('text-primary') }}
        >
          <option value="p">{t('editor.text')}</option>
          <option value="h1">{t('editor.heading1')}</option>
          <option value="h2">{t('editor.heading2')}</option>
          <option value="h3">{t('editor.heading3')}</option>
        </select>
        <Divider />
        <Tool label={t('editor.bold')} icon={Bold} active={state.bold} onClick={() => chain().toggleBold().run()} />
        <Tool label={t('editor.italic')} icon={Italic} active={state.italic} onClick={() => chain().toggleItalic().run()} />
        <Tool label={t('editor.underline')} icon={Underline} active={state.underline} onClick={() => chain().toggleUnderline().run()} />
        <Tool label={t('editor.strike')} icon={Strikethrough} active={state.strike} onClick={() => chain().toggleStrike().run()} />
        <Tool label={t('editor.code')} icon={Code} active={state.code} onClick={() => chain().toggleCode().run()} />
        <Tool label={t('editor.link')} icon={Link2} active={state.link} onClick={() => toggle('link')} />
        <Divider />
        <Tool label={t('editor.textColor')} icon={Palette} active={panel === 'color'} onClick={() => toggle('color')} />
        <Tool label={t('editor.highlight')} icon={Highlighter} active={panel === 'highlight'} onClick={() => toggle('highlight')} />
        <Divider />
        <Tool label={t('editor.alignLeft')} icon={AlignLeft} active={!state.center && !state.right} onClick={() => chain().setTextAlign('left').run()} />
        <Tool label={t('editor.alignCenter')} icon={AlignCenter} active={state.center} onClick={() => chain().setTextAlign('center').run()} />
        <Tool label={t('editor.alignRight')} icon={AlignRight} active={state.right} onClick={() => chain().setTextAlign('right').run()} />
        <Divider />
        <Tool label={t('editor.clear')} icon={RemoveFormatting} onClick={() => chain().unsetAllMarks().clearNodes().run()} />
        {panel === 'link' && <div className="flex w-full"><LinkField editor={editor} onDone={() => setPanel(null)} /></div>}
        {panel === 'color' && (
          <Swatches
            label={t('editor.textColor')}
            colors={editorColors.text}
            onPick={(value) => (value ? chain().setColor(value).run() : chain().unsetColor().run())}
          />
        )}
        {panel === 'highlight' && (
          <Swatches
            label={t('editor.highlight')}
            colors={editorColors.highlight}
            onPick={(value) => (value ? chain().setHighlight({ color: value }).run() : chain().unsetHighlight().run())}
          />
        )}
      </div>
    </BubbleMenu>
  );
}

function TableMenu({ editor, container }: { editor: Editor; container: HTMLElement | null }) {
  const { t } = useTranslation();
  const chain = () => editor.chain().focus();

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="tableMenu"
      appendTo={() => container ?? document.body}
      options={floating}
      shouldShow={({ editor: current }) => current.isEditable && current.isFocused && current.isActive('table')}
    >
      <div role="toolbar" aria-label={t('editor.table')} style={menuStyle}>
        <Tool label={t('editor.addRow')} icon={Rows2} onClick={() => chain().addRowAfter().run()} />
        <Tool label={t('editor.addColumn')} icon={Columns2} onClick={() => chain().addColumnAfter().run()} />
        <Divider />
        <Tool label={t('editor.deleteRow')} icon={Rows2} onClick={() => chain().deleteRow().run()} />
        <Tool label={t('editor.deleteColumn')} icon={Columns2} onClick={() => chain().deleteColumn().run()} />
        <Tool label={t('editor.deleteTable')} icon={Trash2} onClick={() => chain().deleteTable().run()} />
      </div>
    </BubbleMenu>
  );
}

function CodeMenu({ editor, container }: { editor: Editor; container: HTMLElement | null }) {
  const { t } = useTranslation();
  const language = useEditorState({
    editor,
    selector: ({ editor: current }) => (current.getAttributes('codeBlock').language as string | null | undefined) ?? '',
  });

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="codeMenu"
      appendTo={() => container ?? document.body}
      options={floating}
      shouldShow={({ editor: current, element }) =>
        current.isEditable && current.isActive('codeBlock') && (current.isFocused || element.contains(document.activeElement))}
    >
      <div style={menuStyle}>
        <select
          aria-label={t('editor.language')}
          value={language}
          onChange={(event) => editor.chain().focus().updateAttributes('codeBlock', { language: event.target.value || null }).run()}
          className="cursor-pointer bg-transparent p-1"
          style={{ fontSize: font.size.md, color: sem('text-primary') }}
        >
          {CODE_LANGUAGES.map((value) => <option key={value} value={value}>{value || t('editor.plainText')}</option>)}
        </select>
      </div>
    </BubbleMenu>
  );
}

function BlockHandle({ editor }: { editor: Editor }) {
  const { t } = useTranslation();
  const block = useRef<{ end: number } | null>(null);

  const addBelow = () => {
    if (block.current) {
      const { end } = block.current;
      editor.chain().focus().insertContentAt(end, { type: 'paragraph', content: [{ type: 'text', text: '/' }] })
        .setTextSelection(end + 2).run();
    }
  };

  return (
    <DragHandle
      editor={editor}
      onNodeChange={({ node, pos }) => {
        block.current = node ? { end: pos + node.nodeSize } : null;
      }}
    >
      <div className="flex items-center" style={{ color: sem('text-secondary') }}>
        <IconButton size="small" tabIndex={-1} title={t('editor.addBlock')} aria-label={t('editor.addBlock')} onClick={addBelow} sx={{ padding: '2px', borderRadius: `${radius.control}px` }}>
          <Plus size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
        <span title={t('editor.dragBlock')} className="flex cursor-grab" aria-hidden>
          <GripVertical size={16} strokeWidth={1.5} />
        </span>
      </div>
    </DragHandle>
  );
}

export function EditorMenus({ editor, container }: { editor: Editor; container: HTMLElement | null }): ReactNode {
  return (
    <>
      <TextMenu editor={editor} container={container} />
      <TableMenu editor={editor} container={container} />
      <CodeMenu editor={editor} container={container} />
      <BlockHandle editor={editor} />
    </>
  );
}
