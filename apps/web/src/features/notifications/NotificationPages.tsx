import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox, Toggle } from '../../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { Pagination } from '../../components/Pagination';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { NotificationItem, eventKey, useMarkAllRead, useOpenNotification } from './NotificationBell';
import { useNotifications } from './api';

type Scheme = components['schemas']['NotificationSchemeDto'];
type Rule = components['schemas']['NotificationRuleDto'];

const PAGE_SIZE = 25;

const EVENTS = [
  'issue.created', 'issue.assigned', 'issue.updated', 'comment.added', 'mentioned',
  'issue.transitioned', 'workflow.notification', 'sprint.started', 'sprint.completed', 'project.member-added',
];

const RECIPIENTS = [
  { type: 0, label: 'notifications.recipient.assignee' },
  { type: 1, label: 'notifications.recipient.reporter' },
  { type: 2, label: 'notifications.recipient.watcher' },
];

const ROLES = [
  { value: 'ProjectAdmin', label: 'spaces.roleProjectAdmin' },
  { value: 'Member', label: 'spaces.roleMember' },
  { value: 'Viewer', label: 'spaces.roleViewer' },
];

export function NotificationsPage() {
  const { t } = useTranslation();
  const [skip, setSkip] = useState(0);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const list = useNotifications(skip, PAGE_SIZE, unreadOnly);
  const markAll = useMarkAllRead();
  const open = useOpenNotification();

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t('topBar.notifications')}</Typography>
        <Toggle label={t('notifications.unreadOnly')} checked={unreadOnly} onChange={(event) => { setUnreadOnly(event.target.checked); setSkip(0); }} />
        <Button kind="secondary" onClick={() => markAll.mutate()}>{t('notifications.markAll')}</Button>
      </div>
      {list.isPending && <LoadingState />}
      {list.isError && <ErrorState onRetry={() => void list.refetch()} />}
      {list.data?.totalCount === 0 && <EmptyState title={t('notifications.empty')} />}
      <div className="flex flex-col gap-1">
        {(list.data?.items ?? []).map((item) => <NotificationItem key={item.id} item={item} onOpen={open} />)}
      </div>
      {(list.data?.totalCount ?? 0) > 0 && <Pagination skip={skip} pageSize={PAGE_SIZE} total={list.data?.totalCount ?? 0} onChange={setSkip} />}
    </div>
  );
}

export function NotificationPreferencesPage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();

  const preferences = useQuery({
    queryKey: ['notifications', 'preferences'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/notification/preferences')),
  });

  const save = useMutation({
    mutationFn: (item: { eventType: string; enabled: boolean }) =>
      unwrap(apiClient.POST('/api/v1/notification/save-preferences', { body: { items: [item] } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications', 'preferences'] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <div className="flex max-w-md flex-col gap-4">
      <div>
        <Typography variant="h1">{t('topBar.notifications')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('notifications.preferencesHint')}</Typography>
      </div>
      {preferences.isPending && <LoadingState />}
      {preferences.isError && <ErrorState onRetry={() => void preferences.refetch()} />}
      {(preferences.data ?? []).map((item) => (
        <Toggle
          key={item.eventType}
          label={t(`notifications.type.${eventKey(item.eventType)}`)}
          checked={item.enabled ?? true}
          onChange={(event) => save.mutate({ eventType: item.eventType!, enabled: event.target.checked })}
        />
      ))}
    </div>
  );
}

export function NotificationSchemesPage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');

  const schemes = useQuery({
    queryKey: ['admin', 'notification-schemes'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/notification-scheme')),
  });

  const create = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/notification-scheme', { body: { name, rules: [] } })),
    onSuccess: async (scheme) => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'notification-schemes'] });
      await navigate(scheme.id!);
    },
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center">
        <div className="flex-1">
          <Typography variant="h1">{t('notifications.schemes')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('notifications.schemesHint')}</Typography>
        </div>
        <Button onClick={() => setCreating(true)}>{t('notifications.createScheme')}</Button>
      </div>
      {schemes.isPending && <LoadingState />}
      {schemes.isError && <ErrorState onRetry={() => void schemes.refetch()} />}
      {schemes.data && (
        <Table<Scheme>
          label={t('notifications.schemes')}
          rows={schemes.data}
          rowKey={(scheme) => scheme.id!}
          columns={[
            {
              key: 'name',
              header: t('account.name'),
              render: (scheme) => (
                <span className="flex items-center gap-2">
                  <Link component={RouterLink} to={scheme.id!} color="inherit">{scheme.name}</Link>
                  {scheme.isDefault && <Badge tone="success">{t('notifications.default')}</Badge>}
                </span>
              ),
            },
            { key: 'spaces', header: t('spaces.title'), render: (scheme) => scheme.spaceCount },
            { key: 'rules', header: t('notifications.rules'), render: (scheme) => scheme.rules?.length },
          ]}
        />
      )}
      {creating && (
        <Dialog
          open
          title={t('notifications.createScheme')}
          onClose={() => setCreating(false)}
          footer={<DialogFooter confirmLabel={t('notifications.createScheme')} confirmDisabled={!name.trim() || create.isPending} onCancel={() => setCreating(false)} onConfirm={() => create.mutate()} />}
        >
          <TextField label={t('account.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
          {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
        </Dialog>
      )}
    </div>
  );
}

function RuleDialog({ eventType, rules, onSave, onClose }: { eventType: string; rules: Rule[]; onSave: (next: Rule[]) => void; onClose: () => void }) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState<number[]>(rules.filter((rule) => rule.recipientType <= 2).map((rule) => rule.recipientType));
  const [roles, setRoles] = useState<string[]>(rules.filter((rule) => rule.recipientType === 4).map((rule) => rule.recipientValue ?? ''));

  const toggle = <T,>(list: T[], value: T) => (list.includes(value) ? list.filter((item) => item !== value) : [...list, value]);

  return (
    <Dialog
      open
      title={t(`notifications.type.${eventKey(eventType)}`)}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('common.save')}
          onCancel={onClose}
          onConfirm={() =>
            onSave([
              ...selected.map((recipientType) => ({ eventType, recipientType: recipientType as Rule['recipientType'], recipientValue: null })),
              ...roles.map((recipientValue) => ({ eventType, recipientType: 4 as Rule['recipientType'], recipientValue })),
            ])
          }
        />
      }
    >
      <Typography variant="body2" color="textSecondary">{t('notifications.recipients')}</Typography>
      {RECIPIENTS.map((item) => (
        <Checkbox key={item.type} label={t(item.label)} checked={selected.includes(item.type)} onChange={() => setSelected(toggle(selected, item.type))} />
      ))}
      <Typography variant="body2" color="textSecondary">{t('notifications.spaceRole')}</Typography>
      {ROLES.map((item) => (
        <Checkbox key={item.value} label={t(item.label)} checked={roles.includes(item.value)} onChange={() => setRoles(toggle(roles, item.value))} />
      ))}
    </Dialog>
  );
}

export function EditSchemePage() {
  const { t } = useTranslation();
  const { schemeId } = useParams();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [deleting, setDeleting] = useState(false);

  const scheme = useQuery({
    queryKey: ['admin', 'notification-schemes', schemeId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/notification-scheme/{id}', { params: { path: { id: schemeId! } } })),
  });

  useEffect(() => {
    if (scheme.data) {
      setName(scheme.data.name ?? '');
    }
  }, [scheme.data]);

  const save = useMutation({
    mutationFn: (rules: Rule[]) =>
      unwrap(apiClient.PUT('/api/v1/notification-scheme/{id}', { params: { path: { id: schemeId! } }, body: { name, rules } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'notification-schemes'] });
      setEditing(null);
      toast('success', t('account.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/notification-scheme/{id}', { params: { path: { id: schemeId! } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'notification-schemes'] });
      await navigate('..');
    },
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setDeleting(false),
  });

  if (scheme.isPending) {
    return <LoadingState />;
  }
  if (scheme.isError) {
    return <ErrorState onRetry={() => void scheme.refetch()} />;
  }

  const rules = scheme.data.rules ?? [];
  const recipientsOf = (eventType: string) => {
    const own = rules.filter((rule) => rule.eventType === eventType);
    const names = own.map((rule) =>
      rule.recipientType === 4
        ? t(ROLES.find((role) => role.value === rule.recipientValue)?.label ?? 'spaces.roleMember')
        : t(RECIPIENTS.find((item) => item.type === rule.recipientType)?.label ?? ''),
    );
    if (eventType === 'mentioned') {
      names.push(t('notifications.alwaysMentioned'));
    }
    if (eventType === 'project.member-added') {
      names.push(t('notifications.alwaysAdded'));
    }
    return names.join(', ') || t('common.none');
  };

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-end gap-2">
        <div className="flex-1"><TextField label={t('account.name')} value={name} onChange={(event) => setName(event.target.value)} /></div>
        {scheme.data.isDefault && <Badge tone="success">{t('notifications.default')}</Badge>}
        <Button kind="secondary" disabled={!name.trim() || name === scheme.data.name} onClick={() => save.mutate(rules)}>{t('common.save')}</Button>
        {!scheme.data.isDefault && <Button kind="danger" onClick={() => setDeleting(true)}>{t('common.delete')}</Button>}
      </div>
      <Table<string>
        label={t('notifications.rules')}
        rows={EVENTS}
        rowKey={(eventType) => eventType}
        columns={[
          { key: 'event', header: t('notifications.event_column'), render: (eventType) => t(`notifications.type.${eventKey(eventType)}`) },
          { key: 'recipients', header: t('notifications.recipients'), render: recipientsOf },
          {
            key: 'edit',
            header: '',
            render: (eventType) => (
              <Button kind="link" aria-label={`${t('common.edit')} ${t(`notifications.type.${eventKey(eventType)}`)}`} onClick={() => setEditing(eventType)}>{t('common.edit')}</Button>
            ),
          },
        ]}
      />
      {editing && (
        <RuleDialog
          eventType={editing}
          rules={rules.filter((rule) => rule.eventType === editing)}
          onClose={() => setEditing(null)}
          onSave={(next) => save.mutate([...rules.filter((rule) => rule.eventType !== editing), ...next])}
        />
      )}
      <DialogConfirm
        open={deleting}
        title={t('notifications.deleteTitle')}
        description={t('notifications.deleteBody', { name: scheme.data.name })}
        confirmLabel={t('common.delete')}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}

export function SpaceSchemeField({ projectId, disabled }: { projectId: string; disabled: boolean }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();

  const current = useQuery({
    queryKey: ['spaces', projectId, 'scheme'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/notification-scheme/for-project/{projectId}', { params: { path: { projectId } } })),
  });
  const choices = useQuery({
    queryKey: ['notification-scheme-choices'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/notification-scheme/choices')),
  });

  const set = useMutation({
    mutationFn: (schemeId: string) => unwrap(apiClient.POST('/api/v1/notification-scheme/set-for-project', { body: { projectId, schemeId } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['spaces', projectId, 'scheme'] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <SelectField
      label={t('notifications.schemeField')}
      disabled={disabled}
      value={current.data?.schemeId ?? ''}
      onChange={(event) => set.mutate(event.target.value)}
      options={(choices.data ?? []).map((item) => ({ value: item.schemeId!, label: item.name! }))}
    />
  );
}
