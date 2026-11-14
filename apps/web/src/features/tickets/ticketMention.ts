import { Node, mergeAttributes } from '@tiptap/core';
import { linkChipPlugin } from './linkChips';

/** What a chip for a ticket keeps in the saved text; the rest is looked up live when it shows. */
export interface TicketRef {
  key: string;
  title: string;
  url: string;
}

export interface TicketMentionOptions {
  /** Where Sprint Boot is served from: only its own ticket addresses turn into chips. */
  appUrl: () => string;
  /** The ticket an address opens, or null when it is none the person can see. */
  resolve: (url: string) => Promise<TicketRef | null>;
}

const TICKET_PATH = /^\/(?:browse|tickets|issues)\/([A-Za-z][A-Za-z0-9]*-\d+)\/?$/;

/** The key in a pasted ticket address on any Sprint Boot server, or null when the text is no ticket address. */
export function ticketKeyInAddress(text: string): string | null {
  try {
    return TICKET_PATH.exec(new URL(text.trim()).pathname)?.[1]?.toUpperCase() ?? null;
  } catch {
    return null;
  }
}

/** The ticket key of a Sprint Boot ticket address (/browse/KEY-12), or null for any other address. */
export function ticketKeyFromUrl(url: string, appUrl: string): string | null {
  try {
    const page = new URL(url);
    const root = new URL(appUrl);
    if (page.host.toLowerCase() !== root.host.toLowerCase()) {
      return null;
    }
    return TICKET_PATH.exec(page.pathname)?.[1]?.toUpperCase() ?? null;
  } catch {
    return null;
  }
}

/**
 * A ticket (an epic is one too) as a chip, like Docmost shows a Sprint Boot link. Saved as
 * `<a data-type="ticket-mention" class="ticket-mention" data-ticket-key href>KEY-12 Title</a>`, so text without the app
 * still reads as a link; the app shows the ticket's type, title and status live. Pasting or typing a ticket address
 * turns into one once the ticket resolves.
 */
export const TicketMention = Node.create<TicketMentionOptions>({
  name: 'ticketMention',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addOptions() {
    return { appUrl: () => window.location.origin, resolve: async () => null };
  },

  addAttributes() {
    return {
      ticketKey: {
        default: '',
        parseHTML: (element) => element.getAttribute('data-ticket-key') ?? '',
        renderHTML: (attributes) => ({ 'data-ticket-key': attributes.ticketKey }),
      },
      url: {
        default: '',
        parseHTML: (element) => element.getAttribute('href') ?? '',
        renderHTML: (attributes) => ({ href: attributes.url }),
      },
      title: {
        default: '',
        // The text is "KEY-12 Title"; the key is kept apart.
        parseHTML: (element) => {
          const text = element.textContent ?? '';
          const key = element.getAttribute('data-ticket-key') ?? '';
          return (key && text.startsWith(key) ? text.slice(key.length) : text).trim();
        },
        renderHTML: () => ({}),
      },
    };
  },

  // Above the link mark, so a saved chip comes back as a chip rather than a link.
  parseHTML() {
    return [{ tag: 'a[data-type="ticket-mention"]', priority: 60 }];
  },

  renderHTML({ node, HTMLAttributes }) {
    return ['a', mergeAttributes({ 'data-type': 'ticket-mention', class: 'ticket-mention' }, HTMLAttributes), chipText(node.attrs)];
  },

  renderText({ node }) {
    return chipText(node.attrs);
  },

  addProseMirrorPlugins() {
    const { appUrl, resolve } = this.options;
    return [
      linkChipPlugin<TicketRef>({
        name: 'ticketMention',
        matches: (url) => ticketKeyFromUrl(url, appUrl()) !== null,
        resolve,
        create: (ref) => this.type.create({ ticketKey: ref.key, url: ref.url, title: ref.title }),
      }),
    ];
  },
});

function chipText(attrs: Record<string, unknown>) {
  return [attrs.ticketKey, attrs.title].filter(Boolean).join(' ') || String(attrs.url ?? '');
}
