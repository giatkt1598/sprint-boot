import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams, useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { StatusBadge } from '../../components/Badge';
import { IssueTypeIcon, PriorityIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { Pagination } from '../../components/Pagination';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { useSpace } from '../spaces/api';
import { FilterChips, FilterControls, useIssueFilters } from './IssueFilters';
import { priorityLevel, statusCategory, type IssueSummary } from './api';

const PAGE_SIZE = 25;

export function IssuesListPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const [params] = useSearchParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  const filters = useIssueFilters(projectId);
  const { values, createdRange, updatedRange } = filters;

  const [text, setText] = useState(params.get('text') ?? '');
  const [skip, setSkip] = useState(0);

  const search = useQuery({
    queryKey: ['issues', 'search', projectId, text, values, filters.created, filters.updated, skip],
    enabled: !spaceKey || Boolean(projectId),
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/v1/issue/search', {
          params: {
            query: {
              ProjectId: projectId,
              Text: text || undefined,
              AssigneeIds: values.assignee,
              ReporterIds: values.reporter,
              TypeIds: values.type,
              StatusIds: values.status,
              PriorityIds: values.priority,
              ParentIds: values.epic,
              Labels: values.label,
              CreatedFrom: createdRange.from,
              CreatedTo: createdRange.to,
              UpdatedFrom: updatedRange.from,
              UpdatedTo: updatedRange.to,
              SkipCount: skip,
              MaxResultCount: PAGE_SIZE,
            },
          },
        }),
      ),
  });

  const filtering = filters.chips.length > 0 || Boolean(text);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t(spaceKey ? 'issues.title' : 'issues.searchResults')}</Typography>
        {spaceKey && <FilterControls filters={filters} onChange={() => setSkip(0)} />}
        <SearchInput label={t('issues.search')} value={text} onChange={(event) => { setText(event.target.value); setSkip(0); }} />
      </div>

      <FilterChips filters={filters} onClear={() => setSkip(0)} />

      {search.isPending && <LoadingState />}
      {search.isError && <ErrorState onRetry={() => void search.refetch()} />}
      {search.data && search.data.totalCount === 0 && (
        <EmptyState title={filtering ? t('issues.noResults') : t('issues.empty')} action={filtering ? <Typography variant="body2" color="textSecondary">{t('issues.noResultsHint')}</Typography> : undefined} />
      )}
      {search.data && (search.data.totalCount ?? 0) > 0 && (
        <>
          <Table<IssueSummary>
            label={t('issues.title')}
            rows={search.data.items ?? []}
            rowKey={(issue) => issue.id!}
            columns={[
              {
                key: 'key',
                header: t('issues.key'),
                render: (issue) => (
                  <span className="flex items-center gap-2">
                    <IssueTypeIcon type={issue.type?.icon as IconType} />
                    <Link component={RouterLink} to={`/issues/${issue.key}`} color="inherit">{issue.key}</Link>
                  </span>
                ),
              },
              {
                key: 'title',
                header: t('issues.titleField'),
                render: (issue) => <Link component={RouterLink} to={`/issues/${issue.key}`} color="inherit">{issue.title}</Link>,
              },
              { key: 'status', header: t('issues.status'), render: (issue) => <StatusBadge status={statusCategory(issue.status)} label={issue.status?.name} /> },
              { key: 'priority', header: t('issues.priority'), render: (issue) => <PriorityIcon priority={priorityLevel(issue.priority)} /> },
              {
                key: 'assignee',
                header: t('issues.assignee'),
                render: (issue) => issue.assignee && <span className="flex items-center gap-2"><Avatar name={issue.assignee.name ?? ''} />{issue.assignee.name}</span>,
              },
              {
                key: 'reporter',
                header: t('issues.reporter'),
                render: (issue) => issue.reporter && <span className="flex items-center gap-2"><Avatar name={issue.reporter.name ?? ''} />{issue.reporter.name}</span>,
              },
              { key: 'sprint', header: t('agile.sprint'), render: (issue) => issue.sprintName },
              { key: 'created', header: t('issues.createdAt'), render: (issue) => formatDate(issue.createdAt, i18n.language) },
              { key: 'updated', header: t('issues.updatedAt'), render: (issue) => formatDate(issue.updatedAt, i18n.language) },
            ]}
          />
          <Pagination skip={skip} pageSize={PAGE_SIZE} total={search.data.totalCount ?? 0} onChange={setSkip} />
        </>
      )}
    </div>
  );
}
