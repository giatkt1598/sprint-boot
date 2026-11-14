import type { Node as ProseMirrorNode } from '@tiptap/pm/model';
import { closeHistory } from '@tiptap/pm/history';
import { Plugin, PluginKey } from '@tiptap/pm/state';

/** How bare links of one kind turn into chips. */
export interface LinkChipRule<Ref> {
  /** Names the plugin; one per kind of chip. */
  name: string;
  /** Whether an address is one this rule turns into a chip. */
  matches: (url: string) => boolean;
  /** What the chip shows, or null to keep the plain link. */
  resolve: (url: string) => Promise<Ref | null>;
  create: (ref: Ref) => ProseMirrorNode;
}

const CONVERT_DELAY_MILLISECONDS = 300;

/** Bare links the rule matches: the text is the address itself, as pasting or typing one leaves it. */
export function findBareLinks(doc: ProseMirrorNode, matches: (url: string) => boolean) {
  const found: { from: number; to: number; url: string }[] = [];
  doc.descendants((node, pos) => {
    if (!node.isText || !node.text) {
      return;
    }
    const href = node.marks.find((mark) => mark.type.name === 'link')?.attrs.href as string | undefined;
    if (href && node.text.trim() === href && matches(href)) {
      found.push({ from: pos, to: pos + node.nodeSize, url: href });
    }
  });
  return found;
}

/**
 * Turns links just pasted or typed into chips once they resolve. Like Docmost, only those do: links already in the
 * text stay links, and so does one that undo brings back after its chip.
 */
export function linkChipPlugin<Ref>(rule: LinkChipRule<Ref>) {
  const key = new PluginKey<Set<string>>(`${rule.name}Convert`);
  const converted = `${rule.name}Converted`;
  // One lookup per address; a failed one stays a plain link.
  const cache = new Map<string, Promise<Ref | null>>();
  const find = (doc: ProseMirrorNode) => findBareLinks(doc, rule.matches);

  return new Plugin<Set<string>>({
    key,
    state: {
      init: () => new Set(),
      apply: (tr, fresh, before, after) => {
        const handled = tr.getMeta(converted) as string[] | undefined;
        if (handled) {
          return new Set([...fresh].filter((url) => !handled.includes(url)));
        }
        if (!tr.docChanged || tr.getMeta('history$')) {
          return fresh;
        }
        const count = (doc: ProseMirrorNode) => {
          const seen = new Map<string, number>();
          for (const { url } of find(doc)) {
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
          const fresh = key.getState(view.state);
          if (!fresh || fresh.size === 0) {
            return;
          }
          clearTimeout(timer);
          timer = setTimeout(() => {
            const urls = [...(key.getState(view.state) ?? [])];
            // Each address is tried once; whatever happens, it is no longer fresh.
            view.dispatch(view.state.tr.setMeta(converted, urls));
            for (const url of urls) {
              if (!cache.has(url)) {
                cache.set(url, rule.resolve(url).catch(() => null));
              }
              void cache.get(url)!.then((ref) => {
                if (!ref || view.isDestroyed) {
                  return;
                }
                // The text may have moved or changed while the address resolved: look it up again.
                const target = find(view.state.doc).find((link) => link.url === url);
                if (target) {
                  // Its own undo step, so Ctrl+Z turns the chip back into the pasted link.
                  const tr = closeHistory(view.state.tr)
                    .replaceWith(target.from, target.to, rule.create(ref))
                    .setMeta(converted, [url]);
                  view.dispatch(tr);
                }
              });
            }
          }, CONVERT_DELAY_MILLISECONDS);
        },
        destroy: () => clearTimeout(timer),
      };
    },
  });
}
