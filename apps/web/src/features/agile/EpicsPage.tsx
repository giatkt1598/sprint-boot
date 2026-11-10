import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { CardList, CardMeta, ListCard, SortSelect } from '../../components/CardList';
import { TicketTypeIcon } from '../../components/TicketIcons';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { compareValues, Table, useTableSort, type TableSort } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { useCompact } from '../../lib/responsive';
import { font } from '../../theme/tokens';
import { CreateTicketDialog } from '../tickets/CreateTicketDialog';
import { QuickAssignee, QuickPriority, QuickStatus } from '../tickets/QuickFields';
import { useMembers, useSpace } from '../spaces/api';
import { EpicProgress } from './EpicProgress';

type EpicRow = components['schemas']['EpicRowDto'];

const toQuick = (epic: EpicRow) => ({ ...epic, assignee: epic.lead });

/** What each sortable column compares; every epic is already loaded, so the list sorts in the browser. */
const sortValues: Record<string, (epic: EpicRow) => string | number | null | undefined> = {
  key: (epic) => epic.key,
  title: (epic) => epic.title,
  progress: (epic) => (epic.total ? (epic.done ?? 0) / epic.total : null),
  status: (epic) => `${epic.status?.category ?? 0} ${epic.status?.name ?? ''}`,
  priority: (epic) => epic.priority?.order,
  due: (epic) => epic.dueDate,
  lead: (epic) => epic.lead?.name,
};

function sortEpics(rows: EpicRow[], sort: TableSort | null) {
  const value = sort && sortValues[sort.key];
  if (!sort || !value) {
    return rows;
  }
  return [...rows].sort((a, b) => compareValues(value(a), value(b), sort.direction));
}

export function EpicsPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  const [filter, setFilter] = useState('');
  const [creating, setCreating] = useState(false);
  const { sort, toggle, set: setSort } = useTableSort();
  const compact = useCompact();
  const canEdit = space.data?.permissions?.includes('Ticket.Edit') ?? false;
  const members = useMembers(projectId);
  const people = (members.data ?? []).map((member) => member.user!);

  const epics = useQuery({
    queryKey: ['tickets', 'epics-page', projectId],
    enabled: Boolean(projectId),
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/epics/{projectId}', { params: { path: { projectId: projectId! } } })),
  });

  const rows = sortEpics(
    (epics.data ?? []).filter((epic) => `${epic.key} ${epic.title}`.toLowerCase().includes(filter.toLowerCase())),
    sort,
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center gap-2">
        <Typography variant="h1" className="flex-1 max-sm:basis-full">{t('agile.epics')}</Typography>
        <SearchInput label={t('spaces.filter')} value={filter} onChange={(event) => setFilter(event.target.value)} sx={{ flex: { xs: '1 1 140px', md: '0 0 auto' } }} />
        {canEdit && <Button onClick={() => setCreating(true)}>{t('agile.createEpic')}</Button>}
      </div>
      {compact && (
        <SortSelect
          sort={sort}
          columns={[
            { key: 'key', label: t('tickets.key') },
            { key: 'title', label: t('tickets.titleField') },
            { key: 'progress', label: t('agile.progressColumn') },
            { key: 'status', label: t('tickets.status') },
            { key: 'priority', label: t('tickets.priority') },
            { key: 'due', label: t('tickets.dueDate') },
            { key: 'lead', label: t('agile.lead') },
          ]}
          onChange={setSort}
        />
      )}
      {epics.isPending && <LoadingState />}
      {epics.isError && <ErrorState onRetry={() => void epics.refetch()} />}
      {epics.isSuccess && rows.length === 0 && <EmptyState title={t('agile.noEpics')} />}
      {rows.length > 0 && compact && (
        // Cards on small screens, as on the Tickets list: the table's seven columns cannot fit.
        <CardList label={t('agile.epics')}>
          {rows.map((epic) => (
            <ListCard key={epic.id}>
              <CardMeta>
                <TicketTypeIcon type="epic" />
                <span className="flex-1">{epic.key}</span>
                <QuickStatus ticket={toQuick(epic)} canEdit={canEdit} people={people} />
              </CardMeta>
              <Link component={RouterLink} to={`/browse/${epic.key}`} color="inherit" className="line-clamp-2" sx={{ fontSize: font.size.md, overflowWrap: 'anywhere' }}>
                {epic.title}
              </Link>
              <EpicProgress total={epic.total ?? 0} done={epic.done ?? 0} inProgress={epic.inProgress ?? 0} />
              <CardMeta>
                <QuickPriority ticket={toQuick(epic)} canEdit={canEdit} />
                <span className="min-w-0 flex-1 overflow-hidden"><QuickAssignee ticket={toQuick(epic)} canEdit={canEdit} people={people} showName /></span>
                {epic.dueDate && <span className="shrink-0">{formatDate(epic.dueDate, i18n.language)}</span>}
              </CardMeta>
            </ListCard>
          ))}
        </CardList>
      )}
      {rows.length > 0 && !compact && (
        <Table<EpicRow>
          label={t('agile.epics')}
          rows={rows}
          rowKey={(epic) => epic.id!}
          sort={sort}
          onSort={toggle}
          columns={[
            {
              key: 'key', sortable: true,
              header: t('tickets.key'),
              render: (epic) => (
                <span className="flex items-center gap-2">
                  <TicketTypeIcon type="epic" />
                  <Link component={RouterLink} to={`/browse/${epic.key}`} color="inherit">{epic.key}</Link>
                </span>
              ),
            },
            { key: 'title', sortable: true, header: t('tickets.titleField'), render: (epic) => <Link component={RouterLink} to={`/browse/${epic.key}`} color="inherit">{epic.title}</Link> },
            { key: 'progress', sortable: true, header: t('agile.progressColumn'), render: (epic) => <EpicProgress total={epic.total ?? 0} done={epic.done ?? 0} inProgress={epic.inProgress ?? 0} /> },
            { key: 'status', sortable: true, header: t('tickets.status'), render: (epic) => <QuickStatus ticket={toQuick(epic)} canEdit={canEdit} people={people} /> },
            { key: 'priority', sortable: true, header: t('tickets.priority'), render: (epic) => <QuickPriority ticket={toQuick(epic)} canEdit={canEdit} /> },
            { key: 'due', sortable: true, header: t('tickets.dueDate'), render: (epic) => formatDate(epic.dueDate, i18n.language) },
            { key: 'lead', sortable: true, header: t('agile.lead'), render: (epic) => <QuickAssignee ticket={toQuick(epic)} canEdit={canEdit} people={people} showName /> },
          ]}
        />
      )}
      {creating && <CreateTicketDialog epic onClose={() => setCreating(false)} />}
    </div>
  );
}
