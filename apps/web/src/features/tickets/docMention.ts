import { Node, mergeAttributes } from '@tiptap/core';
import type { Node as ProseMirrorNode } from '@tiptap/pm/model';
import { closeHistory } from '@tiptap/pm/history';
import { Plugin, PluginKey } from '@tiptap/pm/state';

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

const CONVERT_DELAY_MILLISECONDS = 300;
const key = new PluginKey<Set<string>>('docMentionConvert');
const CONVERTED = 'docMentionConverted';

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
  const found: { from: number; to: number; url: string }[] = [];
  doc.descendants((node, pos) => {
    if (!node.isText || !node.text) {
      return;
    }
    const href = node.marks.find((mark) => mark.type.name === 'link')?.attrs.href as string | undefined;
    if (href && node.text.trim() === href && isDocmostPageUrl(href, docmostUrl)) {
      found.push({ from: pos, to: pos + node.nodeSize, url: href });
    }
  });
  return found;
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
    const type = this.type;
    // One lookup per address; a failed one stays a plain link.
    const cache = new Map<string, Promise<DocRef | null>>();

    return [
      new Plugin<Set<string>>({
        key,
        // Addresses just pasted or typed. Like Docmost, only those turn into chips: links already in the text stay
        // links, and so does one that undo brings back after its chip.
        state: {
          init: () => new Set(),
          apply: (tr, fresh, before, after) => {
            const handled = tr.getMeta(CONVERTED) as string[] | undefined;
            if (handled) {
              return new Set([...fresh].filter((url) => !handled.includes(url)));
            }
            if (!tr.docChanged || tr.getMeta('history$') || !docmostUrl()) {
              return fresh;
            }
            const count = (doc: ProseMirrorNode) => {
              const seen = new Map<string, number>();
              for (const { url } of findDocmostLinks(doc, docmostUrl())) {
                seen.set(url, (seen.get(url) ?? 0) + 1);
              }
              return seen;
            };
            const old = count(before.doc);
            const added = [...count(after.doc)].filter(([url, times]) => times > (old.get(url) ?? 0)).map(([url]) => url);
            return added.length === 0 ? fresh : new Set([...fresh, ...added]);
          },
        },
        view: () => {
          let timer: ReturnType<typeof setTimeout> | undefined;
          return {
            update: (view) => {
              const fresh = key.getState(view.state) as Set<string> | undefined;
              if (!fresh || fresh.size === 0) {
                return;
              }
              clearTimeout(timer);
              timer = setTimeout(() => {
                const urls = [...(key.getState(view.state) as Set<string>)];
                // Each address is tried once; whatever happens, it is no longer fresh.
                view.dispatch(view.state.tr.setMeta(CONVERTED, urls));
                for (const url of urls) {
                  if (!cache.has(url)) {
                    cache.set(url, resolve(url).catch(() => null));
                  }
                  void cache.get(url)!.then((ref) => {
                    if (!ref || view.isDestroyed) {
                      return;
                    }
                    // The text may have moved or changed while the page resolved: look it up again.
                    const target = findDocmostLinks(view.state.doc, docmostUrl()).find((link) => link.url === url);
                    if (target) {
                      // Its own undo step, so Ctrl+Z turns the chip back into the pasted link.
                      const tr = closeHistory(view.state.tr)
                        .replaceWith(target.from, target.to, type.create({ pageId: ref.pageId, url: ref.url, title: ref.title }))
                        .setMeta(CONVERTED, [url]);
                      view.dispatch(tr);
                    }
                  });
                }
              }, CONVERT_DELAY_MILLISECONDS);
            },
            destroy: () => clearTimeout(timer),
          };
        },
      }),
    ];
  },
});
