import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { getAccessToken } from '../../auth/oidc';

export type Card = components['schemas']['IssueCardDto'];
export type BoardView = components['schemas']['BoardViewDto'];
export type Backlog = components['schemas']['BacklogDto'];
export type SprintSummary = components['schemas']['SprintSummaryDto'];

export const boardKey = (projectId?: string) => ['issues', 'board', projectId];
export const backlogKey = (projectId?: string) => ['issues', 'backlog', projectId];

export const useBoard = (projectId: string | undefined) =>
  useQuery({
    queryKey: boardKey(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/board/view/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

export const useBacklog = (projectId: string | undefined) =>
  useQuery({
    queryKey: backlogKey(projectId),
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/sprint/backlog/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

/** Refetches on server signals; data always comes from the API, never from the socket. */
export function useBoardLive(projectId: string | undefined) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!projectId) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/board', { accessTokenFactory: async () => (await getAccessToken()) ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    const refresh = () => {
      void queryClient.invalidateQueries({ queryKey: boardKey(projectId) });
      void queryClient.invalidateQueries({ queryKey: backlogKey(projectId) });
    };
    const join = () => connection.invoke('Join', projectId);

    connection.on('BoardChanged', refresh);
    connection.onreconnected(() => {
      void join();
      refresh();
    });
    let stopped = false;
    void connection
      .start()
      .then(() => (stopped ? undefined : join()))
      .catch(() => undefined);

    return () => {
      stopped = true;
      void connection.stop();
    };
  }, [projectId, queryClient]);
}
