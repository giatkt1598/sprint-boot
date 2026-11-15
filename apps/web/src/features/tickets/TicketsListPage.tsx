import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { CardList, CardMeta, ListCard, SortSelect } from '../../components/CardList';
import { Badge } from '../../components/Badge';
import { TicketTypeIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { Pagination } from '../../components/Pagination';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table, useTableSort } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { useCompact } from '../../lib/responsive';
import { font } from '../../theme/tokens';
import { useBacklog } from '../agile/api';
import { useMembers, useSpace } from '../spaces/api';
import { FilterChips, FilterControls, useTicketFilters, useSearchText } from './TicketFilters';
import { QuickAssignee, QuickPriority, QuickStatus } from './QuickFields';
import type { TicketSummary } from './api';
import { usePageTitle } from '../../lib/pageTitle';

const PAGE_SIZE = 25;

// One line per ticket: long titles, names and sprints stop here and end in an ellipsis.
const TITLE_MAX = 480;
const PERSON_MAX = 200;

export function TicketsListPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  // In a space, the space's name follows from its pages' scope; on its own the page is the search.
  usePageTitle(t(spaceKey ? 'nav.tickets' : 'tickets.searchResults'));
  // The overview links here with the types or statuses a figure counted; the filters read them from the address.
  // Only a Scrum space has sprints to filter by; closed ones stay listed, after the open ones.
  const backlog = useBacklog(projectId);
  const sprintOptions = useMemo(
    () =>
      backlog.data?.type === 0
        ? [
            ...(backlog.data.sprints ?? []).map((item) => ({ value: item.sprint!.id!, label: item.sprint!.name! })),
            ...(backlog.data.closedSprints ?? []).map((sprint) => ({ value: sprint.id!, label: t('agile.closedSprint', { name: sprint.name }) })),
          ]
        : undefined,
    [backlog.data, t],
  );
  const filters = useTicketFilters(projectId, sprintOptions);
  const members = useMembers(projectId);
  const people = (members.data ?? []).map((member) => member.user!);
  // Search across spaces has no single space to check, so its rows stay read-only.
  const canEdit = space.data?.permissions?.includes('Ticket.Edit') ?? false;
  const { values, createdRange, updatedRange } = filters;

  const [text, setText] = useSearchText();
  const [skip, setSkip] = useState(0);
  const { sort, toggle, set: setSort } = useTableSort();
  const compact = useCompact();
  const sorting = sort ? `${sort.key} ${sort.direction}` : undefined;

  const search = useQuery({
    queryKey: ['tickets', 'search', projectId, text, values, filters.created, filters.updated, sorting, skip],
    enabled: !spaceKey || Boolean(projectId),
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/v1/ticket/search', {
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
              SprintIds: values.sprint,
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
      <div className="flex flex-wrap items-center gap-2">
        <Typography variant="h1" className="flex-1 max-sm:basis-full">{t(spaceKey ? 'tickets.title' : 'tickets.searchResults')}</Typography>
        {spaceKey && <FilterControls filters={filters} onChange={() => setSkip(0)} />}
        <SearchInput label={t('tickets.search')} value={text} onChange={(event) => { setText(event.target.value); setSkip(0); }} sx={{ flex: { xs: '1 1 140px', md: '0 0 auto' } }} />
      </div>
      {compact && (
        <SortSelect
          sort={sort}
          columns={[
            { key: 'key', label: t('tickets.key') },
            { key: 'title', label: t('tickets.titleField') },
            { key: 'status', label: t('tickets.status') },
            { key: 'priority', label: t('tickets.priority') },
            { key: 'assignee', label: t('tickets.assignee') },
            { key: 'reporter', label: t('tickets.reporter') },
            { key: 'sprint', label: t('agile.sprint') },
            { key: 'created', label: t('tickets.createdAt') },
            { key: 'updated', label: t('tickets.updatedAt') },
          ]}
          onChange={(key, direction) => {
            setSort(key, direction);
            setSkip(0);
          }}
        />
      )}

      <FilterChips filters={filters} onClear={() => setSkip(0)} />

      {search.isPending && <LoadingState />}
      {search.isError && <ErrorState onRetry={() => void search.refetch()} />}
      {search.data && search.data.totalCount === 0 && (
        <EmptyState title={filtering ? t('tickets.noResults') : t('tickets.empty')} action={filtering ? <Typography variant="body2" color="textSecondary">{t('tickets.noResultsHint')}</Typography> : undefined} />
      )}
      {search.data && (search.data.totalCount ?? 0) > 0 && (
        <>
          {compact ? <TicketCards tickets={search.data.items ?? []} canEdit={canEdit} people={people} /> : (
            <Table<TicketSummary>
              label={t('tickets.title')}
              rows={search.data.items ?? []}
              rowKey={(ticket) => ticket.id!}
              nowrap
              sort={sort}
              onSort={(key) => {
                toggle(key);
                setSkip(0);
              }}
              columns={[
                {
                  key: 'key', sortable: true,
                  header: t('tickets.key'),
                  render: (ticket) => (
                    <span className="flex items-center gap-2">
                      <TicketTypeIcon type={ticket.type?.icon as IconType} />
                      <Link component={RouterLink} to={`/browse/${ticket.key}`} color="inherit">{ticket.key}</Link>
                    </span>
                  ),
                },
                {
                  key: 'title', sortable: true,
                  header: t('tickets.titleField'),
                  render: (ticket) => (
                    <Link component={RouterLink} to={`/browse/${ticket.key}`} color="inherit" title={ticket.title ?? undefined} className="block truncate" sx={{ maxWidth: TITLE_MAX }}>
                      {ticket.title}
                    </Link>
                  ),
                },
                { key: 'status', sortable: true, header: t('tickets.status'), render: (ticket) => <QuickStatus ticket={ticket} canEdit={canEdit} people={people} /> },
                { key: 'priority', sortable: true, header: t('tickets.priority'), render: (ticket) => <QuickPriority ticket={ticket} canEdit={canEdit} /> },
                {
                  key: 'assignee', sortable: true,
                  header: t('tickets.assignee'),
                  render: (ticket) => <QuickAssignee ticket={ticket} canEdit={canEdit} people={people} showName />,
                },
                {
                  key: 'reporter', sortable: true,
                  header: t('tickets.reporter'),
                  render: (ticket) =>
                    ticket.reporter && (
                      <span className="flex items-center gap-2">
                        <span className="flex shrink-0"><Avatar name={ticket.reporter.name ?? ''} src={ticket.reporter.avatarUrl} /></span>
                        <span title={ticket.reporter.name ?? undefined} className="truncate" style={{ maxWidth: PERSON_MAX }}>{ticket.reporter.name}</span>
                      </span>
                    ),
                },
                {
                  key: 'epic',
                  header: t('tickets.epic'),
                  render: (ticket) =>
                    ticket.epicTitle && (
                      <span title={ticket.epicTitle} className="block truncate" style={{ maxWidth: PERSON_MAX }}>
                        <Badge tone="epic">{ticket.epicTitle}</Badge>
                      </span>
                    ),
                },
                {
                  key: 'sprint', sortable: true,
                  header: t('agile.sprint'),
                  render: (ticket) => <span title={ticket.sprintName ?? undefined} className="block truncate" style={{ maxWidth: PERSON_MAX }}>{ticket.sprintName}</span>,
                },
                { key: 'created', sortable: true, header: t('tickets.createdAt'), render: (ticket) => formatDate(ticket.createdAt, i18n.language) },
                { key: 'updated', sortable: true, header: t('tickets.updatedAt'), render: (ticket) => formatDate(ticket.updatedAt, i18n.language) },
              ]}
            />
          )}
          <Pagination skip={skip} pageSize={PAGE_SIZE} total={search.data.totalCount ?? 0} onChange={setSkip} />
        </>
      )}
    </div>
  );
}

/** Tickets as cards on small screens, where the table's ten columns cannot fit. */
function TicketCards({ tickets, canEdit, people }: { tickets: TicketSummary[]; canEdit: boolean; people: Parameters<typeof QuickAssignee>[0]['people'] }) {
  const { t, i18n } = useTranslation();

  return (
    <CardList label={t('tickets.title')}>
      {tickets.map((ticket) => (
        <ListCard key={ticket.id}>
          <CardMeta>
            <TicketTypeIcon type={ticket.type?.icon as IconType} />
            <span className="flex-1">{ticket.key}</span>
            <QuickStatus ticket={ticket} canEdit={canEdit} people={people} />
          </CardMeta>
          <Link component={RouterLink} to={`/browse/${ticket.key}`} color="inherit" className="line-clamp-2" sx={{ fontSize: font.size.md, overflowWrap: 'anywhere' }}>
            {ticket.title}
          </Link>
          <CardMeta>
            <QuickPriority ticket={ticket} canEdit={canEdit} />
            <span className="min-w-0 flex-1 overflow-hidden"><QuickAssignee ticket={ticket} canEdit={canEdit} people={people} showName /></span>
            <span className="shrink-0">{formatDate(ticket.updatedAt, i18n.language)}</span>
          </CardMeta>
        </ListCard>
      ))}
    </CardList>
  );
}
