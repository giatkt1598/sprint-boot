import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { SpacePermission } from './api';

const post = vi.fn();
const get = vi.fn();
const put = vi.fn();

vi.mock('../../api', () => ({
  apiClient: { POST: (...args: unknown[]) => post(...args), GET: (...args: unknown[]) => get(...args), PUT: (...args: unknown[]) => put(...args) },
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
  afterEach(cleanup);

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
    within(bugs).getByText('(Global)');
    expect(within(screen.getByRole('region', { name: 'Task' })).queryByText('Bug report')).toBeNull();

    fireEvent.click(within(bugs).getByRole('button', { name: 'Use Bug report' }));
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/issue-template', {
      params: { query: { projectId: 'p1' } },
      body: { issueTypeId: 'bug', globalTemplateId: 'g1', isDefault: true },
    }));
    // Used: it becomes the space's own, still marked Global, and is offered no more.
    await within(bugs).findByText('Used');
    within(bugs).getByText('(Global)');
    expect(within(bugs).queryByRole('button', { name: 'Use Bug report' })).toBeNull();
  });

  it('puts global templates first and makes another template the used one', async () => {
    get.mockImplementation((path: string) => Promise.resolve(
      path === '/api/v1/global-issue-template'
        ? [{ id: 'g1', issueTypeKey: 'task', name: 'Workspace task', content: '', mode: 0, usageCount: 0 }]
        : path === '/api/v1/issue-template'
          ? [{ id: 't1', projectId: 'p1', issueTypeId: 'task', name: 'Our task', content: '<p>Do</p>', mode: 1, isDefault: true }]
          : types,
    ));
    put.mockResolvedValue({});

    render(
      <QueryClientProvider client={new QueryClient()}>
        <SpaceIssueTemplatesPage />
      </QueryClientProvider>,
    );

    const tasks = await screen.findByRole('region', { name: 'Task' });
    await within(tasks).findByText('Our task');
    const names = within(tasks).getAllByText(/^(Workspace|Our) task/).map((item) => item.textContent);
    expect(names).toEqual(['Workspace task(Global)', 'Our task']);
    expect(within(tasks).queryByRole('button', { name: 'Use Our task' })).toBeNull();
    within(tasks).getByText('Used');

    fireEvent.click(within(tasks).getByRole('button', { name: 'Use Workspace task' }));
    await waitFor(() => expect(post).toHaveBeenLastCalledWith('/api/v1/issue-template', {
      params: { query: { projectId: 'p1' } },
      body: { issueTypeId: 'task', globalTemplateId: 'g1', isDefault: true },
    }));
  });
});
