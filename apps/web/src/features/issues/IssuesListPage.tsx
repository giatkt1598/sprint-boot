import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams, useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { Pagination } from '../../components/Pagination';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table, useTableSort } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { useMembers, useSpace } from '../spaces/api';
import { FilterChips, FilterControls, useIssueFilters } from './IssueFilters';
import { QuickAssignee, QuickPriority, QuickStatus } from './QuickFields';
import type { IssueSummary } from './api';

const PAGE_SIZE = 25;

// One line per issue: long titles, names and sprints stop here and end in an ellipsis.
const TITLE_MAX = 480;
const PERSON_MAX = 200;

export function IssuesListPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const [params] = useSearchParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  const filters = useIssueFilters(projectId);
  const members = useMembers(projectId);
  const people = (members.data ?? []).map((member) => member.user!);
  // Search across spaces has no single space to check, so its rows stay read-only.
  const canEdit = space.data?.permissions?.includes('Issue.Edit') ?? false;
  const { values, createdRange, updatedRange } = filters;

  const [text, setText] = useState(params.get('text') ?? '');
  const [skip, setSkip] = useState(0);
  const { sort, toggle } = useTableSort();
  const sorting = sort ? `${sort.key} ${sort.direction}` : undefined;

  const search = useQuery({
    queryKey: ['issues', 'search', projectId, text, values, filters.created, filters.updated, sorting, skip],
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
              Sorting: sorting,
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
            nowrap
            sort={sort}
            onSort={(key) => {
              toggle(key);
              setSkip(0);
            }}
            columns={[
              {
                key: 'key', sortable: true,
                header: t('issues.key'),
                render: (issue) => (
                  <span className="flex items-center gap-2">
                    <IssueTypeIcon type={issue.type?.icon as IconType} />
                    <Link component={RouterLink} to={`/issues/${issue.key}`} color="inherit">{issue.key}</Link>
                  </span>
                ),
              },
              {
                key: 'title', sortable: true,
                header: t('issues.titleField'),
                render: (issue) => (
                  <Link component={RouterLink} to={`/issues/${issue.key}`} color="inherit" title={issue.title ?? undefined} className="block truncate" sx={{ maxWidth: TITLE_MAX }}>
                    {issue.title}
                  </Link>
                ),
              },
              { key: 'status', sortable: true, header: t('issues.status'), render: (issue) => <QuickStatus issue={issue} canEdit={canEdit} people={people} /> },
              { key: 'priority', sortable: true, header: t('issues.priority'), render: (issue) => <QuickPriority issue={issue} canEdit={canEdit} /> },
              {
                key: 'assignee', sortable: true,
                header: t('issues.assignee'),
                render: (issue) => <QuickAssignee issue={issue} canEdit={canEdit} people={people} showName />,
              },
              {
                key: 'reporter', sortable: true,
                header: t('issues.reporter'),
                render: (issue) =>
                  issue.reporter && (
                    <span className="flex items-center gap-2">
                      <span className="flex shrink-0"><Avatar name={issue.reporter.name ?? ''} src={issue.reporter.avatarUrl} /></span>
                      <span title={issue.reporter.name ?? undefined} className="truncate" style={{ maxWidth: PERSON_MAX }}>{issue.reporter.name}</span>
                    </span>
                  ),
              },
              {
                key: 'sprint',
                header: t('agile.sprint'),
                render: (issue) => <span title={issue.sprintName ?? undefined} className="block truncate" style={{ maxWidth: PERSON_MAX }}>{issue.sprintName}</span>,
              },
              { key: 'created', sortable: true, header: t('issues.createdAt'), render: (issue) => formatDate(issue.createdAt, i18n.language) },
              { key: 'updated', sortable: true, header: t('issues.updatedAt'), render: (issue) => formatDate(issue.updatedAt, i18n.language) },
            ]}
          />
          <Pagination skip={skip} pageSize={PAGE_SIZE} total={search.data.totalCount ?? 0} onChange={setSkip} />
        </>
      )}
    </div>
  );
}
