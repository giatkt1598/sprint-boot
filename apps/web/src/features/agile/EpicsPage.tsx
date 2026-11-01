import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { IssueTypeIcon } from '../../components/IssueIcons';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { CreateIssueDialog } from '../issues/CreateIssueDialog';
import { QuickAssignee, QuickPriority, QuickStatus } from '../issues/QuickFields';
import { useMembers, useSpace } from '../spaces/api';
import { EpicProgress } from './EpicProgress';

type EpicRow = components['schemas']['EpicRowDto'];

const toQuick = (epic: EpicRow) => ({ ...epic, assignee: epic.lead });

export function EpicsPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  const [filter, setFilter] = useState('');
  const [creating, setCreating] = useState(false);
  const canEdit = space.data?.permissions?.includes('Issue.Edit') ?? false;
  const members = useMembers(projectId);
  const people = (members.data ?? []).map((member) => member.user!);

  const epics = useQuery({
    queryKey: ['issues', 'epics-page', projectId],
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/epics/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

  const rows = (epics.data ?? []).filter((epic) => `${epic.key} ${epic.title}`.toLowerCase().includes(filter.toLowerCase()));

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t('agile.epics')}</Typography>
        <SearchInput label={t('spaces.filter')} value={filter} onChange={(event) => setFilter(event.target.value)} />
        {canEdit && <Button onClick={() => setCreating(true)}>{t('agile.createEpic')}</Button>}
      </div>
      {epics.isPending && <LoadingState />}
      {epics.isError && <ErrorState onRetry={() => void epics.refetch()} />}
      {epics.isSuccess && rows.length === 0 && <EmptyState title={t('agile.noEpics')} />}
      {rows.length > 0 && (
        <Table<EpicRow>
          label={t('agile.epics')}
          rows={rows}
          rowKey={(epic) => epic.id!}
          columns={[
            {
              key: 'key',
              header: t('issues.key'),
              render: (epic) => (
                <span className="flex items-center gap-2"><IssueTypeIcon type="epic" />{epic.key}</span>
              ),
            },
            { key: 'title', header: t('issues.titleField'), render: (epic) => <Link component={RouterLink} to={`/issues/${epic.key}`} color="inherit">{epic.title}</Link> },
            { key: 'progress', header: t('agile.progressColumn'), render: (epic) => <EpicProgress total={epic.total ?? 0} done={epic.done ?? 0} inProgress={epic.inProgress ?? 0} /> },
            { key: 'status', header: t('issues.status'), render: (epic) => <QuickStatus issue={toQuick(epic)} canEdit={canEdit} people={people} /> },
            { key: 'priority', header: t('issues.priority'), render: (epic) => <QuickPriority issue={toQuick(epic)} canEdit={canEdit} /> },
            { key: 'due', header: t('issues.dueDate'), render: (epic) => formatDate(epic.dueDate, i18n.language) },
            { key: 'lead', header: t('agile.lead'), render: (epic) => <QuickAssignee issue={toQuick(epic)} canEdit={canEdit} people={people} showName /> },
          ]}
        />
      )}
      {creating && <CreateIssueDialog epic onClose={() => setCreating(false)} />}
    </div>
  );
}
