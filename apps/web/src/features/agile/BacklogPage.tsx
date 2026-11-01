import { DndContext, KeyboardSensor, PointerSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ChevronDown, ChevronRight, Ellipsis, Pencil, Trash2 } from 'lucide-react';
import { memo, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { Badge, StatusBadge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { DialogConfirm } from '../../components/Dialog';
import { IssueTypeIcon, PriorityIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { MenuItem } from '../../components/Menu';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { font, radius, sem } from '../../theme/tokens';
import { FilterChips, FilterControls, useIssueFilters } from '../issues/IssueFilters';
import { priorityLevel, statusCategory } from '../issues/api';
import { useSpace } from '../spaces/api';
import { IssuePanel } from './IssuePanel';
import { ClosedSprintDialog, CompleteSprintDialog, SprintDialog } from './SprintDialogs';
import { backlogKey, useBacklog, useBoardLive, type Backlog, type Card, type SprintSummary } from './api';

const BACKLOG_LIST = 'backlog';

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

  const run = (action: () => void) => {
    setAnchor(null);
    action();
  };

  return (
    <div className="flex h-9 items-center gap-3 px-2">
      <IssueTypeIcon type={card.type?.icon as IconType} />
      <span style={{ width: 96, flexShrink: 0, fontSize: font.size.xs, color: sem('text-secondary') }}>{card.key}</span>
      <ButtonBase onClick={() => onOpen(card.key!)} className="min-w-0 flex-1 truncate" sx={{ justifyContent: 'flex-start', fontSize: font.size.md, textAlign: 'left' }}>
        <span className="truncate">{card.title}</span>
      </ButtonBase>
      {card.epicTitle && <Badge tone="neutral">{card.epicTitle}</Badge>}
      <StatusBadge status={statusCategory(card.status)} label={card.status?.name} />
      <PriorityIcon priority={priorityLevel(card.priority)} />
      {card.assignee ? <Avatar name={card.assignee.name ?? ''} /> : <span style={{ width: 24 }} />}
      {actions && (
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
      )}
    </div>
  );
}

export const IssueRow = memo(RowBody);

function DraggableRow({ card, onOpen, actions, canDrag }: IssueRowProps & { canDrag: boolean }) {
  const { listeners, setNodeRef: dragRef, transform, isDragging } = useDraggable({ id: card.id!, disabled: !canDrag });
  const { setNodeRef: dropRef, isOver } = useDroppable({ id: `row:${card.id}` });

  return (
    <div
      ref={(node) => {
        dragRef(node);
        dropRef(node);
      }}
      data-testid={`row-${card.key}`}
      {...listeners}
      style={{
        opacity: isDragging ? 0.5 : 1,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined,
        borderTop: `2px solid ${isOver ? sem('border-focus') : 'transparent'}`,
        backgroundColor: sem('bg-surface'),
        cursor: canDrag ? 'grab' : undefined,
      }}
    >
      <IssueRow card={card} onOpen={onOpen} actions={actions} />
    </div>
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
      style={{ minHeight: 40, borderRadius: radius.control, backgroundColor: isOver ? sem('bg-selected') : undefined }}
    >
      {cards.map((card) => (
        <DraggableRow key={card.id} card={card} canDrag={canDrag} onOpen={onOpen} actions={actions} />
      ))}
      {cards.length === 0 && (
        <Typography variant="body2" color="textSecondary" className="px-2 py-2">{empty}</Typography>
      )}
    </div>
  );
}

function SprintHeader({ sprint, canManage, shown, futureSprints, projectId }: { sprint: SprintSummary; canManage: boolean; shown: number; futureSprints: SprintSummary[]; projectId: string }) {
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
  const count = shown === total ? t('agile.issueCount', { count: total }) : t('agile.issueCountFiltered', { shown, total });
  const points = active
    ? t('agile.pointsProgress', { done: sprint.donePoints, total: sprint.totalPoints })
    : t('agile.points', { total: sprint.totalPoints });

  return (
    <div className="flex items-center gap-2 px-2">
      <Typography variant="h3" component="h2">{sprint.name}</Typography>
      <Typography variant="body2" color="textSecondary" className="flex-1">{[range, count, points].filter(Boolean).join(' · ')}</Typography>
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

  const sprintOptions = useMemo(
    () => (backlog.data?.sprints ?? []).map((item) => ({ value: item.sprint!.id!, label: item.sprint!.name! })),
    [backlog.data],
  );
  const filters = useIssueFilters(projectId, scrum ? sprintOptions : undefined);
  const [text, setText] = useState('');
  const [openIssue, setOpenIssue] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [closedOpen, setClosedOpen] = useState(false);
  const [closedSprint, setClosedSprint] = useState<string | null>(null);

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
        <DndContext sensors={sensors} onDragEnd={onDragEnd}>
          {(backlog.data.sprints ?? []).map((item) => {
            const cards = visible(item.issues);
            return (
              <section key={item.sprint!.id} className="flex flex-col gap-1">
                <SprintHeader sprint={item.sprint!} canManage={canManage} shown={cards.length} futureSprints={futureSprints} projectId={projectId} />
                <IssueList id={item.sprint!.id!} cards={cards} canDrag={canRank} onOpen={setOpenIssue} actions={actions} empty={t('agile.sprintEmpty')} />
              </section>
            );
          })}
          <section className="flex flex-col gap-1">
            <div className="flex items-center gap-2 px-2">
              <Typography variant="h3" component="h2">{t('agile.backlog')}</Typography>
              <Typography variant="body2" color="textSecondary">
                {backlogCards.length === backlogTotal ? t('agile.issueCount', { count: backlogTotal }) : t('agile.issueCountFiltered', { shown: backlogCards.length, total: backlogTotal })}
              </Typography>
            </div>
            {scrum ? (
              <IssueList id={BACKLOG_LIST} cards={backlogCards} canDrag={canRank} onOpen={setOpenIssue} actions={actions} empty={t('agile.backlogEmpty')} />
            ) : (
              <div>
                {backlogCards.map((card) => (
                  <div key={card.id} className="flex items-center">
                    <div className="min-w-0 flex-1"><IssueRow card={card} onOpen={setOpenIssue} /></div>
                    {canRank && <Button kind="link" aria-label={t('agile.moveToBoardFor', { key: card.key })} onClick={() => pull.mutate(card)}>{t('agile.moveToBoard')}</Button>}
                  </div>
                ))}
                {backlogCards.length === 0 && <EmptyState title={t('agile.backlogEmpty')} />}
              </div>
            )}
          </section>
        </DndContext>
      )}

      {(backlog.data?.closedSprints?.length ?? 0) > 0 && (
        <section className="flex flex-col gap-1">
          <ButtonBase onClick={() => setClosedOpen(!closedOpen)} aria-expanded={closedOpen} className="flex h-8 items-center gap-2 px-2" sx={{ justifyContent: 'flex-start' }}>
            {closedOpen ? <ChevronDown size={16} strokeWidth={1.5} aria-hidden /> : <ChevronRight size={16} strokeWidth={1.5} aria-hidden />}
            <Typography variant="h3" component="span">{t('agile.closedSprints')}</Typography>
            <Typography variant="body2" color="textSecondary" component="span">{t('agile.sprintCount', { count: backlog.data?.closedSprints?.length })}</Typography>
          </ButtonBase>
          {closedOpen && (backlog.data?.closedSprints ?? []).map((sprint) => (
            <Button key={sprint.id} kind="ghost" sx={{ alignSelf: 'flex-start' }} onClick={() => setClosedSprint(sprint.id!)}>{sprint.name}</Button>
          ))}
        </section>
      )}

      {creating && projectId && <SprintDialog mode="create" projectId={projectId} onClose={() => setCreating(false)} />}
      {closedSprint && <ClosedSprintDialog sprintId={closedSprint} onClose={() => setClosedSprint(null)} onOpenIssue={(issueKey) => { setClosedSprint(null); setOpenIssue(issueKey); }} />}
      <IssuePanel issueKey={openIssue} onClose={() => setOpenIssue(null)} />
    </div>
  );
}
