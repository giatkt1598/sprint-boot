import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';

vi.mock('../../auth/AuthProvider', () => ({ useCurrentUser: () => ({ id: 'me' }) }));
vi.mock('../../api', () => ({ apiClient: {}, unwrap: async <T,>(call: Promise<T>) => call }));

const { ReactionUsers } = await import('./IssueActivityPanel');

describe('ReactionUsers', () => {
  it('lists who reacted, one person per line, calling the viewer You', () => {
    render(
      <ReactionUsers
        meId="me"
        reaction={{
          emoji: '👍',
          count: 3,
          reactedByMe: true,
          users: [
            { id: 'u1', name: 'Lan Nguyen', email: 'lan@example.com' },
            { id: 'me', name: 'Gia Tran', email: 'gia@example.com' },
            { id: 'u2', name: 'Minh Le', email: 'minh@example.com' },
          ],
        }}
      />,
    );

    screen.getByText('3 reacted with 👍');
    const lines = within(screen.getByRole('list')).getAllByRole('listitem');
    expect(lines).toHaveLength(3);
    ['Lan Nguyen', 'You', 'Minh Le'].forEach((name, index) => expect(lines[index].textContent).toContain(name));
    expect(lines[1].textContent).not.toContain('Gia Tran');
  });
});
