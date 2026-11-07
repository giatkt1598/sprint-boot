import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { SpacePermission } from './api';

const post = vi.fn();
const get = vi.fn();

vi.mock('../../api', () => ({
  apiClient: { POST: (...args: unknown[]) => post(...args), GET: (...args: unknown[]) => get(...args) },
  unwrap: async <T,>(call: Promise<T>) => call,
}));
vi.mock('react-router', async (original) => ({
  ...(await original<typeof import('react-router')>()),
  useOutletContext: () => ({ id: 'p1', permissions: [SpacePermission.manage] }),
}));

const { SpaceIssueTemplatesPage } = await import('./IssueTemplatesPage');

const types = [
  { id: 'bug', name: 'Bug', icon: 'bug', hierarchy: 1 },
  { id: 'task', name: 'Task', icon: 'task', hierarchy: 1 },
];

describe('SpaceIssueTemplatesPage', () => {
  it('lists workspace templates under their issue type, marked Global, and uses one', async () => {
    let own: object[] = [];
    get.mockImplementation((path: string) => Promise.resolve(
      path === '/api/v1/global-issue-template'
        ? [{ id: 'g1', issueTypeKey: 'bug', name: 'Bug report', content: '<p>Steps</p>', mode: 0, usageCount: 0 }]
        : path === '/api/v1/issue-template' ? own : types,
    ));
    post.mockImplementation(() => {
      own = [{ id: 't1', projectId: 'p1', issueTypeId: 'bug', name: 'Bug report', content: '<p>Steps</p>', mode: 0, globalTemplateId: 'g1', isDefault: true }];
      return Promise.resolve(own[0]);
    });

    render(
      <QueryClientProvider client={new QueryClient()}>
        <SpaceIssueTemplatesPage />
      </QueryClientProvider>,
    );

    const bugs = await screen.findByRole('region', { name: 'Bug' });
    within(bugs).getByText('Global');
    expect(within(screen.getByRole('region', { name: 'Task' })).queryByText('Bug report')).toBeNull();

    fireEvent.click(within(bugs).getByRole('button', { name: 'Use template Bug report' }));
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/issue-template', {
      params: { query: { projectId: 'p1' } },
      body: { issueTypeId: 'bug', globalTemplateId: 'g1', isDefault: true },
    }));
    // Used: it becomes the space's own, still marked Global, and is offered no more.
    await within(bugs).findByText('Default');
    within(bugs).getByText('Global');
    expect(within(bugs).queryByRole('button', { name: 'Use template Bug report' })).toBeNull();
  });
});
