import { Extension, Node, mergeAttributes, type Editor, type Range } from '@tiptap/core';
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight';
import Highlight from '@tiptap/extension-highlight';
import Image from '@tiptap/extension-image';
import { TaskItem, TaskList } from '@tiptap/extension-list';
import Mention from '@tiptap/extension-mention';
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import TextAlign from '@tiptap/extension-text-align';
import { Color, TextStyle } from '@tiptap/extension-text-style';
import Typography from '@tiptap/extension-typography';
import { Placeholder } from '@tiptap/extensions';
import { TextSelection } from '@tiptap/pm/state';
import { PluginKey } from '@tiptap/pm/state';
import StarterKit from '@tiptap/starter-kit';
import Suggestion, { type SuggestionKeyDownProps, type SuggestionProps } from '@tiptap/suggestion';
import { common, createLowlight } from 'lowlight';

export interface MentionUser {
  id?: string;
  name?: string | null;
}

/** One row of the slash menu. */
export interface SlashCommand {
  id: string;
  label: string;
  keywords: string;
  run: (editor: Editor, range: Range) => void;
}

/** What the open suggestion popup shows. Null when nothing is open. */
export interface PopupState<Item> {
  items: Item[];
  active: number;
  rect: DOMRect | null;
  choose: (index: number) => void;
}

const lowlight = createLowlight(common);

const Callout = Node.create({
  name: 'callout',
  group: 'block',
  content: 'block+',
  defining: true,
  parseHTML: () => [{ tag: 'div[data-type="callout"]' }],
  renderHTML: ({ HTMLAttributes }) => ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'callout', class: 'callout' }), 0],
});

/** Swaps the block at the cursor with its neighbour: the keyboard route for drag and drop. */
function moveBlock(editor: Editor, direction: -1 | 1) {
  const { state, view } = editor;
  const { $from, from: cursor } = state.selection;
  if ($from.depth < 1) {
    return false;
  }

  const index = $from.index(0);
  const target = index + direction;
  if (target < 0 || target >= state.doc.childCount) {
    return false;
  }

  const start = $from.before(1);
  const block = state.doc.child(index);
  const neighbour = state.doc.child(target);
  const tr = state.tr;
  if (direction < 0) {
    tr.delete(start, start + block.nodeSize).insert(start - neighbour.nodeSize, block);
  } else {
    tr.insert(start + block.nodeSize + neighbour.nodeSize, block).delete(start, start + block.nodeSize);
  }

  const moved = cursor + direction * neighbour.nodeSize;
  view.dispatch(tr.setSelection(TextSelection.near(tr.doc.resolve(moved))).scrollIntoView());
  return true;
}

const Video = Node.create({
  name: 'video',
  group: 'block',
  atom: true,
  addAttributes: () => ({ src: { default: null } }),
  parseHTML: () => [{ tag: 'video[src]' }],
  renderHTML: ({ HTMLAttributes }) => ['video', mergeAttributes(HTMLAttributes, { controls: 'true' })],
});

const BlockMover = Extension.create({
  name: 'blockMover',
  addKeyboardShortcuts() {
    return {
      'Alt-ArrowUp': () => moveBlock(this.editor, -1),
      'Alt-ArrowDown': () => moveBlock(this.editor, 1),
    };
  },
});

function popupRenderer<Item, Selected>(onChange: (state: PopupState<Item> | null) => void) {
  return () => {
    let current: SuggestionProps<Item, Selected> | null = null;
    let active = 0;

    const choose = (index: number) => {
      const item = current?.items[index];
      if (item) {
        current?.command(item as unknown as Selected);
      }
    };
    const paint = () => {
      if (current) {
        onChange({ items: current.items, active, rect: current.clientRect?.() ?? null, choose });
      }
    };

    return {
      onStart(props: SuggestionProps<Item, Selected>) {
        current = props;
        active = 0;
        paint();
      },
      onUpdate(props: SuggestionProps<Item, Selected>) {
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
        if (event.key === 'Enter' && count > 0) {
          choose(active);
          return true;
        }
        return event.key === 'Escape';
      },
      onExit() {
        current = null;
        onChange(null);
      },
    };
  };
}

const SlashMenu = Extension.create<{
  commands: () => SlashCommand[];
  onChange: (state: PopupState<SlashCommand> | null) => void;
}>({
  name: 'slashMenu',
  addProseMirrorPlugins() {
    const { commands, onChange } = this.options;
    return [
      Suggestion<SlashCommand, SlashCommand>({
        editor: this.editor,
        char: '/',
        pluginKey: new PluginKey('slashMenu'),
        allow: ({ state }) => !state.selection.$from.parent.type.spec.code,
        items: ({ query }) => {
          const text = query.toLowerCase();
          return commands().filter((item) => `${item.label} ${item.keywords}`.toLowerCase().includes(text));
        },
        command: ({ editor, range, props }) => props.run(editor, range),
        render: popupRenderer<SlashCommand, SlashCommand>(onChange),
      }),
    ];
  },
});

export interface RichTextOptions {
  placeholder: () => string;
  people: () => MentionUser[];
  commands: () => SlashCommand[];
  onMentionPopup: (state: PopupState<MentionUser> | null) => void;
  onSlashPopup: (state: PopupState<SlashCommand> | null) => void;
}

/** Everything reads live values through callbacks, so these extensions never need rebuilding. */
export function createExtensions(options: RichTextOptions) {
  return [
    StarterKit.configure({
      codeBlock: false,
      heading: { levels: [1, 2, 3] },
      link: { openOnClick: false, autolink: true, HTMLAttributes: { rel: 'noopener noreferrer', target: '_blank' } },
    }),
    Placeholder.configure({ placeholder: () => options.placeholder(), showOnlyCurrent: true }),
    CodeBlockLowlight.configure({ lowlight }),
    TaskList,
    TaskItem.configure({ nested: true }),
    Table.configure({ resizable: false }),
    TableRow,
    TableHeader,
    TableCell,
    TextStyle,
    Color,
    Highlight.configure({ multicolor: true }),
    TextAlign.configure({ types: ['heading', 'paragraph'] }),
    // Dragging a handle stores the new size as width and height attributes, which the saved HTML keeps.
    Image.configure({ resize: { enabled: true, directions: ['left', 'right', 'bottom-left', 'bottom-right'], minWidth: 64, alwaysPreserveAspectRatio: true } }),
    Typography,
    Callout,
    Video,
    BlockMover,
    SlashMenu.configure({ commands: options.commands, onChange: options.onSlashPopup }),
    Mention.configure({
      HTMLAttributes: { class: 'mention' },
      suggestion: {
        items: ({ query }: { query: string }) =>
          options.people().filter((person) => person.name?.toLowerCase().includes(query.toLowerCase())).slice(0, 6),
        render: popupRenderer<MentionUser, { id: string; label: string }>(options.onMentionPopup),
        command: ({ editor, range, props }) => {
          const person = props as unknown as MentionUser;
          editor.chain().focus().insertContentAt(range, [
            { type: 'mention', attrs: { id: person.id, label: person.name ?? '' } },
            { type: 'text', text: ' ' },
          ]).run();
        },
      },
    }),
  ];
}
