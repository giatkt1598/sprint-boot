import Autocomplete from '@mui/material/Autocomplete';
import ButtonBase from '@mui/material/ButtonBase';
import MuiLink from '@mui/material/Link';
import MuiTextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, GitBranch, GitCommitHorizontal, GitPullRequest } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { ApiError, apiClient, unwrap } from '../../api';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Button } from '../../components/Button';
import { CollapseHeading } from '../../components/CollapseHeading';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { Tabs } from '../../components/Tabs';
import { SelectField, TextField } from '../../components/TextField';
import { useErrorMessage } from '../../lib/errors';
import { formatDateTime } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';

type Repository = components['schemas']['BranchRepositoryDto'];
type Development = components['schemas']['DevelopmentDto'];

const POLL_MILLISECONDS = 5000;
const REFRESH_MILLISECONDS = 60_000;
const SHORT_SHA = 7;
const PULL_STATES: [string, BadgeTone][] = [
  ['open', 'info'],
  ['merged', 'success'],
  ['closed', 'danger'],
];
const BUILD_STATES: [string, BadgeTone][] = [
  ['pending', 'neutral'],
  ['running', 'info'],
  ['success', 'success'],
  ['failure', 'danger'],
];

function CreateBranchDialog({ issueId, label, onClose }: { issueId: string; label: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [repository, setRepository] = useState<Repository | null>(null);
  const [baseBranch, setBaseBranch] = useState('');
  const [name, setName] = useState<string | null>(null);

  const options = useQuery({
    queryKey: ['git', 'branch-options', issueId],
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-issue/branch-options/{issueId}', { params: { path: { issueId } } })),
  });
  const branches = useQuery({
    queryKey: ['git', 'branches', repository?.id],
    enabled: repository !== null,
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/git-issue/branches', { params: { query: { issueId, repositoryId: repository!.id! } } })),
  });

  useEffect(() => {
    const first = options.data?.repositories?.[0];
    if (first) {
      setRepository((current) => current ?? first);
    }
  }, [options.data]);

  useEffect(() => setBaseBranch(repository?.defaultBranch ?? ''), [repository]);

  const branchName = name ?? options.data?.suggestedName ?? '';
  const create = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/v1/git-issue/branch/{issueId}', {
          params: { path: { issueId } },
          body: { repositoryId: repository!.id!, baseBranch, name: branchName },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['git', 'development', issueId] });
      onClose();
    },
  });

  const taken = create.error instanceof ApiError && create.error.code?.endsWith('GitBranchExists');
  const baseOptions = [...new Set([baseBranch, ...(branches.data ?? [])])].filter(Boolean).map((value) => ({ value, label: value }));

  return (
    <Dialog
      open
      title={t('development.createBranch')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('development.createBranch')}
          confirmDisabled={!repository || !baseBranch || !branchName || create.isPending}
          onCancel={onClose}
          onConfirm={() => create.mutate()}
        />
      }
    >
      <Typography color="textSecondary">{label}</Typography>
      <div className="flex flex-col gap-1">
        <label htmlFor="branch-repository" style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
          {t('development.repository')}
        </label>
        <Autocomplete
          id="branch-repository"
          size="small"
          disableClearable={repository !== null}
          options={options.data?.repositories ?? []}
          value={repository as Repository}
          groupBy={(option) => t(option.usedInSpace ? 'development.usedInSpace' : 'development.allRepositories')}
          getOptionLabel={(option) => option.fullName ?? ''}
          isOptionEqualToValue={(option, selected) => option.id === selected.id}
          onChange={(_, next) => setRepository(next)}
          renderOption={(props, option) => (
            <li {...props} key={option.id} className={`${props.className ?? ''} flex justify-between gap-2`}>
              <span>{option.fullName}</span>
              <Typography component="span" variant="body2" color="textSecondary">{option.connectionName}</Typography>
            </li>
          )}
          renderInput={(params) => <MuiTextField {...params} />}
        />
      </div>
      <SelectField label={t('development.baseBranch')} value={baseBranch} onChange={(event) => setBaseBranch(event.target.value)} options={baseOptions} />
      <TextField label={t('development.branchName')} value={branchName} error={taken ? t('errors.GitBranchExists', { repository: repository?.fullName }) : undefined} onChange={(event) => setName(event.target.value)} />
      {create.isError && !taken && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}

type DevelopmentTab = 'branches' | 'commits' | 'pullRequests' | 'builds';

function StateBadge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  return <span style={{ textTransform: 'uppercase' }}><Badge tone={tone}>{children}</Badge></span>;
}

function PullBadge({ state }: { state: number }) {
  const { t } = useTranslation();
  const [name, tone] = PULL_STATES[state]!;
  return <StateBadge tone={tone}>{t(`development.pull.${name}`)}</StateBadge>;
}

function BuildBadge({ state }: { state: number }) {
  const { t } = useTranslation();
  const [name, tone] = BUILD_STATES[state]!;
  return <StateBadge tone={tone}>{t(`development.build.${name}`)}</StateBadge>;
}

function SummaryRow({ icon, label, badge, onClick }: { icon: ReactNode; label: string; badge?: ReactNode; onClick: () => void }) {
  return (
    <ButtonBase
      onClick={onClick}
      className="flex w-full items-center gap-2 px-1"
      sx={{
        justifyContent: 'flex-start',
        minHeight: 32,
        fontSize: font.size.md,
        borderRadius: `${radius.control}px`,
        '&:hover': { backgroundColor: sem('bg-hover') },
      }}
    >
      <span aria-hidden className="flex shrink-0" style={{ color: sem('text-secondary') }}>{icon}</span>
      <span className="min-w-0 flex-1 truncate text-left">{label}</span>
      {badge}
    </ButtonBase>
  );
}

function DetailRow({ children, badge }: { children: ReactNode; badge?: ReactNode }) {
  return (
    <div className="flex items-center gap-3 py-2" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
      <div className="min-w-0 flex-1">{children}</div>
      {badge}
    </div>
  );
}

function DevelopmentDialog({ data, tab, onTab, onClose }: { data: Development; tab: DevelopmentTab; onTab: (tab: DevelopmentTab) => void; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const external = { target: '_blank', rel: 'noreferrer', variant: 'body2' } as const;
  const empty = <Typography variant="body2" color="textSecondary" className="py-2">{t('development.noneInTab')}</Typography>;
  const secondary = (text: string | null | undefined) => <Typography variant="body2" color="textSecondary">{text}</Typography>;

  return (
    <Dialog open width={640} title={t('development.title')} onClose={onClose} footer={<div className="flex justify-end"><Button kind="ghost" onClick={onClose}>{t('common.close')}</Button></div>}>
      <Tabs
        label={t('development.title')}
        value={tab}
        onChange={(next) => onTab(next as DevelopmentTab)}
        items={[
          { value: 'branches', label: t('development.tabBranch') },
          { value: 'commits', label: t('development.tabCommit') },
          { value: 'pullRequests', label: t('development.tabPullRequest') },
          { value: 'builds', label: t('development.tabBuild') },
        ]}
      />
      <div className="flex max-h-96 flex-col overflow-y-auto">
        {tab === 'branches' && (data.branches?.length ? data.branches.map((branch) => (
          <DetailRow key={branch.url}>
            <MuiLink href={branch.url ?? undefined} {...external}>{branch.name}</MuiLink>
            {secondary(branch.repositoryName)}
          </DetailRow>
        )) : empty)}
        {tab === 'commits' && (data.commits?.length ? data.commits.map((commit) => (
          <DetailRow key={commit.sha}>
            <MuiLink href={commit.url ?? undefined} {...external}>{commit.sha?.slice(0, SHORT_SHA)}</MuiLink>
            <Typography component="span" variant="body2"> {commit.message?.split('\n')[0]}</Typography>
            {secondary(`${commit.authorName ?? ''} · ${formatDateTime(commit.committedAt, i18n.language)}`)}
          </DetailRow>
        )) : empty)}
        {tab === 'pullRequests' && (data.pullRequests?.length ? data.pullRequests.map((pull) => (
          <DetailRow key={pull.url} badge={<PullBadge state={pull.state ?? 0} />}>
            <MuiLink href={pull.url ?? undefined} {...external}>#{pull.number} {pull.title}</MuiLink>
          </DetailRow>
        )) : empty)}
        {tab === 'builds' && (data.builds?.length ? data.builds.map((build, index) => (
          <DetailRow key={`${build.name}-${index}`} badge={<BuildBadge state={build.state ?? 0} />}>
            {build.url ? <MuiLink href={build.url} {...external}>{build.name}</MuiLink> : <Typography variant="body2">{build.name}</Typography>}
            {secondary(formatDateTime(build.updatedAt, i18n.language))}
          </DetailRow>
        )) : empty)}
      </div>
    </Dialog>
  );
}

/** The state that matters most: open work, then merged, then closed. */
const headlinePullState = (states: number[]) => [0, 1, 2].find((state) => states.includes(state)) ?? 0;

export function DevelopmentPanel({ issueId, label, canEdit }: { issueId: string; label: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const [creating, setCreating] = useState(false);
  const [tab, setTab] = useState<DevelopmentTab | null>(null);
  const [open, setOpen] = useState(true);

  const development = useQuery({
    queryKey: ['git', 'development', issueId],
    refetchInterval: POLL_MILLISECONDS,
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-issue/development/{issueId}', { params: { path: { issueId } } })),
  });

  // The fallback for webhooks that never arrive; what it finds shows up in the poll above.
  useQuery({
    queryKey: ['git', 'refresh', issueId],
    refetchInterval: REFRESH_MILLISECONDS,
    retry: false,
    queryFn: async () => {
      await unwrap(apiClient.POST('/api/v1/git-issue/refresh/{issueId}', { params: { path: { issueId } } }));
      return null;
    },
  });

  const data = development.data;
  const canCreate = canEdit && data?.canCreateBranch;
  const branches = data?.branches?.length ?? 0;
  const commits = data?.commits?.length ?? 0;
  const pulls = data?.pullRequests ?? [];
  const builds = data?.builds ?? [];
  const isEmpty = branches + commits + pulls.length + builds.length === 0;

  // Nothing to show and nothing to do: no Git connection, or a failed read that must not disturb the page.
  if (isEmpty && !canCreate) {
    return null;
  }

  return (
    <section className="flex shrink-0 flex-col gap-1 p-3" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
      <div className="flex items-center justify-between pb-1">
        <CollapseHeading label={t('development.title')} open={open} onToggle={() => setOpen(!open)} />
        {canCreate && <MuiLink component="button" variant="body2" onClick={() => setCreating(true)}>{t('development.createBranch')}</MuiLink>}
      </div>
      {open && isEmpty && <Typography variant="body2" color="textSecondary">{t('development.empty')}</Typography>}
      {open && branches > 0 && <SummaryRow icon={<GitBranch size={16} strokeWidth={1.5} />} label={t('development.branches', { count: branches })} onClick={() => setTab('branches')} />}
      {open && commits > 0 && <SummaryRow icon={<GitCommitHorizontal size={16} strokeWidth={1.5} />} label={t('development.commits', { count: commits })} onClick={() => setTab('commits')} />}
      {open && pulls.length > 0 && (
        <SummaryRow
          icon={<GitPullRequest size={16} strokeWidth={1.5} />}
          label={t('development.pullRequests', { count: pulls.length })}
          badge={<PullBadge state={headlinePullState(pulls.map((pull) => pull.state ?? 0))} />}
          onClick={() => setTab('pullRequests')}
        />
      )}
      {open && builds.length > 0 && (
        <SummaryRow
          icon={<Check size={16} strokeWidth={1.5} />}
          label={t('development.builds', { count: builds.length })}
          badge={<BuildBadge state={builds[0]!.state ?? 0} />}
          onClick={() => setTab('builds')}
        />
      )}
      {tab && data && <DevelopmentDialog data={data} tab={tab} onTab={setTab} onClose={() => setTab(null)} />}
      {creating && <CreateBranchDialog issueId={issueId} label={label} onClose={() => setCreating(false)} />}
    </section>
  );
}
