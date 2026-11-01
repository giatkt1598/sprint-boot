import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { getAccessToken } from '../../auth/oidc';

export type Notification = components['schemas']['NotificationDto'];

export const NOTIFICATIONS_KEY = ['notifications'];

export const useUnreadCount = () =>
  useQuery({
    queryKey: [...NOTIFICATIONS_KEY, 'unread'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/notification/unread-count')),
  });

export const useNotifications = (skip: number, take: number, unreadOnly = false) =>
  useQuery({
    queryKey: [...NOTIFICATIONS_KEY, 'list', skip, take, unreadOnly],
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/notification', { params: { query: { SkipCount: skip, MaxResultCount: take, UnreadOnly: unreadOnly } } })),
  });

/** Refetches on a push and after every reconnect, so a dropped socket misses nothing. */
export function useNotificationLive() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const refresh = () => void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_KEY });
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', { accessTokenFactory: async () => (await getAccessToken()) ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    connection.on('NotificationsChanged', refresh);
    connection.onreconnected(refresh);
    void connection.start().catch(() => undefined);

    return () => {
      void connection.stop();
    };
  }, [queryClient]);
}
