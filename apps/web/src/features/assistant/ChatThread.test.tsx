import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const post = vi.fn();
const get = vi.fn();

vi.mock('../../api', () => ({
  apiClient: { POST: (...args: unknown[]) => post(...args), GET: (...args: unknown[]) => get(...args) },
  unwrap: async <T,>(call: Promise<T>) => call,
}));
vi.mock('../../auth/oidc', () => ({ getAccessToken: () => Promise.resolve('token') }));

const { ChatThread } = await import('./ChatThread');
const { AssistantButton, AssistantHost } = await import('./AssistantPanel');

// jsdom has no layout.
Element.prototype.scrollIntoView = () => undefined;

const sse = (events: object[]) =>
  new Response(events.map((event) => `data: ${JSON.stringify(event)}\n\n`).join(''), { headers: { 'content-type': 'text/event-stream' } });

/** As the panel uses it: the first question creates the conversation, and the thread keeps running. */
function Host() {
  const [id, setId] = useState<string | null>(null);
  return <ChatThread conversationId={id} onConversation={setId} />;
}

describe('ChatThread', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('starts a conversation on the first question and shows the streamed answer', async () => {
    post.mockResolvedValue({ id: 'c1' });
    get.mockResolvedValue({ conversation: { id: 'c1', title: 'Hi' }, messages: [] });
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(sse([
      { type: 'delta', text: 'Hel' },
      { type: 'delta', text: 'lo' },
      { type: 'message', message: { id: 'm1', role: 'assistant', text: 'Hello', html: '<p>Hello</p>', tools: [], actions: [], sources: [] } },
      { type: 'done' },
    ]))));

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <Host />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const box = screen.getByRole('textbox');
    fireEvent.change(box, { target: { value: 'Hi' } });
    await act(async () => {
      fireEvent.keyDown(box, { key: 'Enter' });
    });

    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/v1/assistant/conversations/c1/messages', expect.anything()));
    await waitFor(() => expect(get).toHaveBeenCalled());
  });

  it('keeps answering in the panel when the first question creates the conversation', async () => {
    post.mockResolvedValue({ id: 'c2' });
    get.mockImplementation((path: string) =>
      Promise.resolve(path === '/api/v1/assistant/status' ? { available: true } : path === '/api/v1/assistant' ? [] : { conversation: { id: 'c2', title: 'Hi' }, messages: [] }));
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(sse([
      { type: 'delta', text: 'Hello' },
      { type: 'message', message: { id: 'm1', role: 'assistant', text: 'Hello', html: '<p>Hello</p>', tools: [], actions: [], sources: [] } },
      { type: 'done' },
    ]))));

    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <AssistantHost>
            <AssistantButton />
          </AssistantHost>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole('button', { name: /assistant/i }));
    const box = await screen.findByRole('textbox');
    fireEvent.change(box, { target: { value: 'Hi' } });
    await act(async () => {
      fireEvent.keyDown(box, { key: 'Enter' });
    });

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(get).toHaveBeenCalledWith('/api/v1/assistant/{id}', expect.anything()));
  });
});
