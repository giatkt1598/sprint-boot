import {
  DndContext,
  DragOverlay,
  KeyboardCode,
  KeyboardSensor,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useQueryClient } from '@tanstack/react-query';
import { Columns3, Ellipsis } from 'lucide-react';
import { createContext, memo, use, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router';
import { ApiError, apiClient, unwrap } from '../../api';
import { Avatar, UnassignedAvatar } from '../../components/Avatar';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog } from '../../components/Dialog';
import { IssueTypeIcon, PriorityIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { MenuItem } from '../../components/Menu';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { font, radius, sem, shadow } from '../../theme/tokens';
import { FilterChips, FilterControls, useIssueFilters } from '../issues/IssueFilters';
import { TransitionDialog, type Available } from '../issues/TransitionControl';
import { priorityLevel } from '../issues/api';
import { SpacePermission, useSpace } from '../spaces/api';
import { ColumnsEditor } from './ColumnsEditor';
import { IssuePanel } from './IssuePanel';
import { CompleteSprintDialog } from './SprintDialogs';
import { boardKey, useBacklog, useBoard, useBoardLive, type BoardView, type Card } from './api';

type Column = NonNullable<BoardView['columns']>[number];

/** Small enough for five columns beside the sidebar on a 1920px screen. */
const COLUMN_MIN_WIDTH = 200;

/** Set per space in Configure Columns. */
const CardFieldsContext = createContext<string[]>([]);

function CardBody({ card }: { card: Card }) {
  const fields = use(CardFieldsContext);
  const has = (field: string) => fields.includes(field);

  return (
    <div className="flex flex-col gap-1 p-2 text-left">
      <span className="line-clamp-3" style={{ fontSize: font.size.md }}>{card.title}</span>
      {has('epic') && card.epicTitle && <span className="self-start"><Badge tone="epic">{card.epicTitle}</Badge></span>}
      {has('labels') && (card.labels?.length ?? 0) > 0 && (
        <span className="truncate" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{card.labels?.join(', ')}</span>
      )}
      <span className="flex items-center gap-2">
        {has('type') && <IssueTypeIcon type={card.type?.icon as IconType} />}
        <span className="flex-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{has('key') && card.key}</span>
        {has('points') && card.storyPoints != null && <span style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{card.storyPoints}</span>}
        {has('priority') && <PriorityIcon priority={priorityLevel(card.priority)} />}
        {has('assignee') && (card.assignee ? <Avatar name={card.assignee.name ?? ''} /> : <UnassignedAvatar />)}
      </span>
    </div>
  );
}

const cardStyle = { backgroundColor: sem('bg-surface'), borderRadius: `${radius.card}px` };

const BoardCard = memo(function BoardCard({ card, canDrag, onOpen }: { card: Card; canDrag: boolean; onOpen: (key: string) => void }) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: card.id!, disabled: !canDrag });
  const { setNodeRef: dropRef, isOver } = useDroppable({ id: `card:${card.id}` });

  return (
    <div ref={dropRef} style={{ borderTop: `2px solid ${isOver ? sem('border-focus') : 'transparent'}` }}>
      <ButtonBase
        ref={setNodeRef}
        {...attributes}
        {...listeners}
        role="button"
        data-testid={`card-${card.key}`}
        onClick={() => onOpen(card.key!)}
        className="w-full"
        sx={{ ...cardStyle, display: 'block', opacity: isDragging ? 0.4 : 1, '&:hover': { backgroundColor: sem('bg-hover') } }}
      >
        <CardBody card={card} />
      </ButtonBase>
    </div>
  );
});

function StatusZone({ statusId, name, transition }: { statusId: string; name: string; transition?: Available }) {
  const { t } = useTranslation();
  const { setNodeRef, isOver } = useDroppable({ id: `status:${statusId}`, data: { transition } });
  const allowed = Boolean(transition);
  const tone = allowed ? 'border-focus' : 'feedback-danger-text';

  return (
    <div
      ref={setNodeRef}
      data-testid={`zone-${name}`}
      className="flex flex-1 flex-col items-center justify-center p-3 text-center"
      style={{
        minHeight: 96,
        border: `2px dashed ${sem(tone)}`,
        borderRadius: radius.card,
        backgroundColor: sem(allowed ? (isOver ? 'bg-selected' : 'bg-surface') : 'feedback-danger-bg'),
      }}
    >
      <span style={{ fontWeight: font.weight.medium, color: sem(allowed ? 'text-accent' : 'feedback-danger-text') }}>{name}</span>
      <span style={{ fontSize: font.size.xs, color: sem(allowed ? 'text-secondary' : 'feedback-danger-text') }}>
        {transition?.name ?? t('agile.noTransition')}
      </span>
    </div>
  );
}

interface ColumnViewProps {
  column: Column;
  cards: Card[];
  total: number;
  canDrag: boolean;
  dragging: Card | null;
  transitions: Available[];
  onOpen: (key: string) => void;
}

function ColumnView({ column, cards, total, canDrag, dragging, transitions, onOpen }: ColumnViewProps) {
  const { t } = useTranslation();
  const { setNodeRef } = useDroppable({ id: `column:${column.id}` });
  const over = column.maxLimit != null && total > column.maxLimit;
  const under = column.minLimit != null && total < column.minLimit;
  const warn = over || under;
  const ownColumn = dragging && column.statuses?.some((status) => status.id === dragging.status?.id);
  const showZones = dragging && !ownColumn;

  return (
    <section
      ref={setNodeRef}
      aria-label={column.name ?? ''}
      data-testid={`column-${column.name}`}
      className="flex min-w-0 flex-1 flex-col gap-2 p-2"
      style={{ minWidth: COLUMN_MIN_WIDTH, borderRadius: radius.card, backgroundColor: sem(warn ? 'feedback-warning-bg' : 'bg-page') }}
    >
      <div className="flex items-center gap-2 px-1 py-1" style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem(warn ? 'feedback-warning-text' : 'text-secondary') }}>
        <span>{column.name}</span>
        <span>{column.maxLimit != null ? `${total} / ${column.maxLimit}` : total}</span>
        {over && <span>{t('agile.overLimit')}</span>}
        {under && <span>{t('agile.underLimit')}</span>}
      </div>
      {showZones ? (
        <div className="flex flex-1 flex-col gap-2">
          {(column.statuses ?? []).map((status) => (
            <StatusZone key={status.id} statusId={status.id!} name={status.name!} transition={transitions.find((item) => item.toStatus?.id === status.id)} />
          ))}
        </div>
      ) : (
        cards.map((card) => <BoardCard key={card.id} card={card} canDrag={canDrag} onOpen={onOpen} />)
      )}
    </section>
  );
}

export function BoardPage() {
  const { t } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const projectId = space.data?.id;
  const board = useBoard(projectId);
  const backlog = useBacklog(projectId);
  useBoardLive(projectId);

  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const filters = useIssueFilters(projectId);
  const [text, setText] = useState('');
  const [openIssue, setOpenIssue] = useState<string | null>(null);
  const [dragging, setDragging] = useState<Card | null>(null);
  const [transitions, setTransitions] = useState<Available[]>([]);
  const [dialog, setDialog] = useState<{ card: Card; transition: Available } | null>(null);
  const [completing, setCompleting] = useState(false);
  const [configuring, setConfiguring] = useState(false);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);

  const canTransition = space.data?.permissions?.includes('Issue.Transition') ?? false;
  const canRank = space.data?.permissions?.includes('Backlog.Rank') ?? false;
  const canManage = space.data?.permissions?.includes('Sprint.Manage') ?? false;
  const canConfigure = space.data?.permissions?.includes(SpacePermission.manage) ?? false;
  const key = boardKey(projectId);

  // Enter opens the card, so only Space picks it up for a keyboard drag.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, {
      keyboardCodes: { start: [KeyboardCode.Space], cancel: [KeyboardCode.Esc], end: [KeyboardCode.Space, KeyboardCode.Enter, KeyboardCode.Tab] },
    }),
  );

  const visibleCards = useMemo(
    () => (board.data?.cards ?? []).filter((card) => filters.matches(card) && (card.title ?? '').toLowerCase().includes(text.toLowerCase())),
    [board.data?.cards, filters, text],
  );

  const refresh = () => queryClient.invalidateQueries({ queryKey: key });

  const onDragStart = async ({ active }: DragStartEvent) => {
    const card = board.data?.cards?.find((item) => item.id === active.id) ?? null;
    setDragging(card);
    setTransitions([]);
    if (card) {
      const available = await unwrap(apiClient.GET('/api/v1/transition/available/{issueId}', { params: { path: { issueId: card.id! } } })).catch(() => []);
      setTransitions(available);
    }
  };

  const transitionCard = async (card: Card, transition: Available) => {
    queryClient.setQueryData<BoardView>(key, (data) =>
      data ? { ...data, cards: (data.cards ?? []).map((item) => (item.id === card.id ? { ...item, status: transition.toStatus! } : item)) } : data,
    );
    try {
      await unwrap(
        apiClient.POST('/api/v1/transition/execute/{issueId}', {
          params: { path: { issueId: card.id! } },
          body: { transitionId: transition.id!, concurrencyStamp: card.concurrencyStamp! },
        }),
      );
    } catch (failure) {
      toast('error', failure instanceof ApiError && failure.status === 409 ? t('agile.movedByOther') : toMessage(failure));
    }
    await refresh();
  };

  const onDragEnd = ({ over }: DragEndEvent) => {
    const card = dragging;
    setDragging(null);
    if (!card || !over) {
      return;
    }

    const target = String(over.id);
    if (target.startsWith('status:')) {
      const transition = over.data.current?.transition as Available | undefined;
      if (!transition) {
        toast('error', t('agile.dropNotAllowed'));
      } else if (transition.requiresComment || transition.missingFields?.length || transition.problems?.length) {
        setDialog({ card, transition });
      } else {
        void transitionCard(card, transition);
      }
      return;
    }

    if (target.startsWith('card:') && canRank) {
      const targetId = target.slice(5);
      const siblings = (board.data?.cards ?? []).filter((item) => item.status?.id === card.status?.id);
      const index = siblings.findIndex((item) => item.id === targetId);
      if (targetId !== card.id && index >= 0) {
        const others = siblings.filter((item) => item.id !== card.id);
        const position = others.findIndex((item) => item.id === targetId);
        void unwrap(
          apiClient.POST('/api/v1/board/rank/{issueId}', {
            params: { path: { issueId: card.id! } },
            body: { afterIssueId: others[position - 1]?.id ?? null, beforeIssueId: targetId },
          }),
        )
          .catch((failure: unknown) => toast('error', toMessage(failure)))
          .finally(() => void refresh());
      }
    }
  };

  const columnStatuses = (column: Column) => (column.statuses ?? []).map((status) => status.id);
  const sprint = board.data?.activeSprint;
  const scrum = board.data?.type === 0;
  const futureSprints = (backlog.data?.sprints ?? []).map((item) => item.sprint!).filter((item) => item.state === 0);

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="flex items-center gap-2">
        <Typography variant="h1" className="flex-1">{scrum ? (sprint?.name ?? t('agile.board')) : t('agile.board')}</Typography>
        <FilterControls filters={filters} />
        <SearchInput label={t('issues.search')} value={text} onChange={(event) => setText(event.target.value)} />
        {scrum && sprint && canManage && <Button kind="secondary" onClick={() => setCompleting(true)}>{t('agile.completeSprint')}</Button>}
        {canConfigure && (
          <>
            <Tooltip title={t('agile.boardMore')}>
              <IconButton size="small" aria-label={t('agile.boardMore')} onClick={(event) => setMenuAnchor(event.currentTarget)}>
                <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
            </Tooltip>
            <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
              <MenuItem icon={Columns3} onClick={() => { setMenuAnchor(null); setConfiguring(true); }}>{t('agile.configureColumns')}</MenuItem>
            </Menu>
          </>
        )}
      </div>
      <FilterChips filters={filters} />

      {board.isPending && <LoadingState />}
      {board.isError && <ErrorState onRetry={() => void board.refetch()} />}
      {board.data && scrum && !sprint && (
        <EmptyState
          title={t('agile.noActiveSprint')}
          action={<Link component={RouterLink} to={`/spaces/${spaceKey}/backlog`} variant="body2">{t('agile.goToBacklog')}</Link>}
        />
      )}
      {board.data && (!scrum || sprint) && (
        <CardFieldsContext value={board.data.cardFields ?? []}>
        <DndContext sensors={sensors} onDragStart={(event) => void onDragStart(event)} onDragEnd={onDragEnd} onDragCancel={() => setDragging(null)}>
          <div className="flex min-h-0 flex-1 gap-3 overflow-x-auto">
            {(board.data.columns ?? []).filter((column) => !column.isHidden).map((column) => (
              <ColumnView
                key={column.id}
                column={column}
                cards={visibleCards.filter((card) => columnStatuses(column).includes(card.status?.id))}
                total={(board.data.cards ?? []).filter((card) => columnStatuses(column).includes(card.status?.id)).length}
                canDrag={canTransition}
                dragging={dragging}
                transitions={transitions}
                onOpen={setOpenIssue}
              />
            ))}
          </div>
          <DragOverlay>
            {dragging && (
              <div style={{ ...cardStyle, boxShadow: shadow.popover }}>
                <CardBody card={dragging} />
              </div>
            )}
          </DragOverlay>
        </DndContext>
        </CardFieldsContext>
      )}

      {configuring && space.data && (
        <Dialog open width={640} title={t('agile.configureColumns')} onClose={() => setConfiguring(false)}>
          <ColumnsEditor space={space.data} canManage onDone={() => setConfiguring(false)} />
        </Dialog>
      )}
      {dialog && (
        <TransitionDialog
          issue={dialog.card}
          transition={dialog.transition}
          people={filters.people}
          onConflict={() => toast('error', t('agile.movedByOther'))}
          onClose={() => setDialog(null)}
        />
      )}
      {completing && sprint && <CompleteSprintDialog sprint={sprint} futureSprints={futureSprints} onClose={() => setCompleting(false)} />}
      <IssuePanel issueKey={openIssue} onClose={() => setOpenIssue(null)} />
    </div>
  );
}
