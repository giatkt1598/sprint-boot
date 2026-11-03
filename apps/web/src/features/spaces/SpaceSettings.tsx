import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, Navigate, Outlet, useOutletContext, useParams } from 'react-router';
import ButtonBase from '@mui/material/ButtonBase';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { DialogConfirm } from '../../components/Dialog';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { font, radius, sem, size } from '../../theme/tokens';
import { SpaceSchemeField } from '../notifications/NotificationPages';
import { InviteMemberDialog, SpaceInvitations } from '../admin/InvitationPages';
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
          <SettingsMenuItem to="board" label={t('agile.board')} />
          <SettingsMenuItem to="workflows" label={t('workflows.title')} />
          {space.data.permissions?.includes(SpacePermission.manage) && <SettingsMenuItem to="git" label={t('git.title')} />}
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

const SPRINT_PRESETS = [14, 21, 28];
const DEFAULT_SPRINT_DAYS = 14;
const CUSTOM_LENGTH = 'custom';

export function SpaceGeneralPage() {
  const { t } = useTranslation();
  const space = useOutletContext<Space>();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canManage = space.permissions?.includes(SpacePermission.manage);
  const [name, setName] = useState(space.name ?? '');
  const [template, setTemplate] = useState(space.branchNameTemplate ?? '');
  const [lengthDays, setLengthDays] = useState(space.sprintLengthDays ?? DEFAULT_SPRINT_DAYS);
  const [customLength, setCustomLength] = useState(!SPRINT_PRESETS.includes(space.sprintLengthDays ?? DEFAULT_SPRINT_DAYS));

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.PUT('/api/v1/project/{id}', {
          params: { path: { id: space.id! } },
          body: { name, leadId: space.lead?.id, branchNameTemplate: template || null, sprintLengthDays: lengthDays },
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
      <TextField label={t('spaces.branchTemplate')} placeholder="feature/{issue_key}-{issue_title}" value={template} disabled={!canManage} onChange={(event) => setTemplate(event.target.value)} />
      {space.type === 0 && (
        <>
          <SelectField
            label={t('spaces.sprintLength')}
            value={customLength ? CUSTOM_LENGTH : String(lengthDays)}
            disabled={!canManage}
            onChange={(event) => {
              const custom = event.target.value === CUSTOM_LENGTH;
              setCustomLength(custom);
              if (!custom) {
                setLengthDays(Number(event.target.value));
              }
            }}
            options={[
              ...SPRINT_PRESETS.map((days) => ({ value: String(days), label: t('agile.lengthWeeks', { count: days / 7 }) })),
              { value: CUSTOM_LENGTH, label: t('spaces.sprintLengthCustom') },
            ]}
          />
          {customLength && (
            <TextField
              label={t('spaces.sprintLengthDays')}
              type="number"
              disabled={!canManage}
              value={lengthDays}
              onChange={(event) => setLengthDays(Math.min(365, Math.max(1, Number(event.target.value) || 1)))}
            />
          )}
        </>
      )}
      <SpaceSchemeField projectId={space.id!} disabled={!canManage} />
      {canManage && (
        <div>
          <Button type="submit" disabled={!name.trim() || save.isPending}>{t('common.save')}</Button>
        </div>
      )}
    </form>
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
        {canManage && <Button onClick={() => setAdding(true)}>{t('invitations.inviteMember')}</Button>}
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
                  <Avatar name={member.user?.name ?? ''} src={member.user?.avatarUrl} />
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
      {canManage && <SpaceInvitations spaceId={space.id!} />}
      {adding && (
        <InviteMemberDialog
          spaceId={space.id!}
          spaceName={space.name ?? ''}
          onClose={() => {
            setAdding(false);
            void refresh();
          }}
        />
      )}
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
