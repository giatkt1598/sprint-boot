import Badge from '@mui/material/Badge';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Bell } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { Popover } from '../../components/Popover';
import { formatRelative } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { NOTIFICATIONS_KEY, useNotificationLive, useNotifications, useUnreadCount, type Notification } from './api';

const PREVIEW_SIZE = 6;

export const eventKey = (type?: string | null) => (type ?? '').replace(/[.-]/g, '_');

export function NotificationItem({ item, onOpen }: { item: Notification; onOpen: (item: Notification) => void }) {
  const { t, i18n } = useTranslation();
  const unread = !item.readAt;

  return (
    <ButtonBase
      onClick={() => onOpen(item)}
      className="flex w-full items-start gap-2 p-2 text-left"
      sx={{
        justifyContent: 'flex-start',
        borderRadius: `${radius.card}px`,
        backgroundColor: unread ? sem('bg-selected') : 'transparent',
        '&:hover': { backgroundColor: sem('bg-hover') },
      }}
    >
      <Avatar name={item.actorName ?? ''} />
      <span className="flex min-w-0 flex-1 flex-col">
        <span style={{ fontSize: font.size.md }}>
          {t(`notifications.event.${eventKey(item.eventType)}`, { actor: item.actorName, key: item.issueKey, name: item.title, status: item.detail })}
        </span>
        {item.title && <span className="truncate" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{item.title}</span>}
        <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{formatRelative(item.createdAt, i18n.language)}</span>
      </span>
    </ButtonBase>
  );
}

export function useOpenNotification() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const read = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/notification/{id}/mark-read', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_KEY }),
  });

  return (item: Notification) => {
    if (!item.readAt) {
      read.mutate(item.id!);
    }
    if (item.issueKey) {
      void navigate(`/issues/${item.issueKey}`);
    }
  };
}

export function useMarkAllRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/notification/mark-all-read')),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_KEY }),
  });
}

export function NotificationBell() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  useNotificationLive();
  const unread = useUnreadCount();
  const list = useNotifications(0, PREVIEW_SIZE);
  const markAll = useMarkAllRead();
  const open = useOpenNotification();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const count = unread.data ?? 0;

  return (
    <>
      <IconButton
        aria-label={count ? t('notifications.bellUnread', { count }) : t('topBar.notifications')}
        size="small"
        sx={{ color: sem('text-secondary') }}
        onClick={(event) => setAnchor(event.currentTarget)}
      >
        {count > 0 ? (
          <Badge color="error" badgeContent={count} max={99}>
            <Bell size={20} strokeWidth={1.5} aria-hidden />
          </Badge>
        ) : (
          <Bell size={20} strokeWidth={1.5} aria-hidden />
        )}
      </IconButton>
      <Popover width={508} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <div className="flex items-center px-2 pb-2">
          <Typography variant="h3" component="h2" className="flex-1">{t('topBar.notifications')}</Typography>
          {count > 0 && <Button kind="link" onClick={() => markAll.mutate()}>{t('notifications.markAll')}</Button>}
        </div>
        <div className="flex flex-col gap-1">
          {(list.data?.items ?? []).map((item) => (
            <NotificationItem key={item.id} item={item} onOpen={(row) => { setAnchor(null); open(row); }} />
          ))}
          {list.data?.totalCount === 0 && <Typography color="textSecondary" className="p-2">{t('notifications.empty')}</Typography>}
        </div>
        <Button kind="link" onClick={() => { setAnchor(null); void navigate('/notifications'); }}>{t('notifications.seeAll')}</Button>
      </Popover>
    </>
  );
}
