import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DocMention, isDocmostPageUrl } from './docMention';

const DOCMOST = 'https://docs.acme.test';
const PAGE = `${DOCMOST}/s/eng/p/design-notes-abc123`;

describe('isDocmostPageUrl', () => {
  it('knows the page addresses of the connected Docmost', () => {
    expect(isDocmostPageUrl(PAGE, DOCMOST)).toBe(true);
    expect(isDocmostPageUrl(`${DOCMOST}/p/abc123`, DOCMOST)).toBe(true);
  });

  it('leaves other addresses alone', () => {
    expect(isDocmostPageUrl(`${DOCMOST}/s/eng`, DOCMOST)).toBe(false);
    expect(isDocmostPageUrl('https://elsewhere.test/s/eng/p/x-abc123', DOCMOST)).toBe(false);
    expect(isDocmostPageUrl(PAGE, null)).toBe(false);
  });
});

describe('DocMention', () => {
  let editor: Editor | undefined;
  afterEach(() => {
    editor?.destroy();
    vi.useRealTimers();
  });

  const create = (resolve: (url: string) => Promise<{ pageId: string; title: string; url: string } | null>, docmostUrl: string | null = DOCMOST) =>
    new Editor({
      element: document.createElement('div'),
      extensions: [StarterKit, DocMention.configure({ docmostUrl: () => docmostUrl, resolve })],
      content: '<p></p>',
    });

  const pasteLink = (target: Editor, url: string) =>
    target.commands.insertContent({ type: 'text', text: url, marks: [{ type: 'link', attrs: { href: url } }] });

  it('turns a pasted page address into a chip once the page resolves', async () => {
    const resolve = vi.fn(async () => ({ pageId: 'p1', title: 'Design notes', url: PAGE }));
    editor = create(resolve);

    pasteLink(editor, PAGE);

    await vi.waitFor(() => expect(editor!.getHTML()).toContain('data-type="doc-mention"'));
    expect(editor.getHTML()).toContain('data-page-id="p1"');
    expect(editor.getHTML()).toContain('>Design notes</a>');
    expect(resolve).toHaveBeenCalledWith(PAGE);
  });

  it('keeps a plain link when the page does not resolve, or Docmost is not connected', async () => {
    editor = create(async () => null);
    pasteLink(editor, PAGE);
    await new Promise((done) => setTimeout(done, 500));
    expect(editor.getHTML()).not.toContain('doc-mention');
    editor.destroy();

    const resolve = vi.fn(async () => ({ pageId: 'p1', title: 'Design notes', url: PAGE }));
    editor = create(resolve, null);
    pasteLink(editor, PAGE);
    await new Promise((done) => setTimeout(done, 500));
    expect(resolve).not.toHaveBeenCalled();
  });

  it('reads a saved chip back as a chip', () => {
    editor = create(async () => null);
    editor.commands.setContent(`<p><a data-type="doc-mention" class="doc-mention" data-page-id="p1" href="${PAGE}">Design notes</a></p>`);

    expect(editor.getJSON().content?.[0]?.content?.[0]).toMatchObject({ type: 'docMention', attrs: { pageId: 'p1', title: 'Design notes', url: PAGE } });
  });
});
