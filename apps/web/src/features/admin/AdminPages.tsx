import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, Navigate, useNavigate, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { MenuItem } from '../../components/Menu';
import { Pagination } from '../../components/Pagination';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { sem } from '../../theme/tokens';
import { useSpaces } from '../spaces/api';
import { SettingsFrame, SettingsMenuItem } from '../spaces/SpaceSettings';

type User = components['schemas']['UserSummaryDto'];
type DeletedIssue = components['schemas']['DeletedIssueDto'];
type UserSpace = components['schemas']['UserSpaceDto'];
type SignInMethod = components['schemas']['SignInMethodDto'];

const PAGE_SIZE = 20;

export function AdminLayout() {
  const { t } = useTranslation();
  const canManageUsers = useCan('SprintBoot.Users.Manage');
  const canRestore = useCan('SprintBoot.Issues.Restore');
  const canManageWorkflows = useCan('SprintBoot.Workflows.Manage');
  const canManageSchemes = useCan('SprintBoot.NotificationSchemes.Manage');
  const canManageInvitations = useCan('SprintBoot.Invitations.Manage');
  const canManageSso = useCan('SprintBoot.Sso.Manage');
  const canManageEmail = useCan('SprintBoot.Email.Manage');

  if (!canManageEmail && !canManageUsers && !canRestore && !canManageWorkflows && !canManageSchemes && !canManageInvitations && !canManageSso) {
    return <main className="p-8"><Typography role="alert">{t('errors.forbidden')}</Typography></main>;
  }

  return (
    <SettingsFrame
      title={t('admin.title')}
      backTo="/"
      menu={
        <>
          {canManageUsers && <SettingsMenuItem to="spaces" label={t('spaces.title')} />}
          {canManageUsers && <SettingsMenuItem to="users" label={t('admin.users')} />}
          {canManageInvitations && <SettingsMenuItem to="invitations" label={t('invitations.title')} />}
          {canManageSso && <SettingsMenuItem to="sso" label={t('sso.title')} />}
          {canManageEmail && <SettingsMenuItem to="email" label={t('email.title')} />}
          {canManageWorkflows && <SettingsMenuItem to="workflows" label={t('workflows.title')} />}
          {canManageSchemes && <SettingsMenuItem to="notification-schemes" label={t('topBar.notifications')} />}
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

const SPACE_ROLES: Record<string, string> = {
  ProjectAdmin: 'spaces.roleProjectAdmin',
  Member: 'spaces.roleMember',
  Viewer: 'spaces.roleViewer',
};

function AddToSpaceDialog({ userId, joined, onClose }: { userId: string; joined: string[]; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const spaces = useSpaces();
  const available = (spaces.data ?? []).filter((space) => !joined.includes(space.id!));
  const [spaceId, setSpaceId] = useState('');
  const [role, setRole] = useState('Member');

  const add = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/project/{id}/member', { params: { path: { id: spaceId } }, body: { userId, role } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users', userId, 'spaces'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('admin.addToSpace')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('admin.addToSpace')}
          confirmDisabled={!spaceId || add.isPending}
          onCancel={onClose}
          onConfirm={() => add.mutate()}
        />
      }
    >
      <SelectField
        label={t('admin.spaceColumn')}
        value={spaceId}
        onChange={(event) => setSpaceId(event.target.value)}
        options={available.map((space) => ({ value: space.id!, label: space.name! }))}
      />
      <SelectField
        label={t('spaces.role')}
        value={role}
        onChange={(event) => setRole(event.target.value)}
        options={Object.entries(SPACE_ROLES).map(([value, label]) => ({ value, label: t(label) }))}
      />
      {add.isError && <Typography role="alert" color="error" variant="body2">{toMessage(add.error)}</Typography>}
    </Dialog>
  );
}

/** Deleting cannot be undone, so the admin types the email to confirm they mean this person. */
function DeleteUserDialog({ name, email, onCancel, onDelete, pending, error }: {
  name: string;
  email: string;
  onCancel: () => void;
  onDelete: () => void;
  pending: boolean;
  error: string | null;
}) {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');

  return (
    <Dialog
      open
      title={t('admin.deleteTitle', { name })}
      onClose={onCancel}
      footer={
        <DialogFooter
          confirmLabel={t('admin.deleteAccount')}
          confirmKind="danger"
          confirmDisabled={typed.trim().toLowerCase() !== email.toLowerCase() || pending}
          onCancel={onCancel}
          onConfirm={onDelete}
        />
      }
    >
      <Typography variant="body1" color="textSecondary">{t('admin.deleteBody')}</Typography>
      <TextField label={t('admin.deleteConfirm', { email })} autoFocus value={typed} onChange={(event) => setTyped(event.target.value)} />
      {error && <Typography role="alert" color="error" variant="body2">{error}</Typography>}
    </Dialog>
  );
}

export function ManageUserPage() {
  const { t, i18n } = useTranslation();
  const { userId } = useParams();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const navigate = useNavigate();
  const [roleAnchor, setRoleAnchor] = useState<HTMLElement | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<UserSpace | null>(null);

  const user = useQuery({
    queryKey: ['admin', 'users', userId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/user-admin/{id}', { params: { path: { id: userId! } } })),
  });
  const spaces = useQuery({
    queryKey: ['admin', 'users', userId, 'spaces'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/project-admin/user-spaces/{userId}', { params: { path: { userId: userId! } } })),
  });
  const methods = useQuery({
    queryKey: ['admin', 'users', userId, 'methods'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/user-admin/{id}/sign-in-methods', { params: { path: { id: userId! } } })),
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
  const removeFromSpace = useMutation({
    mutationFn: (space: UserSpace) =>
      unwrap(apiClient.DELETE('/api/v1/project/{id}/member/{userId}', { params: { path: { id: space.projectId!, userId: userId! } } })),
    ...onDone,
    onSettled: () => setRemoving(null),
  });

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/user-admin/{id}', { params: { path: { id: userId! } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
      toast('success', t('admin.deleted'));
      await navigate('/admin/users');
    },
  });

  if (user.isPending) {
    return <LoadingState />;
  }
  if (user.isError) {
    return <NotFoundPage />;
  }

  const roleLabel = t(user.data.systemRole === 'admin' ? 'admin.roleAdmin' : 'admin.roleMember');
  const pickRole = (systemRole: string) => {
    setRoleAnchor(null);
    setRole.mutate(systemRole);
  };

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          <Typography variant="h1">{user.data.name}</Typography>
          <Typography variant="body2" color="textSecondary">
            {user.data.email}
            {user.data.lastSignInAt ? ` · ${t('admin.lastActive', { date: formatDate(user.data.lastSignInAt, i18n.language) })}` : ''}
          </Typography>
        </div>
        <StatusOf user={user.data} />
        <Button kind="secondary" onClick={(event) => setRoleAnchor(event.currentTarget)}>
          {t('admin.systemRole')}: {roleLabel}
        </Button>
        <Menu open={Boolean(roleAnchor)} anchorEl={roleAnchor} onClose={() => setRoleAnchor(null)}>
          <MenuItem onClick={() => pickRole('member')}>{t('admin.roleMember')}</MenuItem>
          <MenuItem onClick={() => pickRole('admin')}>{t('admin.roleAdmin')}</MenuItem>
        </Menu>
        {user.data.isLocked ? (
          <Button kind="secondary" onClick={() => unlock.mutate()}>{t('admin.unlockAccount')}</Button>
        ) : (
          <Button kind="secondary" onClick={() => setConfirming(true)}>{t('admin.lockAccount')}</Button>
        )}
        <Button kind="danger" onClick={() => setDeleting(true)}>{t('admin.deleteAccount')}</Button>
      </div>

      <section className="flex flex-col gap-2">
        <div className="flex items-center">
          <Typography variant="h2" className="flex-1">{t('spaces.title')}</Typography>
          <Button kind="secondary" onClick={() => setAdding(true)}>{t('admin.addToSpace')}</Button>
        </div>
        {spaces.isPending && <LoadingState />}
        {spaces.isError && <ErrorState onRetry={() => void spaces.refetch()} />}
        {spaces.data?.length === 0 && <EmptyState title={t('admin.noSpaces')} />}
        {(spaces.data?.length ?? 0) > 0 && (
          <Table<UserSpace>
            label={t('spaces.title')}
            rows={spaces.data!}
            rowKey={(space) => space.projectId!}
            columns={[
              { key: 'name', header: t('admin.spaceColumn'), render: (space) => space.name },
              { key: 'key', header: t('spaces.key'), render: (space) => space.key },
              { key: 'role', header: t('spaces.role'), render: (space) => t(SPACE_ROLES[space.role ?? ''] ?? 'spaces.roleMember') },
              { key: 'added', header: t('admin.added'), render: (space) => formatDate(space.addedAt, i18n.language) },
              {
                key: 'remove',
                header: '',
                render: (space) => (
                  <Button kind="ghost" aria-label={`${t('spaces.remove')} ${space.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRemoving(space)}>
                    {t('spaces.remove')}
                  </Button>
                ),
              },
            ]}
          />
        )}
      </section>

      <section className="flex flex-col gap-2">
        <Typography variant="h2">{t('admin.signInMethods')}</Typography>
        {methods.isPending && <LoadingState />}
        {methods.isError && <ErrorState onRetry={() => void methods.refetch()} />}
        {methods.data && (
          <Table<SignInMethod>
            label={t('admin.signInMethods')}
            rows={methods.data}
            rowKey={(method) => method.method!}
            columns={[
              { key: 'method', header: t('admin.method'), render: (method) => (method.method === 'password' ? t('admin.methodPassword') : method.method) },
              { key: 'status', header: t('admin.status'), render: (method) => t(method.isEnabled ? 'admin.methodOn' : 'admin.methodOff') },
              { key: 'account', header: t('admin.methodAccount'), render: (method) => method.account },
              { key: 'linked', header: t('admin.methodLinked'), render: (method) => formatDate(method.linkedAt, i18n.language) },
              { key: 'used', header: t('admin.methodUsed'), render: (method) => formatDate(method.lastUsedAt, i18n.language) },
            ]}
          />
        )}
      </section>

      <DialogConfirm
        open={confirming}
        title={t('admin.lockTitle')}
        description={t('admin.lockBody', { name: user.data.name })}
        confirmLabel={t('admin.lockAccount')}
        onCancel={() => setConfirming(false)}
        onConfirm={() => lock.mutate()}
      />
      <DialogConfirm
        open={Boolean(removing)}
        title={t('spaces.removeTitle')}
        description={t('spaces.removeBody', { name: user.data.name })}
        confirmLabel={t('spaces.remove')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removeFromSpace.mutate(removing!)}
      />
      {deleting && (
        <DeleteUserDialog
          name={user.data.name ?? ''}
          email={user.data.email ?? ''}
          pending={remove.isPending}
          error={remove.isError ? toMessage(remove.error) : null}
          onCancel={() => setDeleting(false)}
          onDelete={() => remove.mutate()}
        />
      )}
      {adding && (
        <AddToSpaceDialog
          userId={userId!}
          joined={(spaces.data ?? []).map((space) => space.projectId!)}
          onClose={() => setAdding(false)}
        />
      )}
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
