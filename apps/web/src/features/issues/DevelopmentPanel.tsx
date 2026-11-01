import Autocomplete from '@mui/material/Autocomplete';
import Link from '@mui/material/Link';
import MuiTextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, GitBranch, GitCommitHorizontal, GitPullRequest } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { ApiError, apiClient, unwrap } from '../../api';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { SelectField, TextField } from '../../components/TextField';
import { useErrorMessage } from '../../lib/errors';
import { font, radius, sem } from '../../theme/tokens';

type Repository = components['schemas']['BranchRepositoryDto'];

const POLL_MILLISECONDS = 5000;
const SHORT_SHA = 7;
const PULL_STATES: [string, BadgeTone][] = [
  ['open', 'success'],
  ['merged', 'info'],
  ['closed', 'neutral'],
];
const BUILD_STATES: [string, BadgeTone][] = [
  ['pending', 'neutral'],
  ['running', 'info'],
  ['success', 'success'],
  ['failure', 'danger'],
];

function Row({ icon, children, badge }: { icon: ReactNode; children: ReactNode; badge?: ReactNode }) {
  return (
    <div className="flex items-center gap-2" style={{ minHeight: 28 }}>
      <span className="flex shrink-0" style={{ color: sem('text-secondary') }} aria-hidden>{icon}</span>
      <div className="min-w-0 flex-1 truncate">{children}</div>
      {badge}
    </div>
  );
}

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

export function DevelopmentPanel({ issueId, label, canEdit }: { issueId: string; label: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const [creating, setCreating] = useState(false);

  const development = useQuery({
    queryKey: ['git', 'development', issueId],
    refetchInterval: POLL_MILLISECONDS,
    queryFn: () => unwrap(apiClient.GET('/api/v1/git-issue/development/{issueId}', { params: { path: { issueId } } })),
  });
  const data = development.data;
  const canCreate = canEdit && data?.canCreateBranch;
  const isEmpty = !data || ((data.branches?.length ?? 0) + (data.commits?.length ?? 0) + (data.pullRequests?.length ?? 0) === 0 && !data.latestBuild);

  // Nothing to show and nothing to do: no Git connection, or a failed read that must not disturb the page.
  if (isEmpty && !canCreate) {
    return null;
  }

  const build = data?.latestBuild;
  const [buildState, buildTone] = BUILD_STATES[build?.state ?? 0]!;
  const external = { target: '_blank', rel: 'noreferrer', variant: 'body2' } as const;

  return (
    <section className="flex flex-col gap-1 p-3" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
      <div className="flex items-center justify-between pb-1">
        <Typography variant="h3" component="h2">{t('development.title')}</Typography>
        {canCreate && <Link component="button" variant="body2" onClick={() => setCreating(true)}>{t('development.createBranch')}</Link>}
      </div>
      {isEmpty && <Typography variant="body2" color="textSecondary">{t('development.empty')}</Typography>}
      {data?.branches?.map((branch) => (
        <Row key={branch.url} icon={<GitBranch size={16} strokeWidth={1.5} />}>
          <Link href={branch.url ?? undefined} title={branch.repositoryName ?? undefined} {...external}>{branch.name}</Link>
        </Row>
      ))}
      {data?.commits?.map((commit) => (
        <Row key={commit.sha} icon={<GitCommitHorizontal size={16} strokeWidth={1.5} />}>
          <Link href={commit.url ?? undefined} {...external}>{commit.sha?.slice(0, SHORT_SHA)}</Link>
          <Typography component="span" variant="body2"> {commit.message?.split('\n')[0]}</Typography>
        </Row>
      ))}
      {data?.pullRequests?.map((pull) => {
        const [state, tone] = PULL_STATES[pull.state ?? 0]!;
        return (
          <Row key={pull.url} icon={<GitPullRequest size={16} strokeWidth={1.5} />} badge={<Badge tone={tone}>{t(`development.pull.${state}`)}</Badge>}>
            <Link href={pull.url ?? undefined} {...external}>#{pull.number} {pull.title}</Link>
          </Row>
        );
      })}
      {build && (
        <Row icon={<Check size={16} strokeWidth={1.5} />} badge={<Badge tone={buildTone}>{t(`development.build.${buildState}`)}</Badge>}>
          {build.url ? <Link href={build.url} {...external}>{build.name}</Link> : <Typography variant="body2">{build.name}</Typography>}
        </Row>
      )}
      {creating && <CreateBranchDialog issueId={issueId} label={label} onClose={() => setCreating(false)} />}
    </section>
  );
}
