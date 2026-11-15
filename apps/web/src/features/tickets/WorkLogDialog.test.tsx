import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';

vi.mock('../../auth/AuthProvider', () => ({ useCurrentUser: () => ({ id: 'me' }) }));
vi.mock('../../api', () => ({ apiClient: {}, unwrap: async <T,>(call: Promise<T>) => call }));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const { WorkLogDialog } = await import('./TicketActivityPanel');

describe('WorkLogDialog', () => {
  it('sets the time from a quick chip and steps it with the arrow keys', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <WorkLogDialog ticketId="t1" onClose={() => undefined} />
      </QueryClientProvider>,
    );
    const input = screen.getByLabelText('Time spent') as HTMLInputElement;

    fireEvent.click(screen.getByRole('button', { name: '1h' }));
    expect(input.value).toBe('1h');
    expect(screen.getByRole('button', { name: '1h' }).getAttribute('aria-pressed')).toBe('true');

    fireEvent.keyDown(input, { key: 'ArrowUp' });
    expect(input.value).toBe('1h 30m');
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    expect(input.value).toBe('2h');
    expect(screen.getByRole('button', { name: '2h' }).getAttribute('aria-pressed')).toBe('true');
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(input.value).toBe('1h');
  });
});
