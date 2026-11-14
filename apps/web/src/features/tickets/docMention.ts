import { Node, mergeAttributes } from '@tiptap/core';
import type { Node as ProseMirrorNode } from '@tiptap/pm/model';
import { findBareLinks, linkChipPlugin } from './linkChips';

/** What a chip for a Docmost page shows. */
export interface DocRef {
  pageId: string;
  title: string;
  url: string;
}

export interface DocMentionOptions {
  /** Where Docmost is served from; null while Docmost is not connected, which turns the conversion off. */
  docmostUrl: () => string | null;
  /** The page an address opens, or null when it is no Docmost page. */
  resolve: (url: string) => Promise<DocRef | null>;
}

/** A Docmost page address on the connected server: /s/{space}/p/{slug} or /p/{slug}. */
export function isDocmostPageUrl(url: string, docmostUrl: string | null): boolean {
  if (!docmostUrl) {
    return false;
  }
  try {
    const page = new URL(url);
    const root = new URL(docmostUrl);
    if (page.host.toLowerCase() !== root.host.toLowerCase()) {
      return false;
    }
    const prefix = root.pathname.replace(/\/+$/, '');
    const path = page.pathname.slice(prefix.length);
    return page.pathname.startsWith(prefix) && /^(\/s\/[^/]+)?\/p\/[^/?#]+\/?$/.test(path);
  } catch {
    return false;
  }
}

/** Bare links to a Docmost page: the text is the address itself, as pasting or typing one leaves it. */
export function findDocmostLinks(doc: ProseMirrorNode, docmostUrl: string | null) {
  return findBareLinks(doc, (url) => isDocmostPageUrl(url, docmostUrl));
}

/**
 * A Docmost page as a chip, like a person's @mention. Saved as
 * `<a data-type="doc-mention" class="doc-mention" data-page-id href>Title</a>`, which the server reads to link the page
 * to the ticket; pasting or typing a Docmost page address turns into one once the page resolves.
 */
export const DocMention = Node.create<DocMentionOptions>({
  name: 'docMention',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addOptions() {
    return { docmostUrl: () => null, resolve: async () => null };
  },

  addAttributes() {
    return {
      pageId: {
        default: null,
        parseHTML: (element) => element.getAttribute('data-page-id'),
        renderHTML: (attributes) => ({ 'data-page-id': attributes.pageId }),
      },
      url: {
        default: '',
        parseHTML: (element) => element.getAttribute('href') ?? '',
        renderHTML: (attributes) => ({ href: attributes.url }),
      },
      title: {
        default: '',
        parseHTML: (element) => element.textContent ?? '',
        renderHTML: () => ({}),
      },
    };
  },

  // Above the link mark, so a saved chip comes back as a chip rather than a link.
  parseHTML() {
    return [{ tag: 'a[data-type="doc-mention"]', priority: 60 }];
  },

  renderHTML({ node, HTMLAttributes }) {
    return [
      'a',
      mergeAttributes({ 'data-type': 'doc-mention', class: 'doc-mention', target: '_blank', rel: 'noopener noreferrer' }, HTMLAttributes),
      node.attrs.title || node.attrs.url,
    ];
  },

  renderText({ node }) {
    return node.attrs.title || node.attrs.url;
  },

  addProseMirrorPlugins() {
    const { docmostUrl, resolve } = this.options;
    return [
      linkChipPlugin<DocRef>({
        name: 'docMention',
        matches: (url) => isDocmostPageUrl(url, docmostUrl()),
        resolve,
        create: (ref) => this.type.create({ pageId: ref.pageId, url: ref.url, title: ref.title }),
      }),
    ];
  },
});
