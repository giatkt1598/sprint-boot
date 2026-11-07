import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';

const post = vi.fn();
const get = vi.fn();

vi.mock('../../api', () => ({
  apiClient: { POST: (...args: unknown[]) => post(...args), GET: (...args: unknown[]) => get(...args) },
  unwrap: async <T,>(call: Promise<T>) => call,
}));

// jsdom has no layout.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const { AssistantSettingsPage } = await import('./AssistantSettingsPage');

describe('AssistantSettingsPage', () => {
  it('loads models and tests the settings as typed, with the stored key when the field is blank', async () => {
    get.mockResolvedValue({
      enabled: true,
      connections: [{ id: 'c1', name: 'Local', provider: 0, baseUrl: 'http://localhost:11434/v1', hasApiKey: true, model: 'old', isDefault: true }],
    });
    post.mockImplementation((path: string) => Promise.resolve(path.endsWith('/list-models')
      ? [{ id: 'llama3.3' }, { id: 'qwen3', name: 'Qwen 3' }]
      : { succeeded: true, message: 'Hello!' }));

    render(
      <QueryClientProvider client={new QueryClient()}>
        <AssistantSettingsPage />
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Edit Local' }));
    fireEvent.click(screen.getByRole('button', { name: 'Load models' }));
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/assistant-admin/list-models', {
      body: { provider: 0, baseUrl: 'http://localhost:11434/v1', apiKey: null, connectionId: 'c1' },
    }));

    const model = screen.getByRole('combobox', { name: 'Model' });
    model.focus();
    fireEvent.keyDown(model, { key: 'ArrowDown' });
    expect(await screen.findByText('llama3.3')).toBeTruthy();
    fireEvent.change(model, { target: { value: 'qwen' } });
    await waitFor(() => expect(screen.queryByText('llama3.3')).toBeNull());
    fireEvent.click(screen.getByText('qwen3'));
    expect(model).toHaveProperty('value', 'qwen3');

    fireEvent.click(screen.getByRole('button', { name: 'Test connection' }));
    expect(await screen.findByRole('status')).toHaveProperty('textContent', 'The connection works: Hello!');
    expect(post).toHaveBeenLastCalledWith('/api/v1/assistant-admin/test-settings', {
      body: { provider: 0, baseUrl: 'http://localhost:11434/v1', apiKey: null, connectionId: 'c1', model: 'qwen3', effort: null },
    });
  });
});
