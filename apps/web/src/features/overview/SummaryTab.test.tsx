import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { OverviewSummary } from './api';

let summary: Partial<OverviewSummary>;

vi.mock('./api', () => ({
  useOverviewSummary: () => ({ data: summary, isPending: false, isError: false, refetch: () => undefined }),
}));
vi.mock('../tickets/api', () => ({
  useStatuses: () => ({ data: [] }),
  useTicketTypes: () => ({ data: [] }),
}));

const { SummaryTab } = await import('./SummaryTab');

const base: Partial<OverviewSummary> = {
  range: { from: '2026-11-01', to: '2026-11-30' } as OverviewSummary['range'],
  members: 3,
  membersAddedInPeriod: 0,
  tickets: { total: 4, toDo: 2, inProgress: 1, done: 1, createdInPeriod: 4, inProgressByStatus: [] } as unknown as OverviewSummary['tickets'],
  activity: { created: 0, updated: 0, completed: 0 } as OverviewSummary['activity'],
  pullRequests: { total: 0, open: 0, merged: 0 } as OverviewSummary['pullRequests'],
  byType: [],
  velocity: [],
  velocitySprints: 0,
  recent: [],
};

const space = { id: 'p1', key: 'SB', name: 'Sprint Boot' } as Parameters<typeof SummaryTab>[0]['space'];

const renderTab = () => render(<MemoryRouter><SummaryTab space={space} /></MemoryRouter>);

describe('SummaryTab', () => {
  afterEach(cleanup);

  it('leaves the AI Assistant section out while the assistant is off', () => {
    summary = { ...base, assistant: undefined };
    renderTab();
    expect(screen.queryByRole('region', { name: 'AI Assistant' })).toBeNull();
  });

  it('shows the assistant counts while it is on', () => {
    summary = {
      ...base,
      assistant: {
        conversations: 2,
        messages: 7,
        users: 1,
        actions: [{ tool: 'create_ticket', count: 3 }, { tool: 'transition_ticket', count: 1 }],
        confirmed: 3,
        declined: 1,
        failed: 0,
        topUsers: [{ user: { id: 'u1', name: 'Lan Pham' }, messages: 4 }],
        ticketsCreated: 2,
        ticketsCreatedInPeriod: 4,
      } as OverviewSummary['assistant'],
    };
    renderTab();
    const section = screen.getByRole('region', { name: 'AI Assistant' });
    expect(section.textContent).toContain('7 messages');
    expect(section.textContent).toContain('75% confirmed');
    expect(section.textContent).toContain('50% of 4 tickets created');
    expect(section.textContent).toContain('Create ticket');
    expect(section.textContent).toContain('Change status');
    expect(section.textContent).toContain('3 confirmed · 1 declined · 0 failed');
    expect(section.textContent).toContain('Lan Pham');
    expect(section.textContent).toContain('4 questions');
  });
});
