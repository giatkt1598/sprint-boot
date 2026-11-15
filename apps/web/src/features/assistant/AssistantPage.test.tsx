import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';

vi.mock('../../api', () => ({ apiClient: {}, unwrap: async <T,>(call: Promise<T>) => call }));
vi.mock('./ChatThread', () => ({ ChatThread: () => null }));
vi.mock('./api', () => ({
  assistantKeys: { list: () => ['assistant'] },
  useAssistantStatus: () => ({ data: { available: true } }),
  useConversations: () => ({
    data: [{ id: 'c1', title: 'Plan the sprint', ticketKey: null, updatedAt: '2026-11-01T00:00:00Z' }],
    isPending: false,
    isError: false,
  }),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const { AssistantPage } = await import('./AssistantPage');

describe('AssistantPage', () => {
  it('renames and deletes a conversation from its "more" menu', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <AssistantPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.queryByRole('button', { name: /^Rename|^Delete/ })).toBeNull();
    const more = screen.getByRole('button', { name: 'Actions for Plan the sprint' });
    expect(more.getAttribute('aria-haspopup')).toBe('menu');

    fireEvent.click(more);
    expect(more.getAttribute('aria-expanded')).toBe('true');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Edit' }));
    expect(screen.getByRole('dialog', { name: 'Rename conversation' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    fireEvent.click(more);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    expect(screen.getByRole('dialog', { name: 'Delete Plan the sprint?' })).toBeTruthy();
  });

  it('lets the conversation list be resized', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <AssistantPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getAllByRole('separator', { name: 'Resize conversation list' }).length).toBeGreaterThan(0);
  });
});
