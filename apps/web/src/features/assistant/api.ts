import { useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { getAccessToken } from '../../auth/oidc';

export type Conversation = components['schemas']['ConversationDto'];
export type AssistantMessage = components['schemas']['AssistantMessageDto'];
export type AssistantAction = components['schemas']['AssistantActionDto'];
export type AiConnection = components['schemas']['AiConnectionDto'];

/** One server-sent event while the assistant answers; mirrors AssistantEventDto. */
export interface AssistantEvent {
  type: 'delta' | 'tool' | 'action' | 'message' | 'error' | 'done';
  text?: string | null;
  tool?: string | null;
  state?: 'running' | 'done' | 'failed' | null;
  action?: AssistantAction | null;
  message?: AssistantMessage | null;
  code?: string | null;
}

/** As the API numbers AssistantActionState. */
export const ActionState = { proposed: 0, confirmed: 1, declined: 2, failed: 3 } as const;

/** As the API numbers AiProvider. */
export const Provider = { openAiCompatible: 0, anthropic: 1 } as const;

export const assistantKeys = {
  status: ['assistant', 'status'],
  list: (issueId?: string) => ['assistant', 'conversations', issueId ?? 'all'],
  detail: (id: string) => ['assistant', 'conversation', id],
};

export const useAssistantStatus = () =>
  useQuery({
    queryKey: assistantKeys.status,
    staleTime: 60_000,
    queryFn: () => unwrap(apiClient.GET('/api/v1/assistant/status')),
  });

export const useConversations = (issueId?: string) =>
  useQuery({
    queryKey: assistantKeys.list(issueId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/assistant', { params: { query: { issueId } } })),
  });

export const conversationQuery = (id: string) => ({
  queryKey: assistantKeys.detail(id),
  queryFn: () => unwrap(apiClient.GET('/api/v1/assistant/{id}', { params: { path: { id } } })),
});

export const useConversation = (id: string | null) =>
  useQuery({ ...conversationQuery(id ?? ''), enabled: Boolean(id) });

export const createConversation = (issueId?: string) =>
  unwrap(apiClient.POST('/api/v1/assistant', { body: { issueId: issueId ?? null } }));

/** Splits a server-sent event stream into its JSON events; returns what is left of a partial event. */
export function parseEvents(buffer: string, onEvent: (event: AssistantEvent) => void): string {
  const chunks = buffer.split('\n\n');
  const rest = chunks.pop() ?? '';
  for (const chunk of chunks) {
    const data = chunk
      .split('\n')
      .filter((line) => line.startsWith('data:'))
      .map((line) => line.slice(5).trimStart())
      .join('\n');
    if (data) {
      onEvent(JSON.parse(data) as AssistantEvent);
    }
  }
  return rest;
}

/** The answer streams as server-sent events; openapi-fetch cannot read those, so this is a plain fetch with the token. */
export async function streamAssistant(path: string, body: unknown, onEvent: (event: AssistantEvent) => void, signal: AbortSignal) {
  const token = await getAccessToken();
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  if (!response.ok || !response.headers.get('content-type')?.includes('text/event-stream')) {
    // Errors before the first event come back as ordinary JSON errors.
    const error = (await response.json().catch(() => null)) as { error?: { code?: string; message?: string } } | null;
    onEvent({ type: 'error', code: error?.error?.code ?? null, text: error?.error?.message ?? response.statusText });
    return;
  }

  const reader = response.body!.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }
    buffer = parseEvents(buffer + value, onEvent);
  }
  parseEvents(`${buffer}\n\n`, onEvent);
}
