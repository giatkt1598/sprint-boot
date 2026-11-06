import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const post = vi.fn();

vi.mock('../../api', () => ({
  apiClient: { POST: (...args: unknown[]) => post(...args) },
  unwrap: async <T>(call: Promise<T>) => call,
}));

const { uploadFile } = await import('./upload');

const session = (urls: (string | null)[]) => ({
  id: 's1',
  partSize: 4,
  expiresAt: '2030-01-01T00:00:00Z',
  parts: urls.map((url, index) => ({ number: index + 1, url })),
});

describe('uploadFile', () => {
  beforeEach(() => {
    post.mockReset();
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('puts each part to its presigned URL, renews expired URLs and completes with the ETags', async () => {
    const calls: string[] = [];
    post.mockImplementation((path: string, options: { body?: unknown }) => {
      calls.push(path);
      if (path === '/api/v1/upload/start') {
        return Promise.resolve(session(['https://s3/p1?old', 'https://s3/p2?old']));
      }
      if (path === '/api/v1/upload/{id}/renew') {
        return Promise.resolve(session(['https://s3/p1?new', 'https://s3/p2?new']));
      }
      if (path === '/api/v1/upload/{id}/complete') {
        return Promise.resolve({ attachment: { id: 'a1', body: options.body } });
      }
      return Promise.reject(new Error(`unexpected ${path}`));
    });
    vi.stubGlobal('fetch', vi.fn((url: string) =>
      Promise.resolve(url.endsWith('old')
        ? new Response(null, { status: 403 })
        : new Response(null, { status: 200, headers: { ETag: `"${url.split('/')[3]!.split('?')[0]}"` } })),
    ));
    const progress: number[] = [];

    const result = await uploadFile({ purpose: 0, issueId: 'i1' }, new File(['abcdefg'], 'a.txt'), (loaded) => progress.push(loaded));

    expect(calls.filter((path) => path.endsWith('/renew'))).toHaveLength(1);
    expect((result.attachment as unknown as { body: { parts: unknown[] } }).body.parts).toEqual(
      expect.arrayContaining([{ number: 1, eTag: '"p1"' }, { number: 2, eTag: '"p2"' }]),
    );
    expect(progress.at(-1)).toBe(7);
  });

  it('gives up after repeated failures and cancels the upload', async () => {
    post.mockImplementation((path: string) =>
      path === '/api/v1/upload/start' ? Promise.resolve(session(['https://s3/p1'])) : Promise.resolve({}),
    );
    const fetch = vi.fn(() => Promise.resolve(new Response(null, { status: 500 })));
    vi.stubGlobal('fetch', fetch);

    await expect(uploadFile({ purpose: 1, projectId: 'p1' }, new File(['abc'], 'a.txt'))).rejects.toThrow();
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(post).toHaveBeenCalledWith('/api/v1/upload/{id}/abort', expect.anything());
  });
});
