import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

const { AssistantButton, AssistantHost, useViewingIssue } = await import('./AssistantPanel');

Element.prototype.scrollIntoView = () => undefined;

const conversation = (id: string, title: string, issueId: string | null = null) => ({
  id, title, issueId, issueKey: issueId ? 'SB-1' : null, createdAt: '2026-11-01T00:00:00Z', updatedAt: '2026-11-01T00:00:00Z',
});

function OnIssue() {
  useViewingIssue({ id: 'i1', key: 'SB-1' });
  return <AssistantButton />;
}

const renderHost = (child: React.ReactNode) =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <AssistantHost>{child}</AssistantHost>
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe('AssistantPanel', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('starts new, lists the issue conversations first, and asks about the issue on screen', async () => {
    get.mockImplementation((path: string) => Promise.resolve(
      path === '/api/v1/assistant/status'
        ? { available: true }
        : path === '/api/v1/assistant'
          ? [conversation('c1', 'Newest'), conversation('c2', 'Second'), conversation('c3', 'About the bug', 'i1'), conversation('c4', 'Older'), conversation('c5', 'Oldest')]
          : { conversation: conversation('c9', 'Hi', 'i1'), messages: [] },
    ));
    post.mockResolvedValue(conversation('c9', '', 'i1'));
    const fetch = vi.fn(() => Promise.resolve(new Response('data: {"type":"done"}\n\n', { headers: { 'content-type': 'text/event-stream' } })));
    vi.stubGlobal('fetch', fetch);

    renderHost(<OnIssue />);
    fireEvent.click(await screen.findByRole('button', { name: /assistant/i }));

    const recent = await screen.findByRole('region', { name: 'Recent conversations' });
    await within(recent).findByText('About the bug');
    expect(within(recent).getAllByRole('button').map((item) => item.textContent)).toEqual([
      'View all (2 more)',
      expect.stringContaining('About the bug'),
      expect.stringContaining('Newest'),
      expect.stringContaining('Second'),
    ]);

    fireEvent.click(within(recent).getByRole('button', { name: 'View all (2 more)' }));
    const history = await screen.findByRole('dialog', { name: 'History' });
    within(history).getByText('About this issue');
    fireEvent.change(within(history).getByRole('textbox', { name: 'Search conversations' }), { target: { value: 'old' } });
    expect(within(history).getAllByRole('button').map((item) => item.textContent)).toEqual([expect.stringMatching(/^Older[^s]/), expect.stringMatching(/^Oldest/)]);
    fireEvent.keyDown(history, { key: 'Escape' });

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Summarize this page' }));
    });
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/assistant', { body: { issueId: 'i1' } }));
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    const [, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ text: 'Summarize this page', issueId: 'i1' });
  });

  it('offers no issue suggestions away from an issue', async () => {
    get.mockImplementation((path: string) => Promise.resolve(path === '/api/v1/assistant/status' ? { available: true } : []));
    renderHost(<AssistantButton />);
    fireEvent.click(await screen.findByRole('button', { name: /assistant/i }));

    await screen.findByRole('textbox', { name: 'Ask the assistant' });
    expect(screen.queryByRole('button', { name: 'Summarize this page' })).toBeNull();
    expect(screen.queryByRole('region', { name: 'Recent conversations' })).toBeNull();
  });
});
