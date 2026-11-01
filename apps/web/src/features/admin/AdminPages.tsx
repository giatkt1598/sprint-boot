import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, Navigate, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { Pagination } from '../../components/Pagination';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { SettingsFrame, SettingsMenuItem } from '../spaces/SpaceSettings';

type User = components['schemas']['UserSummaryDto'];
type DeletedIssue = components['schemas']['DeletedIssueDto'];

const PAGE_SIZE = 20;

export function AdminLayout() {
  const { t } = useTranslation();
  const canManageUsers = useCan('SprintBoot.Users.Manage');
  const canRestore = useCan('SprintBoot.Issues.Restore');

  if (!canManageUsers && !canRestore) {
    return <main className="p-8"><Typography role="alert">{t('errors.forbidden')}</Typography></main>;
  }

  return (
    <SettingsFrame
      title={t('admin.title')}
      backTo="/spaces"
      menu={
        <>
          {canManageUsers && <SettingsMenuItem to="users" label={t('admin.users')} />}
          {canRestore && <SettingsMenuItem to="deleted-issues" label={t('admin.deletedIssues')} />}
        </>
      }
    />
  );
}

export const AdminIndex = () => <Navigate to="users" replace />;

function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [systemRole, setRole] = useState('member');

  const create = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/user-admin', { body: { name, email, password, systemRole } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('admin.createUser')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('admin.createUser')}
          confirmDisabled={!name || !email || !password || create.isPending}
          onCancel={onClose}
          onConfirm={() => create.mutate()}
        />
      }
    >
      <TextField label={t('account.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      <TextField label={t('auth.email')} type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
      <TextField label={t('auth.password')} type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} />
      <SelectField
        label={t('admin.systemRole')}
        value={systemRole}
        onChange={(event) => setRole(event.target.value)}
        options={[
          { value: 'member', label: t('admin.roleMember') },
          { value: 'admin', label: t('admin.roleAdmin') },
        ]}
      />
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}

const StatusOf = ({ user }: { user: User }) => {
  const { t } = useTranslation();
  return user.isLocked ? <Badge tone="danger">{t('admin.locked')}</Badge> : <Badge tone="success">{t('admin.active')}</Badge>;
};

export function UsersPage() {
  const { t, i18n } = useTranslation();
  const [search, setSearch] = useState('');
  const [skip, setSkip] = useState(0);
  const [creating, setCreating] = useState(false);

  const users = useQuery({
    queryKey: ['admin', 'users', search, skip],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/user-admin', { params: { query: { Search: search, SkipCount: skip, MaxResultCount: PAGE_SIZE } } })),
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t('admin.users')}</Typography>
        <SearchInput label={t('spaces.filter')} value={search} onChange={(event) => { setSearch(event.target.value); setSkip(0); }} />
        <Button onClick={() => setCreating(true)}>{t('admin.createUser')}</Button>
      </div>
      {users.isPending && <LoadingState />}
      {users.isError && <ErrorState onRetry={() => void users.refetch()} />}
      {users.data && users.data.totalCount === 0 && <EmptyState title={t('admin.noUsers')} />}
      {users.data && (users.data.totalCount ?? 0) > 0 && (
        <>
          <Table<User>
            label={t('admin.users')}
            rows={users.data.items ?? []}
            rowKey={(user) => user.id!}
            columns={[
              {
                key: 'name',
                header: t('account.name'),
                render: (user) => (
                  <span className="flex items-center gap-2"><Avatar name={user.name ?? ''} />{user.name}</span>
                ),
              },
              { key: 'email', header: t('auth.email'), render: (user) => user.email },
              { key: 'role', header: t('admin.systemRole'), render: (user) => t(user.systemRole === 'admin' ? 'admin.roleAdmin' : 'admin.roleMember') },
              { key: 'status', header: t('admin.status'), render: (user) => <StatusOf user={user} /> },
              { key: 'joined', header: t('admin.joined'), render: (user) => formatDate(user.creationTime, i18n.language) },
              {
                key: 'manage',
                header: '',
                render: (user) => (
                  <Link component={RouterLink} to={user.id!} aria-label={`${t('admin.manage')} ${user.name}`}>{t('admin.manage')}</Link>
                ),
              },
            ]}
          />
          <Pagination skip={skip} pageSize={PAGE_SIZE} total={users.data.totalCount ?? 0} onChange={setSkip} />
        </>
      )}
      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

export function ManageUserPage() {
  const { t } = useTranslation();
  const { userId } = useParams();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);

  const user = useQuery({
    queryKey: ['admin', 'users', userId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/user-admin/{id}', { params: { path: { id: userId! } } })),
  });

  const onDone = {
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'users'] }),
    onError: (failure: unknown) => toast('error', toMessage(failure)),
  };

  const setRole = useMutation({
    mutationFn: (systemRole: string) =>
      unwrap(apiClient.POST('/api/v1/user-admin/{id}/set-system-role', { params: { path: { id: userId! } }, body: { systemRole } })),
    ...onDone,
  });
  const lock = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/user-admin/{id}/lock', { params: { path: { id: userId! } } })),
    ...onDone,
    onSettled: () => setConfirming(false),
  });
  const unlock = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/user-admin/{id}/unlock', { params: { path: { id: userId! } } })),
    ...onDone,
  });

  if (user.isPending) {
    return <LoadingState />;
  }
  if (user.isError) {
    return <NotFoundPage />;
  }

  return (
    <div className="flex max-w-md flex-col gap-4">
      <div className="flex items-center gap-2">
        <Avatar name={user.data.name ?? ''} />
        <Typography variant="h1" className="flex-1">{user.data.name}</Typography>
        <StatusOf user={user.data} />
      </div>
      <Typography color="textSecondary">{user.data.email}</Typography>
      <SelectField
        label={t('admin.systemRole')}
        value={user.data.systemRole ?? 'member'}
        onChange={(event) => setRole.mutate(event.target.value)}
        options={[
          { value: 'member', label: t('admin.roleMember') },
          { value: 'admin', label: t('admin.roleAdmin') },
        ]}
      />
      <div>
        {user.data.isLocked ? (
          <Button kind="secondary" onClick={() => unlock.mutate()}>{t('admin.unlockAccount')}</Button>
        ) : (
          <Button kind="danger" onClick={() => setConfirming(true)}>{t('admin.lockAccount')}</Button>
        )}
      </div>
      <DialogConfirm
        open={confirming}
        title={t('admin.lockTitle')}
        description={t('admin.lockBody', { name: user.data.name })}
        confirmLabel={t('admin.lockAccount')}
        onCancel={() => setConfirming(false)}
        onConfirm={() => lock.mutate()}
      />
    </div>
  );
}

export function DeletedIssuesPage() {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [skip, setSkip] = useState(0);

  const list = useQuery({
    queryKey: ['admin', 'deleted', skip],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/issue/deleted-list', { params: { query: { SkipCount: skip, MaxResultCount: PAGE_SIZE } } })),
  });

  const restore = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/issue/{id}/restore', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'deleted'] });
      toast('success', t('admin.restored'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <div className="flex flex-col gap-6">
      <Typography variant="h1">{t('admin.deletedIssues')}</Typography>
      {list.isPending && <LoadingState />}
      {list.isError && <ErrorState onRetry={() => void list.refetch()} />}
      {list.data && list.data.totalCount === 0 && <EmptyState title={t('admin.noDeleted')} />}
      {list.data && (list.data.totalCount ?? 0) > 0 && (
        <>
          <Table<DeletedIssue>
            label={t('admin.deletedIssues')}
            rows={list.data.items ?? []}
            rowKey={(issue) => issue.id!}
            columns={[
              { key: 'key', header: t('issues.key'), render: (issue) => issue.key },
              { key: 'title', header: t('issues.titleField'), render: (issue) => issue.title },
              { key: 'space', header: t('admin.spaceColumn'), render: (issue) => issue.projectName },
              { key: 'subtasks', header: t('admin.subtasks'), render: (issue) => issue.subtaskCount },
              { key: 'by', header: t('admin.deletedBy'), render: (issue) => issue.deletedBy?.name },
              { key: 'at', header: t('admin.deletedAt'), render: (issue) => formatDate(issue.deletedAt, i18n.language) },
              {
                key: 'restore',
                header: '',
                render: (issue) => (
                  <Button kind="ghost" aria-label={`${t('admin.restore')} ${issue.key}`} onClick={() => restore.mutate(issue.id!)}>
                    {t('admin.restore')}
                  </Button>
                ),
              },
            ]}
          />
          <Pagination skip={skip} pageSize={PAGE_SIZE} total={list.data.totalCount ?? 0} onChange={setSkip} />
        </>
      )}
    </div>
  );
}
