import { DndContext, KeyboardSensor, PointerSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core';
import Box from '@mui/material/Box';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ChevronDown, ChevronRight, Ellipsis, Pencil, Trash2 } from 'lucide-react';
import { createContext, memo, use, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { DialogConfirm } from '../../components/Dialog';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { MenuItem } from '../../components/Menu';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { useToast } from '../../components/ToastHost';
import type { PickerUser } from '../../components/UserPicker';
import { swallowClickAfterDrag } from '../../lib/drag';
import { useErrorMessage } from '../../lib/errors';
import { font, radius, sem, shadow, type SemanticToken } from '../../theme/tokens';
import { FilterChips, FilterControls, useIssueFilters } from '../issues/IssueFilters';
import { QuickAssignee, QuickPriority, QuickStatus } from '../issues/QuickFields';
import { useMembers, useSpace } from '../spaces/api';
import { IssuePanel } from './IssuePanel';
import { CompleteSprintDialog, SprintDialog } from './SprintDialogs';
import { backlogKey, useBacklog, useBoardLive, type Backlog, type Card, type SprintSummary } from './api';

const BACKLOG_LIST = 'backlog';

/** Closed sprints reuse the row outside the provider, where nothing may change. */
const RowEditing = createContext<{ canEdit: boolean; people: PickerUser[] }>({ canEdit: false, people: [] });

interface RowActions {
  onMove: (card: Card, listId: string) => void;
  onNudge: (card: Card, delta: number) => void;
  lists: { id: string; name: string }[];
}

interface IssueRowProps {
  card: Card;
  onOpen: (key: string) => void;
  actions?: RowActions;
}

/** One backlog line. Drag is pointer only; the row menu offers the same moves to keyboards. */
function RowBody({ card, onOpen, actions }: IssueRowProps) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const { canEdit, people } = use(RowEditing);

  const run = (action: () => void) => {
    setAnchor(null);
    action();
  };

  return (
    <>
      <td className="pl-2"><span className="flex"><IssueTypeIcon type={card.type?.icon as IconType} /></span></td>
      <td><Link
        component={RouterLink}
        to={`/issues/${card.key}`}
        draggable={false}
        sx={{ width: 96, flexShrink: 0, fontSize: font.size.xs, color: sem('text-accent') }}
      >
        {card.key}
      </Link></td>
      <td className="pr-3">
        <ButtonBase onClick={() => onOpen(card.key!)} className="w-full min-w-0" sx={{ justifyContent: 'flex-start', fontSize: font.size.md, textAlign: 'left' }}>
          <span className="min-w-0 truncate">{card.title}</span>
        </ButtonBase>
      </td>
      <td className="truncate pr-3">{card.epicTitle && <Badge tone="epic">{card.epicTitle}</Badge>}</td>
      <td><QuickStatus issue={card} canEdit={canEdit} people={people} /></td>
      <td className="pr-1">{card.storyPoints != null && (
        <Tooltip title={t('agile.storyPoints', { count: card.storyPoints })}>
          <span className="inline-flex">
            <Badge tone="subtle">{card.storyPoints}</Badge>
          </span>
        </Tooltip>
      )}</td>
      <td><QuickPriority issue={card} canEdit={canEdit} /></td>
      <td className="overflow-hidden py-1 pr-3"><QuickAssignee issue={card} canEdit={canEdit} people={people} showName /></td>
      <td>{actions && (
        <>
          <IconButton size="small" aria-label={t('agile.rowActions', { key: card.key })} onClick={(event) => setAnchor(event.currentTarget)}>
            <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
          <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
            <MenuItem onClick={() => run(() => actions.onNudge(card, -1))}>{t('agile.moveUp')}</MenuItem>
            <MenuItem onClick={() => run(() => actions.onNudge(card, 1))}>{t('agile.moveDown')}</MenuItem>
            {actions.lists.map((list) => (
              <MenuItem key={list.id} onClick={() => run(() => actions.onMove(card, list.id))}>
                {t('agile.moveToList', { name: list.name })}
              </MenuItem>
            ))}
          </Menu>
        </>
      )}</td>
    </>
  );
}

const RowCells = memo(RowBody);

const IssueColumns: { width?: number }[] = [{ width: 32 }, { width: 96 }, {}, { width: 144 }, { width: 120 }, { width: 48 }, { width: 40 }, { width: 248 }, { width: 40 }];

/** Fixed columns, so sprints and the backlog line up with each other. */
function IssueTable({ extraColumn, children }: { extraColumn?: boolean; children: ReactNode }) {
  return (
    <table className="w-full" style={{ tableLayout: 'fixed', borderCollapse: 'collapse' }}>
      <colgroup>
        {IssueColumns.map((column, index) => <col key={index} style={{ width: column.width }} />)}
        {extraColumn && <col style={{ width: 128 }} />}
      </colgroup>
      <tbody>{children}</tbody>
    </table>
  );
}

/** Rows that only open their issue, for lists outside the backlog such as a closed sprint. */
export function StaticIssueList({ cards, onOpen }: { cards: Card[]; onOpen: (key: string) => void }) {
  return (
    <div style={{ borderTop: `1px solid ${sem('border-default')}` }}>
      <IssueTable>
        {cards.map((card) => <Box key={card.id} component="tr" sx={rowStyles}><RowCells card={card} onOpen={onOpen} /></Box>)}
      </IssueTable>
    </div>
  );
}

/** Neighbouring rows share a line; the list draws the one above the first row. */
const rowStyles = {
  height: 36,
  // One line per issue: whatever does not fit its column is cut off, never wrapped.
  '& > td': { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  backgroundColor: sem('bg-surface'),
  borderBottom: `1px solid ${sem('border-default')}`,
  '&:hover': { backgroundColor: sem('bg-hover') },
};

export function SectionCard({ children }: { children: ReactNode }) {
  return (
    <section
      className="flex flex-col gap-2 p-3"
      style={{ backgroundColor: sem('bg-surface'), borderRadius: radius.card, boxShadow: shadow.card }}
    >
      {children}
    </section>
  );
}

function DraggableRow({ card, onOpen, actions, canDrag }: IssueRowProps & { canDrag: boolean }) {
  const { listeners, setNodeRef: dragRef, transform, isDragging } = useDraggable({ id: card.id!, disabled: !canDrag });
  const { setNodeRef: dropRef, isOver } = useDroppable({ id: `row:${card.id}` });
  return (
    <Box
      component="tr"
      ref={(node: HTMLTableRowElement | null) => {
        dragRef(node);
        dropRef(node);
      }}
      data-testid={`row-${card.key}`}
      {...listeners}
      sx={[rowStyles, { '& td': { boxShadow: isOver ? `inset 0 2px 0 ${sem('border-focus')}` : undefined } }]}
      style={{
        opacity: isDragging ? 0.5 : 1,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined,
        cursor: canDrag ? 'grab' : undefined,
      }}
    >
      <RowCells card={card} onOpen={onOpen} actions={actions} />
    </Box>
  );
}

interface ListProps {
  id: string;
  cards: Card[];
  canDrag: boolean;
  onOpen: (key: string) => void;
  actions?: RowActions;
  empty: string;
}

function IssueList({ id, cards, canDrag, onOpen, actions, empty }: ListProps) {
  const { setNodeRef, isOver } = useDroppable({ id: `list:${id}` });

  return (
    <div
      ref={setNodeRef}
      data-testid={`list-${id}`}
      style={{ minHeight: 40, borderTop: `1px solid ${sem('border-default')}`, backgroundColor: isOver ? sem('bg-selected') : undefined }}
    >
      {cards.length > 0 && (
        <IssueTable>
          {cards.map((card) => (
            <DraggableRow key={card.id} card={card} canDrag={canDrag} onOpen={onOpen} actions={actions} />
          ))}
        </IssueTable>
      )}
      {cards.length === 0 && (
        <Typography variant="body2" color="textSecondary" className="px-2 py-2">{empty}</Typography>
      )}
    </div>
  );
}

const DAY_MILLISECONDS = 86_400_000;
const WEEK_DAYS = 7;

/** Both dates count, so Monday to the Sunday after next is 2 weeks. */
function sprintLength(start: string, end: string, t: (key: string, options: { count: number }) => string) {
  const days = (Date.parse(end) - Date.parse(start)) / DAY_MILLISECONDS + 1;
  return days % WEEK_DAYS === 0 ? t('agile.lengthWeeks', { count: days / WEEK_DAYS }) : t('agile.lengthDays', { count: days });
}

const STATUS_GROUPS: { category: number; label: string; background: SemanticToken; color: SemanticToken }[] = [
  { category: 0, label: 'agile.countToDo', background: 'status-todo-bg', color: 'status-todo-text' },
  { category: 1, label: 'agile.countInProgress', background: 'status-progress-bg', color: 'status-progress-text' },
  { category: 2, label: 'agile.countDone', background: 'status-done-bg', color: 'status-done-text' },
];

function StatusCounters({ issues }: { issues: Card[] }) {
  const { t } = useTranslation();

  return (
    <div className="flex items-center gap-1">
      {STATUS_GROUPS.map((group) => {
        const count = issues.filter((issue) => issue.status?.category === group.category).length;
        return (
          <Tooltip key={group.category} title={t(group.label)}>
            <span
              role="img"
              aria-label={`${t(group.label)}: ${count}`}
              className="inline-flex h-6 items-center justify-center px-2"
              style={{
                minWidth: 24,
                fontSize: font.size.xs,
                fontWeight: font.weight.medium,
                backgroundColor: sem(group.background),
                color: sem(group.color),
                borderRadius: radius.control,
              }}
            >
              {count}
            </span>
          </Tooltip>
        );
      })}
    </div>
  );
}

function SprintHeader({ sprint, issues, canManage, shown, futureSprints, projectId }: { sprint: SprintSummary; issues: Card[]; canManage: boolean; shown: number; futureSprints: SprintSummary[]; projectId: string }) {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [dialog, setDialog] = useState<'edit' | 'start' | 'complete' | 'delete' | null>(null);
  const active = sprint.state === 1;

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/sprint/{id}', { params: { path: { id: sprint.id! } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['issues'] }),
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setDialog(null),
  });

  const date = (value?: string | null) =>
    value ? new Intl.DateTimeFormat(i18n.language, { month: 'short', day: 'numeric' }).format(new Date(`${value}T00:00:00`)) : '';
  const range = sprint.startDate && sprint.endDate ? `${date(sprint.startDate)} – ${date(sprint.endDate)}` : '';
  const total = sprint.issueCount ?? 0;
  const length = sprint.startDate && sprint.endDate ? sprintLength(sprint.startDate, sprint.endDate, t) : '';
  const count = shown === total ? t('agile.issueCount', { count: total }) : t('agile.issueCountFiltered', { shown, total });
  const points = t('agile.pointsProgress', { done: sprint.donePoints, total: sprint.totalPoints });

  return (
    <div className="flex items-center gap-2 px-2">
      <Typography variant="h3" component="h2">{sprint.name}</Typography>
      <Typography variant="body2" color="textSecondary" className="flex-1">{[range, length, count, points].filter(Boolean).join(' · ')}</Typography>
      <StatusCounters issues={issues} />
      {canManage && active && <Button kind="ghost" onClick={() => setDialog('complete')}>{t('agile.completeSprint')}</Button>}
      {canManage && !active && <Button kind="ghost" onClick={() => setDialog('start')}>{t('agile.startSprint')}</Button>}
      {canManage && (
        <>
          <IconButton size="small" aria-label={t('agile.sprintActions', { name: sprint.name })} onClick={(event) => setAnchor(event.currentTarget)}>
            <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
          <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
            <MenuItem icon={Pencil} onClick={() => { setAnchor(null); setDialog('edit'); }}>{t('common.edit')}</MenuItem>
            {!active && <MenuItem icon={Trash2} danger onClick={() => { setAnchor(null); setDialog('delete'); }}>{t('common.delete')}</MenuItem>}
          </Menu>
        </>
      )}
      {(dialog === 'edit' || dialog === 'start') && <SprintDialog mode={dialog} projectId={projectId} sprint={sprint} onClose={() => setDialog(null)} />}
      {dialog === 'complete' && <CompleteSprintDialog sprint={sprint} futureSprints={futureSprints} onClose={() => setDialog(null)} />}
      <DialogConfirm
        open={dialog === 'delete'}
        title={t('agile.deleteSprintTitle', { name: sprint.name })}
        description={t('agile.deleteSprintBody', { count: total })}
        confirmLabel={t('common.delete')}
        onCancel={() => setDialog(null)}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}

/** Applies a move to the cached backlog so the row lands immediately. */
function moveInCache(data: Backlog, card: Card, listId: string, index: number): Backlog {
  const without = (cards: Card[] | null | undefined) => (cards ?? []).filter((item) => item.id !== card.id);
  const insert = (cards: Card[]) => [...cards.slice(0, index), card, ...cards.slice(index)];

  return {
    ...data,
    backlog: listId === BACKLOG_LIST ? insert(without(data.backlog)) : without(data.backlog),
    sprints: (data.sprints ?? []).map((item) => ({
      ...item,
      issues: item.sprint?.id === listId ? insert(without(item.issues)) : without(item.issues),
    })),
  };
}

export function BacklogPage() {
  const { t } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  const backlog = useBacklog(projectId);
  useBoardLive(projectId);

  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canRank = space.data?.permissions?.includes('Backlog.Rank') ?? false;
  const canManage = space.data?.permissions?.includes('Sprint.Manage') ?? false;
  const scrum = backlog.data?.type === 0;
  const members = useMembers(projectId);
  const canEdit = space.data?.permissions?.includes('Issue.Edit') ?? false;
  const rowEditing = useMemo(
    () => ({ canEdit, people: (members.data ?? []).map((member) => member.user!) }),
    [canEdit, members.data],
  );

  const sprintOptions = useMemo(
    () => (backlog.data?.sprints ?? []).map((item) => ({ value: item.sprint!.id!, label: item.sprint!.name! })),
    [backlog.data],
  );
  const filters = useIssueFilters(projectId, scrum ? sprintOptions : undefined);
  const [text, setText] = useState('');
  const [openIssue, setOpenIssue] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [closedOpen, setClosedOpen] = useState(false);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor),
  );

  const key = backlogKey(projectId);
  const refresh = () => queryClient.invalidateQueries({ queryKey: key });

  const move = useMutation({
    mutationFn: ({ card, listId, after, before }: { card: Card; listId: string; after?: string; before?: string }) =>
      unwrap(
        apiClient.POST('/api/v1/sprint/move-issue/{issueId}', {
          params: { path: { issueId: card.id! } },
          body: { sprintId: listId === BACKLOG_LIST ? null : listId, afterIssueId: after ?? null, beforeIssueId: before ?? null },
        }),
      ),
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: refresh,
  });

  const pull = useMutation({
    mutationFn: (card: Card) =>
      unwrap(apiClient.POST('/api/v1/board/set-entry', { params: { query: { projectId, issueId: card.id } }, body: { onBoard: true } })),
    onSuccess: () => toast('success', t('agile.movedToBoard')),
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: refresh,
  });

  const cardsOf = (listId: string) =>
    listId === BACKLOG_LIST
      ? (backlog.data?.backlog ?? [])
      : (backlog.data?.sprints?.find((item) => item.sprint?.id === listId)?.issues ?? []);

  const place = (card: Card, listId: string, index: number) => {
    const others = cardsOf(listId).filter((item) => item.id !== card.id);
    const after = others[index - 1]?.id;
    const before = others[index]?.id;
    queryClient.setQueryData<Backlog>(key, (data) => (data ? moveInCache(data, card, listId, index) : data));
    move.mutate({ card, listId, after, before });
  };

  const listOf = (card: Card) =>
    backlog.data?.sprints?.find((item) => item.issues?.some((issue) => issue.id === card.id))?.sprint?.id ?? BACKLOG_LIST;

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over) {
      return;
    }
    const all = [...(backlog.data?.backlog ?? []), ...(backlog.data?.sprints ?? []).flatMap((item) => item.issues ?? [])];
    const card = all.find((item) => item.id === active.id);
    if (!card) {
      return;
    }

    const target = String(over.id);
    if (target.startsWith('list:')) {
      const listId = target.slice(5);
      if (listId !== listOf(card)) {
        place(card, listId, cardsOf(listId).length);
      }
      return;
    }

    const targetId = target.slice(4);
    if (targetId === card.id) {
      return;
    }
    const targetCard = all.find((item) => item.id === targetId)!;
    const listId = listOf(targetCard);
    const others = cardsOf(listId).filter((item) => item.id !== card.id);
    place(card, listId, others.findIndex((item) => item.id === targetId));
  };

  const lists = [
    ...(backlog.data?.sprints ?? []).map((item) => ({ id: item.sprint!.id!, name: item.sprint!.name! })),
    { id: BACKLOG_LIST, name: t('agile.backlog') },
  ];

  const actions: RowActions | undefined =
    canRank && scrum
      ? {
          lists,
          onMove: (card, listId) => place(card, listId, cardsOf(listId).length),
          onNudge: (card, delta) => {
            const listId = listOf(card);
            const index = cardsOf(listId).findIndex((item) => item.id === card.id);
            place(card, listId, Math.max(0, index + delta));
          },
        }
      : undefined;

  const visible = (cards: Card[] | null | undefined) =>
    (cards ?? []).filter((card) => filters.matches(card) && (card.title ?? '').toLowerCase().includes(text.toLowerCase()));
  const futureSprints = (backlog.data?.sprints ?? []).map((item) => item.sprint!).filter((sprint) => sprint.state === 0);
  const backlogCards = visible(backlog.data?.backlog);
  const backlogTotal = backlog.data?.backlog?.length ?? 0;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{t('agile.backlog')}</Typography>
        <FilterControls filters={filters} />
        <SearchInput label={t('issues.search')} value={text} onChange={(event) => setText(event.target.value)} />
        {scrum && canManage && <Button kind="secondary" onClick={() => setCreating(true)}>{t('agile.createSprint')}</Button>}
      </div>
      <FilterChips filters={filters} />

      {backlog.isPending && <LoadingState />}
      {backlog.isError && <ErrorState onRetry={() => void backlog.refetch()} />}
      {backlog.data && projectId && (
        <RowEditing value={rowEditing}>
        <DndContext
          sensors={sensors}
          onDragEnd={(event) => {
            swallowClickAfterDrag();
            onDragEnd(event);
          }}
          onDragCancel={swallowClickAfterDrag}
        >
          {(backlog.data.sprints ?? []).map((item) => {
            const cards = visible(item.issues);
            return (
              <SectionCard key={item.sprint!.id}>
                <SprintHeader sprint={item.sprint!} issues={item.issues ?? []} canManage={canManage} shown={cards.length} futureSprints={futureSprints} projectId={projectId} />
                <IssueList id={item.sprint!.id!} cards={cards} canDrag={canRank} onOpen={setOpenIssue} actions={actions} empty={t('agile.sprintEmpty')} />
              </SectionCard>
            );
          })}
          <SectionCard>
            <div className="flex items-center gap-2 px-2">
              <Typography variant="h3" component="h2">{t('agile.backlog')}</Typography>
              <Typography variant="body2" color="textSecondary">
                {backlogCards.length === backlogTotal ? t('agile.issueCount', { count: backlogTotal }) : t('agile.issueCountFiltered', { shown: backlogCards.length, total: backlogTotal })}
              </Typography>
            </div>
            {scrum ? (
              <IssueList id={BACKLOG_LIST} cards={backlogCards} canDrag={canRank} onOpen={setOpenIssue} actions={actions} empty={t('agile.backlogEmpty')} />
            ) : (
              <div style={{ borderTop: `1px solid ${sem('border-default')}` }}>
                {backlogCards.length > 0 && (
                  <IssueTable extraColumn={canRank}>
                    {backlogCards.map((card) => (
                      <Box key={card.id} component="tr" sx={rowStyles}>
                        <RowCells card={card} onOpen={setOpenIssue} />
                        {canRank && <td><Button kind="link" aria-label={t('agile.moveToBoardFor', { key: card.key })} onClick={() => pull.mutate(card)}>{t('agile.moveToBoard')}</Button></td>}
                      </Box>
                    ))}
                  </IssueTable>
                )}
                {backlogCards.length === 0 && <EmptyState inline title={t('agile.backlogEmpty')} />}
              </div>
            )}
          </SectionCard>
        </DndContext>
        </RowEditing>
      )}

      {(backlog.data?.closedSprints?.length ?? 0) > 0 && (
        <section className="flex flex-col gap-1">
          <ButtonBase onClick={() => setClosedOpen(!closedOpen)} aria-expanded={closedOpen} className="flex h-8 items-center gap-2 px-2" sx={{ justifyContent: 'flex-start' }}>
            {closedOpen ? <ChevronDown size={16} strokeWidth={1.5} aria-hidden /> : <ChevronRight size={16} strokeWidth={1.5} aria-hidden />}
            <Typography variant="h3" component="span">{t('agile.closedSprints')}</Typography>
            <Typography variant="body2" color="textSecondary" component="span">{t('agile.sprintCount', { count: backlog.data?.closedSprints?.length })}</Typography>
          </ButtonBase>
          {closedOpen && (backlog.data?.closedSprints ?? []).map((sprint) => (
            <Button key={sprint.id} kind="ghost" component={RouterLink} to={`/spaces/${spaceKey}/sprints/${sprint.id}`} sx={{ alignSelf: 'flex-start' }}>{sprint.name}</Button>
          ))}
        </section>
      )}

      {creating && projectId && <SprintDialog mode="create" projectId={projectId} onClose={() => setCreating(false)} />}
      <IssuePanel issueKey={openIssue} onClose={() => setOpenIssue(null)} />
    </div>
  );
}
