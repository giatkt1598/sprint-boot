import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useOutletContext, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { useCan } from '../../auth/AuthProvider';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { CopyField } from '../../components/CopyField';
import { Dialog, DialogConfirm } from '../../components/Dialog';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { radius, sem } from '../../theme/tokens';
import { SpacePermission, type Space } from '../spaces/api';

type Connection = components['schemas']['GitConnectionDto'];
type Repository = components['schemas']['GitRepositoryDto'];
type SaveConnection = components['schemas']['SaveGitConnectionInput'];

const GITHUB = 0;
const GITEA = 1;
const ACCESS_TOKEN = 1;
const OAUTH2 = 2;
const NEEDS_ATTENTION = 2;
const STATUS: [string, BadgeTone][] = [
  ['pending', 'neutral'],
  ['connected', 'success'],
  ['attention', 'danger'],
];
const WEBHOOK: [string, BadgeTone][] = [
  ['notRegistered', 'warning'],
  ['active', 'success'],
  ['failed', 'danger'],
];

function Banner({ children }: { children: string }) {
  return (
    <div
      role="alert"
      className="flex items-center gap-2 p-3"
      style={{ backgroundColor: sem('feedback-danger-bg'), color: sem('feedback-danger-text'), borderRadius: radius.card }}
    >
      <TriangleAlert size={20} strokeWidth={1.5} aria-hidden />
      <Typography variant="body2" sx={{ color: 'inherit' }}>{children}</Typography>
    </div>
  );
}

/** GitRepositoryAccess.Selected: only the ticked repositories reach the space. */
const SELECTED_REPOSITORIES = 1;

const useRefresh = () => {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['admin', 'git'] });
};

function ConnectionDialog({ projectId, connection, onClose }: { projectId: string; connection: Connection | null; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const refresh = useRefresh();
  const editing = connection !== null;
  const [provider, setProvider] = useState<SaveConnection['providerType']>(connection?.providerType ?? GITHUB);
  const [name, setName] = useState(connection?.name ?? '');
  const [baseUrl, setBaseUrl] = useState(connection?.baseUrl ?? '');
  const [authType, setAuthType] = useState<SaveConnection['authType']>(connection?.authType === OAUTH2 ? OAUTH2 : ACCESS_TOKEN);
  const [accessToken, setAccessToken] = useState('');
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const [removing, setRemoving] = useState(false);
  const isGitHub = provider === GITHUB;
  const redirects = isGitHub || authType === OAUTH2;

  const body = (): SaveConnection => ({ projectId, name, providerType: provider, baseUrl, authType, accessToken, clientId, clientSecret });
  const redirectUris = useQuery({
    queryKey: ['admin', 'git', 'redirect-uris'],
    enabled: redirects,
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-connection/redirect-uris')),
  });

  const save = useMutation({
    mutationFn: async () => {
      const saved = editing
        ? await unwrap(apiClient.PUT('/api/v1/git-connection/{id}', { params: { path: { id: connection.id! } }, body: body() }))
        : await unwrap(apiClient.POST('/api/v1/git-connection', { body: body() }));
      if (redirects && !(editing && isGitHub)) {
        const target = await unwrap(apiClient.GET('/api/v1/git-connection/{id}/authorize-url', { params: { path: { id: saved.id! } } }));
        window.location.assign(target.url ?? '/');
      }
    },
    onSuccess: async () => {
      await refresh();
      onClose();
    },
  });

  const test = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/git-connection/test', { body: { connectionId: connection?.id, baseUrl, accessToken } })),
    onSuccess: (result) => toast(result.succeeded ? 'success' : 'error', t(result.succeeded ? 'git.testPassed' : 'git.testFailed')),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/git-connection/{id}', { params: { path: { id: connection!.id! } } })),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const missing = !name || (!isGitHub && !baseUrl)
    || (!editing && !isGitHub && (authType === ACCESS_TOKEN ? !accessToken : !clientId || !clientSecret));

  return (
    <Dialog
      open
      title={editing ? t('git.editTitle', { name: connection.name }) : t('git.addTitle')}
      onClose={onClose}
      footer={
        <div className="flex w-full items-center gap-2">
          {editing && <Button kind="danger" onClick={() => setRemoving(true)}>{t('git.remove')}</Button>}
          <span className="flex-1" />
          <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
          {!isGitHub && authType === ACCESS_TOKEN && (
            <Button kind="secondary" disabled={test.isPending} onClick={() => test.mutate()}>{t('git.test')}</Button>
          )}
          <Button disabled={missing || save.isPending} onClick={() => save.mutate()}>
            {editing || !redirects ? t('git.save') : t(isGitHub ? 'git.installApp' : 'git.authorize')}
          </Button>
        </div>
      }
    >
      {connection?.status === NEEDS_ATTENTION && <Banner>{t(`git.problem.${connection.problem}`, { name: connection.name })}</Banner>}
      {!editing && (
        <div role="radiogroup" aria-label={t('git.provider')} className="flex gap-2">
          {[GITHUB, GITEA].map((value) => (
            <button
              key={value}
              type="button"
              role="radio"
              aria-checked={provider === value}
              className="flex flex-1 cursor-pointer flex-col p-3 text-left"
              style={{
                backgroundColor: provider === value ? sem('bg-selected') : 'transparent',
                border: `1px solid ${provider === value ? sem('border-focus') : sem('border-default')}`,
                borderRadius: radius.card,
              }}
              onClick={() => setProvider(value as SaveConnection['providerType'])}
            >
              <Typography sx={{ color: provider === value ? sem('text-accent') : undefined }}>{value === GITHUB ? 'GitHub' : 'Gitea'}</Typography>
              <Typography variant="body2" color="textSecondary">{t(value === GITHUB ? 'git.githubHint' : 'git.giteaHint')}</Typography>
            </button>
          ))}
        </div>
      )}
      <TextField label={t('git.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      {!isGitHub && (
        <>
          <TextField label={t('git.baseUrl')} value={baseUrl} disabled={editing} onChange={(event) => setBaseUrl(event.target.value)} />
          {editing && <Typography variant="body2" color="textSecondary">{t('git.baseUrlHint')}</Typography>}
          <SelectField
            label={t('git.authentication')}
            value={String(authType)}
            onChange={(event) => setAuthType(Number(event.target.value) as SaveConnection['authType'])}
            options={[
              { value: String(ACCESS_TOKEN), label: t('git.accessToken') },
              { value: String(OAUTH2), label: 'OAuth2' },
            ]}
          />
          {authType === ACCESS_TOKEN ? (
            <TextField
              label={t(editing ? 'git.newAccessToken' : 'git.accessToken')}
              type="password"
              autoComplete="off"
              placeholder={editing ? t('git.tokenPlaceholder') : undefined}
              value={accessToken}
              onChange={(event) => setAccessToken(event.target.value)}
            />
          ) : (
            <>
              <TextField label={t('sso.clientId')} value={clientId} onChange={(event) => setClientId(event.target.value)} />
              <TextField label={t('sso.clientSecret')} type="password" autoComplete="off" value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} />
            </>
          )}
        </>
      )}
      {redirects && redirectUris.data && (
        <CopyField
          label={t('git.redirectUri')}
          value={(isGitHub ? redirectUris.data.gitHubSetup : redirectUris.data.oAuth) ?? ''}
          copyLabel={t('git.copyRedirect')}
          copiedMessage={t('git.redirectCopied')}
        />
      )}
      <Typography variant="body2" color="textSecondary">
        {t(isGitHub ? 'git.installHint' : authType === OAUTH2 ? 'git.oauthHint' : 'git.tokenHint')}
      </Typography>
      {save.isError && <Typography role="alert" color="error" variant="body2">{toMessage(save.error)}</Typography>}
      <DialogConfirm
        open={removing}
        title={t('git.removeTitle')}
        description={t('git.removeBody', { name: connection?.name })}
        confirmLabel={t('git.remove')}
        onCancel={() => setRemoving(false)}
        onConfirm={() => remove.mutate()}
      />
    </Dialog>
  );
}

const BRANCH_TEMPLATE_PLACEHOLDER = 'feature/{issue_key}-{issue_title}';

/** How the branch name for an issue is suggested; it lives with the Git settings, not with the space's general ones. */
function BranchTemplateSection({ space }: { space: Space }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const canManage = Boolean(space.permissions?.includes(SpacePermission.manage));
  const [template, setTemplate] = useState(space.branchNameTemplate ?? '');

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.PUT('/api/v1/project/{id}', {
          params: { path: { id: space.id! } },
          // The update replaces every field, so the others go back as they are.
          body: { name: space.name!, leadId: space.lead?.id, branchNameTemplate: template || null, sprintLengthDays: space.sprintLengthDays },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['spaces'] });
      toast('success', t('account.saved'));
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <form
      className="flex max-w-xl flex-col gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <TextField
        label={t('git.branchTemplate')}
        placeholder={BRANCH_TEMPLATE_PLACEHOLDER}
        value={template}
        disabled={!canManage}
        onChange={(event) => setTemplate(event.target.value)}
      />
      <Typography variant="body2" color="textSecondary">{t('git.branchTemplateHint')}</Typography>
      {canManage && (
        <div>
          <Button type="submit" disabled={save.isPending || template === (space.branchNameTemplate ?? '')}>{t('common.save')}</Button>
        </div>
      )}
    </form>
  );
}

export function GitConnectionsPage() {
  const { t, i18n } = useTranslation();
  const space = useOutletContext<Space>();
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<Connection | null>(null);
  const connections = useQuery({
    queryKey: ['admin', 'git', 'connections', space.id],
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-connection', { params: { query: { projectId: space.id! } } })),
  });
  const canAssign = useCan('SprintBoot.Git.Manage');
  const toast = useToast();
  const toMessage = useErrorMessage();
  const refresh = useRefresh();
  const unassigned = useQuery({
    queryKey: ['admin', 'git', 'unassigned'],
    enabled: canAssign,
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-connection/unassigned')),
  });
  const assign = useMutation({
    mutationFn: (id: string) =>
      unwrap(apiClient.POST('/api/v1/git-connection/{id}/assign', { params: { path: { id } }, body: { projectId: space.id! } })),
    onSuccess: () => refresh(),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const failing = connections.data?.filter((item) => item.status === NEEDS_ATTENTION) ?? [];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('git.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('git.subtitle')}</Typography>
        </div>
        <Button onClick={() => setAdding(true)}>{t('git.add')}</Button>
      </div>
      {canAssign && unassigned.data?.map((item) => (
        <div key={item.id} role="status" className="flex items-center gap-2 p-3" style={{ backgroundColor: sem('feedback-warning-bg'), color: sem('feedback-warning-text'), borderRadius: radius.card }}>
          <Typography variant="body2" className="flex-1" sx={{ color: 'inherit' }}>{t('git.unassigned', { name: item.name })}</Typography>
          <Button kind="secondary" disabled={assign.isPending} onClick={() => assign.mutate(item.id!)}>{t('git.moveHere')}</Button>
        </div>
      ))}
      {failing.map((item) => <Banner key={item.id}>{t(`git.problem.${item.problem}`, { name: item.name })}</Banner>)}
      {connections.isPending && <LoadingState />}
      {connections.isError && <ErrorState onRetry={() => void connections.refetch()} />}
      {connections.data?.length === 0 && <EmptyState title={t('git.empty')} />}
      {(connections.data?.length ?? 0) > 0 && (
        <Table<Connection>
          label={t('git.title')}
          rows={connections.data ?? []}
          rowKey={(item) => item.id!}
          columns={[
            { key: 'name', header: t('git.name'), render: (item) => item.name },
            { key: 'provider', header: t('git.provider'), render: (item) => (item.providerType === GITHUB ? 'GitHub' : 'Gitea') },
            { key: 'url', header: t('git.url'), render: (item) => item.baseUrl },
            {
              key: 'repositories',
              header: t('git.repositories'),
              render: (item) => (
                <Link component={RouterLink} to={item.id!} aria-label={`${item.repositoryCount} ${t('git.repositories')} ${item.name}`}>{item.repositoryCount}</Link>
              ),
            },
            {
              key: 'status',
              header: t('git.status'),
              render: (item) => {
                const [name, tone] = STATUS[item.status ?? 0]!;
                return <Badge tone={tone}>{t(`git.statuses.${name}`)}</Badge>;
              },
            },
            { key: 'sync', header: t('git.lastSync'), render: (item) => (item.lastSyncAt ? formatDate(item.lastSyncAt, i18n.language) : '—') },
            {
              key: 'edit',
              header: <span className="sr-only">{t('sso.edit')}</span>,
              render: (item) => (
                <Link component="button" variant="body2" aria-label={`${t('sso.edit')} ${item.name}`} onClick={() => setEditing(item)}>{t('sso.edit')}</Link>
              ),
            },
          ]}
        />
      )}
      <BranchTemplateSection space={space} />
      {adding && <ConnectionDialog projectId={space.id!} connection={null} onClose={() => setAdding(false)} />}
      {editing && <ConnectionDialog projectId={space.id!} connection={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function WebhookSetupDialog({ repository, onClose }: { repository: Repository; onClose: () => void }) {
  const { t } = useTranslation();
  const setup = useQuery({
    queryKey: ['admin', 'git', 'webhook', repository.id],
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-connection/webhook-setup/{repositoryId}', { params: { path: { repositoryId: repository.id! } } })),
  });

  return (
    <Dialog open title={t('git.webhookTitle', { name: repository.fullName })} onClose={onClose} footer={<Button kind="ghost" onClick={onClose}>{t('common.close')}</Button>}>
      <Typography color="textSecondary">{t('git.webhookHint')}</Typography>
      <TextField label={t('git.webhookUrl')} value={setup.data?.url ?? ''} readOnly />
      <TextField label={t('git.webhookSecret')} value={setup.data?.secret ?? ''} readOnly />
    </Dialog>
  );
}

export function GitRepositoriesPage() {
  const { t, i18n } = useTranslation();
  const { connectionId } = useParams();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const refresh = useRefresh();
  const [search, setSearch] = useState('');
  const [setup, setSetup] = useState<Repository | null>(null);
  const id = connectionId!;

  const connection = useQuery({
    queryKey: ['admin', 'git', 'connection', id],
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-connection/{id}', { params: { path: { id } } })),
  });
  const repositories = useQuery({
    queryKey: ['admin', 'git', 'repositories', id, search],
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-connection/{id}/repositories', { params: { path: { id }, query: { search } } })),
  });
  const sync = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/git-connection/{id}/sync', { params: { path: { id } } })),
    onSuccess: () => refresh(),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const access = useMutation({
    mutationFn: (repositoryAccess: 0 | 1) =>
      unwrap(apiClient.POST('/api/v1/git-connection/{id}/set-repository-access', { params: { path: { id } }, body: { repositoryAccess } })),
    onSuccess: () => refresh(),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const select = useMutation({
    mutationFn: ({ repositoryId, isSelected }: { repositoryId: string; isSelected: boolean }) =>
      unwrap(apiClient.POST('/api/v1/git-connection/set-repository-selected/{repositoryId}', { params: { path: { repositoryId } }, body: { isSelected } })),
    onSuccess: () => refresh(),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const problems = connection.data?.webhookProblemCount ?? 0;
  const selectedOnly = connection.data?.repositoryAccess === SELECTED_REPOSITORIES;

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{connection.data?.name}</Typography>
          <Typography variant="body2" color="textSecondary">{t('git.repositoriesHint', { url: connection.data?.baseUrl })}</Typography>
        </div>
        <SearchInput label={t('git.searchRepositories')} value={search} onChange={(event) => setSearch(event.target.value)} />
        <Button kind="secondary" disabled={sync.isPending} onClick={() => sync.mutate()}>{t('git.syncNow')}</Button>
      </div>
      {problems > 0 && <Banner>{t('git.webhookProblems', { count: problems })}</Banner>}
      {connection.data && (
        <div className="flex max-w-md flex-col gap-1">
          <SelectField
            label={t('git.repositoryAccess')}
            value={String(connection.data.repositoryAccess ?? 0)}
            disabled={access.isPending}
            onChange={(event) => access.mutate(Number(event.target.value) as 0 | 1)}
            options={[
              { value: '0', label: t('git.accessAll') },
              { value: String(SELECTED_REPOSITORIES), label: t('git.accessSelected') },
            ]}
          />
          <Typography variant="caption" color="textSecondary">{t(selectedOnly ? 'git.accessSelectedHint' : 'git.accessAllHint')}</Typography>
        </div>
      )}
      {repositories.isPending && <LoadingState />}
      {repositories.isError && <ErrorState onRetry={() => void repositories.refetch()} />}
      {repositories.data?.length === 0 && <EmptyState title={t('git.noRepositories')} />}
      {(repositories.data?.length ?? 0) > 0 && (
        <Table<Repository>
          label={t('git.repositories')}
          rows={repositories.data ?? []}
          rowKey={(item) => item.id!}
          columns={[
            {
              key: 'name',
              header: t('git.repository'),
              render: (item) =>
                selectedOnly ? (
                  <Checkbox
                    label={item.fullName ?? ''}
                    checked={item.isSelected ?? false}
                    disabled={select.isPending}
                    onChange={(event) => select.mutate({ repositoryId: item.id!, isSelected: event.target.checked })}
                  />
                ) : (
                  item.fullName
                ),
            },
            { key: 'visibility', header: t('git.visibility'), render: (item) => t(item.isPrivate ? 'git.private' : 'git.public') },
            { key: 'branch', header: t('git.defaultBranch'), render: (item) => item.defaultBranch },
            {
              key: 'webhook',
              header: t('git.webhook'),
              render: (item) => {
                const [name, tone] = WEBHOOK[item.webhookState ?? 0]!;
                const badge = <Badge tone={tone}>{t(`git.webhooks.${name}`)}</Badge>;
                return item.webhookState === 1 || connection.data?.providerType === GITHUB ? badge : (
                  <div className="flex items-center gap-2">
                    {badge}
                    <Link component="button" variant="body2" aria-label={`${t('git.setUp')} ${item.fullName}`} onClick={() => setSetup(item)}>{t('git.setUp')}</Link>
                  </div>
                );
              },
            },
            { key: 'event', header: t('git.lastEvent'), render: (item) => (item.lastEventAt ? formatDate(item.lastEventAt, i18n.language) : t('git.never')) },
          ]}
        />
      )}
      {setup && <WebhookSetupDialog repository={setup} onClose={() => setSetup(null)} />}
    </div>
  );
}
