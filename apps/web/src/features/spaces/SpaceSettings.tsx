import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, Navigate, Outlet, useOutletContext, useParams } from 'react-router';
import ButtonBase from '@mui/material/ButtonBase';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { UserPicker, type PickerUser } from '../../components/UserPicker';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { font, radius, sem, size } from '../../theme/tokens';
import { SpacePermission, useMembers, useSpace, type Space, type SpaceMember } from './api';

const ROLE_LABELS: Record<string, string> = {
  ProjectAdmin: 'spaces.roleProjectAdmin',
  Member: 'spaces.roleMember',
  Viewer: 'spaces.roleViewer',
};

export function SettingsMenuItem({ to, label }: { to: string; label: string }) {
  return (
    <ButtonBase
      component={NavLink}
      to={to}
      className="flex h-8 w-full items-center px-2"
      sx={{
        justifyContent: 'flex-start',
        borderRadius: `${radius.control}px`,
        fontSize: font.size.md,
        fontWeight: font.weight.medium,
        color: sem('text-secondary'),
        '&:hover': { backgroundColor: sem('bg-hover') },
        '&.active': { backgroundColor: sem('bg-selected'), color: sem('text-accent') },
      }}
    >
      {label}
    </ButtonBase>
  );
}

export function SpaceSettingsLayout() {
  const { t } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);

  if (space.isPending) {
    return <LoadingState />;
  }
  if (space.isError) {
    return <ErrorState onRetry={() => void space.refetch()} />;
  }

  return (
    <SettingsFrame
      title={t('spaces.settings')}
      backTo={`/spaces/${space.data.key}/issues`}
      context={space.data}
      menu={
        <>
          <SettingsMenuItem to="general" label={t('spaces.general')} />
          <SettingsMenuItem to="members" label={t('spaces.members')} />
          <SettingsMenuItem to="workflows" label={t('workflows.title')} />
        </>
      }
    />
  );
}

export function SettingsFrame({ title, backTo, menu, context }: { title: string; backTo: string; menu: ReactNode; context?: unknown }) {
  return (
    <div className="flex min-h-0 flex-1">
      <aside
        className="flex shrink-0 flex-col gap-1 p-4"
        style={{ width: size.sidebar, backgroundColor: sem('bg-surface'), borderRight: `1px solid ${sem('border-default')}` }}
      >
        <BackHeader title={title} backTo={backTo} />
        {menu}
      </aside>
      <main className="min-w-0 flex-1 overflow-auto p-8" style={{ backgroundColor: sem('bg-surface') }}>
        <div className="max-w-4xl">
          <Outlet context={context} />
        </div>
      </main>
    </div>
  );
}

function BackHeader({ title, backTo }: { title: string; backTo: string }) {
  const { t } = useTranslation();

  return (
    <div className="flex items-center gap-2 pb-2">
      <IconButton component={Link} to={backTo} size="small" aria-label={t('spaces.back')}>
        <ArrowLeft size={16} strokeWidth={1.5} aria-hidden />
      </IconButton>
      <Typography variant="h3">{title}</Typography>
    </div>
  );
}

export function SpaceGeneralPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canManage = space.permissions?.includes(SpacePermission.manage);
  const [name, setName] = useState(space.name ?? '');
  const [template, setTemplate] = useState(space.branchNameTemplate ?? '');

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.PUT('/api/v1/project/{id}', {
          params: { path: { id: space.id! } },
          body: { name, leadId: space.lead?.id, branchNameTemplate: template || null },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['spaces'] });
      toast('success', t('account.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      <Typography variant="h1">{t('spaces.general')}</Typography>
      <TextField label={t('spaces.name')} value={name} disabled={!canManage} onChange={(event) => setName(event.target.value)} />
      <TextField label={t('spaces.key')} value={space.key ?? ''} disabled />
      <TextField label={t('spaces.branchTemplate')} value={template} disabled={!canManage} onChange={(event) => setTemplate(event.target.value)} />
      {canManage && (
        <div>
          <Button type="submit" disabled={!name.trim() || save.isPending}>{t('common.save')}</Button>
        </div>
      )}
    </form>
  );
}

function AddMemberDialog({ space, onClose }: { space: Space; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [user, setUser] = useState<PickerUser | null>(null);
  const [role, setRole] = useState('Member');

  const users = useQuery({
    queryKey: ['users', 'directory', search],
    queryFn: () => unwrap(apiClient.GET('/api/v1/user-directory/search', { params: { query: { search } } })),
  });

  const add = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/v1/project/{id}/member', {
          params: { path: { id: space.id! } },
          body: { userId: user!.id!, role },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['spaces', space.id, 'members'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('spaces.addMember')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('spaces.addMember')}
          confirmDisabled={!user || add.isPending}
          onCancel={onClose}
          onConfirm={() => add.mutate()}
        />
      }
    >
      <UserPicker label={t('spaces.person')} options={users.data ?? []} value={user} onChange={setUser} onInputChange={setSearch} />
      <SelectField
        label={t('spaces.role')}
        value={role}
        onChange={(event) => setRole(event.target.value)}
        options={Object.entries(ROLE_LABELS).map(([value, label]) => ({ value, label: t(label) }))}
      />
      {add.isError && <Typography role="alert" color="error" variant="body2">{toMessage(add.error)}</Typography>}
    </Dialog>
  );
}

export function SpaceMembersPage() {
  const { t, i18n } = useTranslation();
  const space = useOutletContext<Space>();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const members = useMembers(space.id);
  const canManage = space.permissions?.includes(SpacePermission.manageMembers);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<SpaceMember | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['spaces', space.id, 'members'] });

  const updateRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: string }) =>
      unwrap(
        apiClient.PUT('/api/v1/project/{id}/member/{userId}', {
          params: { path: { id: space.id!, userId } },
          body: { role },
        }),
      ),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const remove = useMutation({
    mutationFn: (userId: string) =>
      unwrap(apiClient.DELETE('/api/v1/project/{id}/member/{userId}', { params: { path: { id: space.id!, userId } } })),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setRemoving(null),
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center">
        <div className="flex-1">
          <Typography variant="h1">{t('spaces.members')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('spaces.membersHint')}</Typography>
        </div>
        {canManage && <Button onClick={() => setAdding(true)}>{t('spaces.addMember')}</Button>}
      </div>
      {members.isPending && <LoadingState />}
      {members.isError && <ErrorState onRetry={() => void members.refetch()} />}
      {members.data && (
        <Table<SpaceMember>
          label={t('spaces.members')}
          rows={members.data}
          rowKey={(member) => member.user!.id!}
          columns={[
            {
              key: 'name',
              header: t('spaces.name'),
              render: (member) => (
                <span className="flex items-center gap-2">
                  <Avatar name={member.user?.name ?? ''} />
                  {member.user?.name}
                </span>
              ),
            },
            { key: 'email', header: t('auth.email'), render: (member) => member.user?.email },
            {
              key: 'role',
              header: t('spaces.role'),
              render: (member) =>
                canManage ? (
                  <SelectField
                    quiet
                    label={`${t('spaces.role')}: ${member.user?.name}`}
                    value={member.role ?? 'Member'}
                    onChange={(event) => updateRole.mutate({ userId: member.user!.id!, role: event.target.value })}
                    options={Object.entries(ROLE_LABELS).map(([value, label]) => ({ value, label: t(label) }))}
                  />
                ) : (
                  t(ROLE_LABELS[member.role ?? ''] ?? 'spaces.roleMember')
                ),
            },
            { key: 'joined', header: t('spaces.joined'), render: (member) => formatDate(member.joinedAt, i18n.language) },
            {
              key: 'actions',
              header: '',
              render: (member) =>
                canManage && (
                  <Button kind="ghost" aria-label={`${t('spaces.remove')} ${member.user?.name}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRemoving(member)}>
                    {t('spaces.remove')}
                  </Button>
                ),
            },
          ]}
        />
      )}
      {adding && <AddMemberDialog space={space} onClose={() => setAdding(false)} />}
      <DialogConfirm
        open={Boolean(removing)}
        title={t('spaces.removeTitle')}
        description={t('spaces.removeBody', { name: removing?.user?.name })}
        confirmLabel={t('spaces.remove')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => remove.mutate(removing!.user!.id!)}
      />
    </div>
  );
}

export const SpaceSettingsIndex = () => <Navigate to="general" replace />;
