import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { Pagination } from '../../components/Pagination';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { sem } from '../../theme/tokens';
import { useSpaces } from '../spaces/api';

type Invitation = components['schemas']['InvitationDto'];
type InvitationLink = components['schemas']['InvitationLinkDto'];

const PAGE_SIZE = 20;
const STATUS: [string, BadgeTone][] = [
  ['pending', 'warning'],
  ['accepted', 'success'],
  ['revoked', 'neutral'],
  ['expired', 'neutral'],
];
const DELIVERY: [string, BadgeTone][] = [
  ['queued', 'neutral'],
  ['sent', 'success'],
  ['failed', 'danger'],
];
const SPACE_ROLES = ['Viewer', 'Member', 'Admin'];

function useRoleOptions() {
  const { t } = useTranslation();
  return SPACE_ROLES.map((value) => ({ value, label: t(`invitations.roles.${value}`) }));
}

/** Shared by both dialogs: existing people join at once, others get an email. */
function useInvite(onClose: () => void) {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: components['schemas']['CreateInvitationInput']) =>
      unwrap(apiClient.POST('/api/v1/invitation', { body })),
    onSuccess: async (result: InvitationLink) => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'invitations'] });
      await queryClient.invalidateQueries({ queryKey: ['spaces'] });
      toast('success', t(result.addedDirectly ? 'invitations.addedDirectly' : 'invitations.sent'));
      onClose();
    },
  });
}

export function InviteMemberDialog({ spaceId, spaceName, onClose }: { spaceId: string; spaceName: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const roles = useRoleOptions();
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('Member');
  const invite = useInvite(onClose);

  return (
    <Dialog
      open
      title={t('invitations.inviteTo', { space: spaceName })}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('invitations.send')}
          confirmDisabled={!email || invite.isPending}
          onCancel={onClose}
          onConfirm={() => invite.mutate({ email, projects: [{ projectId: spaceId, role }] })}
        />
      }
    >
      <Typography color="textSecondary">{t('invitations.memberHint')}</Typography>
      <TextField label={t('auth.email')} type="email" autoFocus value={email} onChange={(event) => setEmail(event.target.value)} />
      <SelectField label={t('invitations.spaceRole')} value={role} onChange={(event) => setRole(event.target.value)} options={roles} />
      <Typography variant="body2" color="textSecondary">{t('invitations.roleCap')}</Typography>
      {invite.isError && <Typography role="alert" color="error" variant="body2">{toMessage(invite.error)}</Typography>}
    </Dialog>
  );
}

function InviteUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const roles = useRoleOptions();
  const canManageUsers = useCan('SprintBoot.Users.Manage');
  const spaces = useSpaces();
  const [email, setEmail] = useState('');
  const [systemRole, setSystemRole] = useState('member');
  const [spaceId, setSpaceId] = useState('');
  const [role, setRole] = useState('Member');
  const invite = useInvite(onClose);

  return (
    <Dialog
      open
      title={t('invitations.inviteUser')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('invitations.send')}
          confirmDisabled={!email || (!canManageUsers && !spaceId) || invite.isPending}
          onCancel={onClose}
          onConfirm={() =>
            invite.mutate({
              email,
              systemRole: canManageUsers ? systemRole : null,
              projects: spaceId ? [{ projectId: spaceId, role }] : [],
            })
          }
        />
      }
    >
      <TextField label={t('auth.email')} type="email" autoFocus value={email} onChange={(event) => setEmail(event.target.value)} />
      {canManageUsers && (
        <SelectField
          label={t('admin.systemRole')}
          value={systemRole}
          onChange={(event) => setSystemRole(event.target.value)}
          options={[
            { value: 'member', label: t('admin.roleMember') },
            { value: 'admin', label: t('admin.roleAdmin') },
          ]}
        />
      )}
      <SelectField
        label={t('invitations.space')}
        value={spaceId}
        onChange={(event) => setSpaceId(event.target.value)}
        options={[
          { value: '', label: t('invitations.noSpace') },
          ...(spaces.data ?? []).map((space) => ({ value: space.id!, label: space.name ?? '' })),
        ]}
      />
      {spaceId && <SelectField label={t('invitations.spaceRole')} value={role} onChange={(event) => setRole(event.target.value)} options={roles} />}
      <Typography variant="body2" color="textSecondary">{t('invitations.roleCap')}</Typography>
      {invite.isError && <Typography role="alert" color="error" variant="body2">{toMessage(invite.error)}</Typography>}
    </Dialog>
  );
}

const describeAccess = (invitation: Invitation, t: (key: string) => string) => {
  const project = invitation.projects?.[0];
  if (project) {
    return `${t(`invitations.roles.${project.role}`)}, ${project.projectName}`;
  }
  return invitation.systemRole === 'admin' ? t('admin.roleAdmin') : t('admin.roleMember');
};

export function InvitationsPage() {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [skip, setSkip] = useState(0);
  const [inviting, setInviting] = useState(false);
  const [revoking, setRevoking] = useState<Invitation | null>(null);

  const invitations = useQuery({
    queryKey: ['admin', 'invitations', skip],
    placeholderData: keepPreviousData,
    queryFn: () => unwrap(apiClient.GET('/api/v1/invitation', { params: { query: { SkipCount: skip, MaxResultCount: PAGE_SIZE } } })),
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin', 'invitations'] });

  const resend = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/invitation/{id}/resend', { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      toast('success', t('invitations.sent'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const copy = useMutation({
    mutationFn: async (id: string) => {
      const result = await unwrap(apiClient.POST('/api/v1/invitation/{id}/link', { params: { path: { id } } }));
      await navigator.clipboard.writeText(result.link ?? '');
    },
    onSuccess: () => toast('success', t('invitations.copied')),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const revoke = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.POST('/api/v1/invitation/{id}/revoke', { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      setRevoking(null);
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('invitations.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('invitations.subtitle')}</Typography>
        </div>
        <Button onClick={() => setInviting(true)}>{t('invitations.inviteUser')}</Button>
      </div>
      {invitations.isPending && <LoadingState />}
      {invitations.isError && <ErrorState onRetry={() => void invitations.refetch()} />}
      {invitations.data?.totalCount === 0 && <EmptyState title={t('invitations.empty')} />}
      {(invitations.data?.totalCount ?? 0) > 0 && (
        <>
          <Table<Invitation>
            label={t('invitations.title')}
            rows={invitations.data?.items ?? []}
            rowKey={(item) => item.id!}
            columns={[
              { key: 'email', header: t('auth.email'), render: (item) => item.email },
              { key: 'access', header: t('invitations.access'), render: (item) => describeAccess(item, t) },
              { key: 'by', header: t('invitations.invitedBy'), render: (item) => item.invitedBy?.name ?? '' },
              {
                key: 'status',
                header: t('invitations.status'),
                render: (item) => {
                  const [name, tone] = STATUS[item.status ?? 0]!;
                  return <Badge tone={tone}>{t(`invitations.statuses.${name}`)}</Badge>;
                },
              },
              {
                key: 'delivery',
                header: t('invitations.delivery'),
                render: (item) => {
                  const [name, tone] = DELIVERY[item.emailStatus ?? 0]!;
                  return <Badge tone={tone}>{t(`invitations.deliveries.${name}`)}</Badge>;
                },
              },
              {
                key: 'expires',
                header: t('invitations.expires'),
                render: (item) => (item.status === 1 ? '—' : formatDate(item.expiresAt ?? '', i18n.language)),
              },
              {
                key: 'actions',
                header: <span className="sr-only">{t('invitations.actions')}</span>,
                render: (item) =>
                  item.status === 0 && (
                    <div className="flex gap-2 whitespace-nowrap">
                      <Link component="button" variant="body2" aria-label={`${t('invitations.resend')} ${item.email}`} onClick={() => resend.mutate(item.id!)}>{t('invitations.resend')}</Link>
                      <Link component="button" variant="body2" aria-label={`${t('invitations.copyLink')} ${item.email}`} onClick={() => copy.mutate(item.id!)}>{t('invitations.copyLink')}</Link>
                      <Link component="button" variant="body2" aria-label={`${t('invitations.revoke')} ${item.email}`} sx={{ color: sem('feedback-danger-text') }} onClick={() => setRevoking(item)}>{t('invitations.revoke')}</Link>
                    </div>
                  ),
              },
            ]}
          />
          <Pagination total={invitations.data?.totalCount ?? 0} pageSize={PAGE_SIZE} skip={skip} onChange={setSkip} />
        </>
      )}
      {inviting && <InviteUserDialog onClose={() => setInviting(false)} />}
      <DialogConfirm
        open={revoking !== null}
        title={t('invitations.revokeTitle')}
        description={t('invitations.revokeBody', { email: revoking?.email })}
        confirmLabel={t('invitations.revoke')}
        onCancel={() => setRevoking(null)}
        onConfirm={() => revoking && revoke.mutate(revoking.id!)}
      />
    </div>
  );
}
