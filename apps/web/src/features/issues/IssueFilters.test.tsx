import { act, cleanup, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { readRange, useIssueFilters, useSearchText, writeRange } from './IssueFilters';

describe('date filters in the address', () => {
  it('read and write presets and custom ranges, and drop anything else', () => {
    expect(readRange('last7')).toEqual({ preset: 'last7', from: '', to: '' });
    expect(readRange('2026-11-01~2026-11-07')).toEqual({ preset: 'custom', from: '2026-11-01', to: '2026-11-07' });
    expect(readRange('yesterday')).toEqual({ preset: 'any', from: '', to: '' });
    expect(readRange(null).preset).toBe('any');
    expect(writeRange({ preset: 'custom', from: '2026-11-01', to: '' })).toBe('2026-11-01~');
    expect(writeRange({ preset: 'any', from: '', to: '' })).toBeNull();
  });
});

describe('issue filters', () => {
  afterEach(cleanup);

  /** Renders the hooks on a page of a memory router, so the address can be read back. */
  const mount = (url: string) => {
    let location = '';
    const client = new QueryClient({ defaultOptions: { queries: { enabled: false } } });
    const Probe = ({ children }: { children: ReactNode }) => {
      const current = useLocation();
      location = current.search;
      return children;
    };
    const wrapper = ({ children }: { children: ReactNode }) => {
      const router = createMemoryRouter([{ path: '*', element: <Probe>{children}</Probe> }], { initialEntries: [url] });
      return (
        <QueryClientProvider client={client}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      );
    };
    const hook = renderHook(() => ({ filters: useIssueFilters(undefined), search: useSearchText() }), { wrapper });
    return { hook, location: () => location };
  };

  it('start from the address, so coming back to a list keeps its filters', () => {
    const { hook } = mount('/spaces/DOC/backlog?assignee=u1&label=ui&created=last30&text=login');
    expect(hook.result.current.filters.values.assignee).toEqual(['u1']);
    expect(hook.result.current.filters.values.label).toEqual(['ui']);
    expect(hook.result.current.filters.created.preset).toBe('last30');
    expect(hook.result.current.search[0]).toBe('login');
  });

  it('write changes back to the address and leave other parameters alone', () => {
    const { hook, location } = mount('/spaces/DOC/issues?sort=key+asc');
    act(() => hook.result.current.filters.toggle('type', 't1'));
    act(() => hook.result.current.search[1]('bug'));
    const params = new URLSearchParams(location());
    expect(params.getAll('type')).toEqual(['t1']);
    expect(params.get('text')).toBe('bug');
    expect(params.get('sort')).toBe('key asc');

    act(() => hook.result.current.filters.clearAll());
    expect(new URLSearchParams(location()).has('type')).toBe(false);
    expect(new URLSearchParams(location()).get('sort')).toBe('key asc');
  });
});
