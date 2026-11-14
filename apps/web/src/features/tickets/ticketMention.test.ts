import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TicketMention, ticketKeyFromUrl, ticketKeyInAddress, type TicketRef } from './ticketMention';

const APP = 'https://sprint.acme.test';
const TICKET = `${APP}/browse/SB-12`;

describe('ticketKeyFromUrl', () => {
  it('knows the ticket addresses of this app, epics included', () => {
    expect(ticketKeyFromUrl(TICKET, APP)).toBe('SB-12');
    expect(ticketKeyFromUrl(`${APP}/browse/sb-3/`, APP)).toBe('SB-3');
    expect(ticketKeyFromUrl(`${APP}/tickets/SB-4?tab=history`, APP)).toBe('SB-4');
  });

  it('leaves other addresses alone', () => {
    expect(ticketKeyFromUrl(`${APP}/spaces/SB/board`, APP)).toBeNull();
    expect(ticketKeyFromUrl('https://elsewhere.test/browse/SB-12', APP)).toBeNull();
    expect(ticketKeyFromUrl('not a url', APP)).toBeNull();
  });
});

describe('ticketKeyInAddress', () => {
  it('reads the key of a pasted ticket address from any Sprint Boot server', () => {
    expect(ticketKeyInAddress(' https://sprint-boot.example.com/browse/doc-4?tab=history ')).toBe('DOC-4');
    expect(ticketKeyInAddress('SB-12')).toBeNull();
    expect(ticketKeyInAddress('https://sprint-boot.example.com/spaces/SB/board')).toBeNull();
  });
});

describe('TicketMention', () => {
  let editor: Editor | undefined;
  afterEach(() => editor?.destroy());

  const create = (resolve: (url: string) => Promise<TicketRef | null>, content = '<p></p>') =>
    new Editor({
      element: document.createElement('div'),
      extensions: [StarterKit, TicketMention.configure({ appUrl: () => APP, resolve })],
      content,
    });

  const pasteLink = (target: Editor, url: string) =>
    target.commands.insertContent({ type: 'text', text: url, marks: [{ type: 'link', attrs: { href: url } }] });

  it('turns a pasted ticket address into a chip once the ticket resolves', async () => {
    const resolve = vi.fn(async () => ({ key: 'SB-12', title: 'Login page', url: TICKET }));
    editor = create(resolve);

    pasteLink(editor, TICKET);

    await vi.waitFor(() => expect(editor!.getHTML()).toContain('data-type="ticket-mention"'));
    expect(editor.getHTML()).toContain('data-ticket-key="SB-12"');
    expect(editor.getHTML()).toContain('>SB-12 Login page</a>');
    expect(resolve).toHaveBeenCalledWith(TICKET);
  });

  it('keeps a plain link to a ticket the person cannot see', async () => {
    editor = create(async () => null);
    pasteLink(editor, TICKET);
    await new Promise((done) => setTimeout(done, 500));
    expect(editor.getHTML()).not.toContain('ticket-mention');
    expect(editor.getHTML()).toContain(`href="${TICKET}"`);
  });

  it('reads a saved chip back as a chip with its key and title apart', () => {
    editor = create(async () => null, `<p><a data-type="ticket-mention" class="ticket-mention" data-ticket-key="SB-12" href="${TICKET}">SB-12 Login page</a></p>`);
    const chip = editor.state.doc.firstChild!.firstChild!;
    expect(chip.type.name).toBe('ticketMention');
    expect(chip.attrs).toMatchObject({ ticketKey: 'SB-12', title: 'Login page', url: TICKET });
  });

  it('turns undo back into the pasted link', async () => {
    editor = create(async () => ({ key: 'SB-12', title: 'Login page', url: TICKET }));
    pasteLink(editor, TICKET);
    await vi.waitFor(() => expect(editor!.getHTML()).toContain('ticket-mention'));

    editor.commands.undo();

    await new Promise((done) => setTimeout(done, 500));
    expect(editor.getHTML()).not.toContain('ticket-mention');
    expect(editor.getHTML()).toContain(`href="${TICKET}"`);
  });
});
