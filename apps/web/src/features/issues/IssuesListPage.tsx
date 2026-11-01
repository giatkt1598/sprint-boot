import ButtonBase from '@mui/material/ButtonBase';
import Chip from '@mui/material/Chip';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ListFilter } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams, useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { StatusBadge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { IssueTypeIcon, PriorityIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { Pagination } from '../../components/Pagination';
import { Popover } from '../../components/Popover';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { useMembers, useSpace } from '../spaces/api';
import { priorityLevel, statusCategory, useEpics, useIssueTypes, useLabels, usePriorities, useStatuses, type IssueSummary } from './api';
import { DatePicker } from '../../components/DatePicker';

const PAGE_SIZE = 25;
const MAX_AVATARS = 5;

type ValueFilter = 'assignee' | 'reporter' | 'type' | 'status' | 'priority' | 'epic' | 'label';
type DateFilter = 'created' | 'updated';
type DatePreset = 'any' | 'today' | 'last7' | 'last30' | 'custom';

interface DateRange {
  preset: DatePreset;
  from: string;
  to: string;
}

const ANY_TIME: DateRange = { preset: 'any', from: '', to: '' };
const NO_VALUES: Record<ValueFilter, string[]> = { assignee: [], reporter: [], type: [], status: [], priority: [], epic: [], label: [] };

function rangeToQuery({ preset, from, to }: DateRange) {
  if (preset === 'any') {
    return {};
  }
  if (preset === 'custom') {
    return { from: from || undefined, to: to ? `${to}T23:59:59Z` : undefined };
  }
  const days = { today: 0, last7: 7, last30: 30 }[preset];
  const start = new Date();
  start.setDate(start.getDate() - days);
  start.setHours(0, 0, 0, 0);
  return { from: start.toISOString() };
}

export function IssuesListPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const [params] = useSearchParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;

  const [text, setText] = useState(params.get('text') ?? '');
  const [skip, setSkip] = useState(0);
  const [values, setValues] = useState(NO_VALUES);
  const [created, setCreated] = useState(ANY_TIME);
  const [updated, setUpdated] = useState(ANY_TIME);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [activeFilter, setActiveFilter] = useState<ValueFilter | DateFilter>('assignee');
  const [showMoreAssignees, setShowMoreAssignees] = useState<HTMLElement | null>(null);

  const members = useMembers(projectId);
  const types = useIssueTypes(projectId);
  const statuses = useStatuses();
  const priorities = usePriorities();
  const labels = useLabels(projectId);
  const epics = useEpics(projectId);
  const people = (members.data ?? []).map((member) => member.user!);

  const createdRange = rangeToQuery(created);
  const updatedRange = rangeToQuery(updated);
  const search = useQuery({
    queryKey: ['issues', 'search', projectId, text, values, created, updated, skip],
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

  const toggle = (filter: ValueFilter, value: string) => {
    setSkip(0);
    setValues((current) => ({
      ...current,
      [filter]: current[filter].includes(value)
        ? current[filter].filter((item) => item !== value)
        : [...current[filter], value],
    }));
  };

  const options: Record<ValueFilter, { value: string; label: string }[]> = {
    assignee: people.map((user) => ({ value: user.id!, label: user.name! })),
    reporter: people.map((user) => ({ value: user.id!, label: user.name! })),
    type: (types.data ?? []).map((type) => ({ value: type.id!, label: t(`issue.type.${type.icon}`) })),
    status: (statuses.data ?? []).map((status) => ({ value: status.id!, label: t(`issue.status.${statusCategory(status)}`) })),
    priority: (priorities.data ?? []).map((priority) => ({ value: priority.id!, label: priority.name! })),
    epic: (epics.data?.items ?? []).map((epic) => ({ value: epic.id!, label: `${epic.key} ${epic.title}` })),
    label: (labels.data ?? []).map((label) => ({ value: label, label })),
  };

  const filterLabels: Record<ValueFilter | DateFilter, string> = {
    assignee: t('issues.assignee'),
    reporter: t('issues.reporter'),
    type: t('issues.issueType'),
    status: t('issues.status'),
    priority: t('issues.priority'),
    epic: t('issues.epic'),
    label: t('issues.labels'),
    created: t('issues.createdAt'),
    updated: t('issues.updatedAt'),
  };

  const chips = [
    ...(Object.keys(values) as ValueFilter[]).flatMap((filter) =>
      values[filter].map((value) => ({
        key: `${filter}:${value}`,
        label: `${filterLabels[filter]}: ${options[filter].find((option) => option.value === value)?.label ?? value}`,
        remove: () => toggle(filter, value),
      })),
    ),
    ...(created.preset !== 'any' ? [{ key: 'created', label: filterLabels.created, remove: () => setCreated(ANY_TIME) }] : []),
    ...(updated.preset !== 'any' ? [{ key: 'updated', label: filterLabels.updated, remove: () => setUpdated(ANY_TIME) }] : []),
  ];

  const clearAll = () => {
    setValues(NO_VALUES);
    setCreated(ANY_TIME);
    setUpdated(ANY_TIME);
    setSkip(0);
  };

  const dateRange = activeFilter === 'created' ? created : updated;
  const setDateRange = activeFilter === 'created' ? setCreated : setUpdated;
  const visibleAvatars = people.slice(0, MAX_AVATARS);
  const hiddenPeople = people.slice(MAX_AVATARS);
  const hiddenSelected = hiddenPeople.some((user) => values.assignee.includes(user.id!));
  const filtering = chips.length > 0 || Boolean(text);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t(spaceKey ? 'issues.title' : 'issues.searchResults')}</Typography>
        {spaceKey && (
          <div className="flex items-center gap-1">
            {visibleAvatars.map((user) => {
              const selected = values.assignee.includes(user.id!);
              return (
                <ButtonBase
                  key={user.id}
                  aria-label={`${t('issues.assignee')}: ${user.name}`}
                  aria-pressed={selected}
                  onClick={() => toggle('assignee', user.id!)}
                  sx={{ borderRadius: '50%', outline: selected ? `2px solid ${sem('border-focus')}` : 'none' }}
                >
                  <Avatar name={user.name ?? ''} />
                </ButtonBase>
              );
            })}
            {hiddenPeople.length > 0 && (
              <ButtonBase
                aria-label={t('issues.moreAssignees', { count: hiddenPeople.length })}
                onClick={(event) => setShowMoreAssignees(event.currentTarget)}
                sx={{
                  width: 24,
                  height: 24,
                  borderRadius: '50%',
                  fontSize: font.size.xs,
                  backgroundColor: sem('bg-subtle'),
                  outline: hiddenSelected ? `2px solid ${sem('border-focus')}` : 'none',
                }}
              >
                +{hiddenPeople.length}
              </ButtonBase>
            )}
            <Popover width={200} open={Boolean(showMoreAssignees)} anchorEl={showMoreAssignees} onClose={() => setShowMoreAssignees(null)}>
              {hiddenPeople.map((user) => (
                <Checkbox key={user.id} label={user.name ?? ''} checked={values.assignee.includes(user.id!)} onChange={() => toggle('assignee', user.id!)} />
              ))}
            </Popover>
          </div>
        )}
        {spaceKey && (
          <Button kind="secondary" startIcon={<ListFilter size={16} strokeWidth={1.5} aria-hidden />} onClick={(event) => setAnchor(event.currentTarget)}>
            {chips.length ? t('issues.filterCount', { count: chips.length }) : t('issues.filter')}
          </Button>
        )}
        <SearchInput label={t('issues.search')} value={text} onChange={(event) => { setText(event.target.value); setSkip(0); }} />
      </div>

      <Popover width={508} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <div className="flex gap-2">
          <div className="flex shrink-0 flex-col gap-1" style={{ width: 160 }}>
            {(Object.keys(filterLabels) as (ValueFilter | DateFilter)[]).map((filter) => (
              <ButtonBase
                key={filter}
                className="h-8 px-2"
                onClick={() => setActiveFilter(filter)}
                sx={{
                  justifyContent: 'flex-start',
                  borderRadius: `${radius.control}px`,
                  fontSize: font.size.md,
                  backgroundColor: activeFilter === filter ? sem('bg-selected') : 'transparent',
                  color: sem(activeFilter === filter ? 'text-accent' : 'text-primary'),
                }}
              >
                {filterLabels[filter]}
              </ButtonBase>
            ))}
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            {activeFilter === 'created' || activeFilter === 'updated' ? (
              <>
                {(['any', 'today', 'last7', 'last30', 'custom'] as const).map((preset) => (
                  <Checkbox
                    key={preset}
                    label={t({ any: 'issues.anyTime', today: 'issues.today', last7: 'issues.last7', last30: 'issues.last30', custom: 'issues.customRange' }[preset])}
                    checked={dateRange.preset === preset}
                    onChange={() => { setDateRange({ ...dateRange, preset }); setSkip(0); }}
                  />
                ))}
                {dateRange.preset === 'custom' && (
                  <div className="grid grid-cols-2 gap-2">
                    <DatePicker label={t('issues.from')} value={dateRange.from} onChange={(from) => setDateRange({ ...dateRange, from })} />
                    <DatePicker label={t('issues.to')} value={dateRange.to} onChange={(to) => setDateRange({ ...dateRange, to })} />
                  </div>
                )}
              </>
            ) : (
              options[activeFilter].map((option) => (
                <Checkbox key={option.value} label={option.label} checked={values[activeFilter].includes(option.value)} onChange={() => toggle(activeFilter, option.value)} />
              ))
            )}
          </div>
        </div>
      </Popover>

      {chips.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          {chips.map((chip) => (
            <Chip key={chip.key} size="small" label={chip.label} onDelete={chip.remove} />
          ))}
          <Button kind="ghost" onClick={clearAll}>{t('issues.clearAll')}</Button>
        </div>
      )}

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
              { key: 'title', header: t('issues.titleField'), render: (issue) => issue.title },
              { key: 'status', header: t('issues.status'), render: (issue) => <StatusBadge status={statusCategory(issue.status)} /> },
              { key: 'priority', header: t('issues.priority'), render: (issue) => <PriorityIcon priority={priorityLevel(issue.priority)} /> },
              {
                key: 'assignee',
                header: t('issues.assignee'),
                render: (issue) => issue.assignee && <span className="flex items-center gap-2"><Avatar name={issue.assignee.name ?? ''} />{issue.assignee.name}</span>,
              },
              { key: 'updated', header: t('issues.updatedAt'), render: (issue) => formatDate(issue.updatedAt, i18n.language) },
            ]}
          />
          <Pagination skip={skip} pageSize={PAGE_SIZE} total={search.data.totalCount ?? 0} onChange={setSkip} />
        </>
      )}
    </div>
  );
}
