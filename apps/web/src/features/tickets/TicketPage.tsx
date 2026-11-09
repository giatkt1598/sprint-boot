import Box from '@mui/material/Box';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { DevelopmentPanel } from './DevelopmentPanel';
import { AddIcon, DeleteIcon, LinkIcon, MoreIcon, ShowIcon, UploadIcon } from '../../components/icons';
import { useEffect, useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import { ApiError, apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Resizer, usePersistentWidth } from '../../components/Resizer';
import { LinkedTickets } from './LinkedTickets';
import { Avatar, UnassignedAvatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { CollapseHeading } from '../../components/CollapseHeading';
import { DialogConfirm } from '../../components/Dialog';
import { TicketTypeIcon, PriorityIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { MenuItem } from '../../components/Menu';
import { PickerPopover } from '../../components/PickerPopover';
import { Popover } from '../../components/Popover';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Tabs } from '../../components/Tabs';
import { TagInput } from '../../components/TagInput';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { UserPicker } from '../../components/UserPicker';
import { useErrorMessage } from '../../lib/errors';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { useRecordVisit } from './RecentsMenu';
import { hasSelectedText } from '../../lib/selection';
import { formatDateTime, formatRelative, formatSize } from '../../lib/format';
import { font, hoverPane, radius, sem } from '../../theme/tokens';
import { useMembers, useSpace } from '../spaces/api';
import { TicketActivityPanel } from './TicketActivityPanel';
import { QuickAssignee, QuickStatus, QuickTitle, TitleEditor } from './QuickFields';
import { TransitionControl } from './TransitionControl';
import { EpicProgress } from '../agile/EpicProgress';
import { backlogKey, useBacklog } from '../agile/api';
import { useViewingTicket } from '../assistant/AssistantPanel';
import { RichTextEditor, RichTextView } from './RichTextEditor';
import { priorityLevel, uploadAttachment, useEpics, useTicketTypes, useLabels, usePriorities, type Ticket } from './api';
import { DatePicker } from '../../components/DatePicker';

const DETAIL_LABEL_WIDTH = 88;
const DETAIL_GAP = 12;

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-h-9 items-center gap-3">
      <span style={{ width: DETAIL_LABEL_WIDTH, flexShrink: 0, fontSize: font.size.xs, color: sem('text-tertiary') }}>{label}</span>
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}

const useTicket = (key: string | undefined) =>
  useQuery({
    queryKey: ['tickets', key],
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/by-key', { params: { query: { key } } })),
    retry: (count, failure) => !(failure instanceof ApiError && failure.status < 500) && count < 2,
  });

export function TicketPage() {
  const { ticketKey } = useParams();
  return <TicketContent ticketKey={ticketKey} />;
}

/** What a side panel pins above the ticket: its close control, and whether the panel has scrolled. */
export interface PanelHeader {
  close: ReactNode;
  scrolled: boolean;
}

/** Same content on the full page and in the board panel; compact stacks the details. */
export function TicketContent({ ticketKey, compact, panelHeader }: { ticketKey: string | undefined; compact?: boolean; panelHeader?: PanelHeader }) {
  const { t } = useTranslation();
  const ticket = useTicket(ticketKey);

  if (ticket.isPending || ticket.isError) {
    const state = ticket.isPending ? (
      <LoadingState />
    ) : ticket.error instanceof ApiError && ticket.error.status < 500 ? (
      <NotFoundPage title={t('notFound.ticketTitle')} description={t('tickets.notFound')} />
    ) : (
      <ErrorState onRetry={() => void ticket.refetch()} />
    );
    return panelHeader ? <div className="flex flex-col gap-4 pt-6">{panelHeader.close}{state}</div> : state;
  }
  return <TicketView key={ticket.data.id} ticket={ticket.data} compact={compact} panelHeader={panelHeader} />;
}

// Wide enough that the assignee and reporter pickers show a full name next to the label column.
const DETAILS_WIDTH = { min: 320, max: 560, initial: 360 };

function TicketView({ ticket, compact, panelHeader }: { ticket: Ticket; compact?: boolean; panelHeader?: PanelHeader }) {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const space = useSpace(ticket.projectKey ?? undefined);
  const members = useMembers(ticket.projectId);
  const priorities = usePriorities();
  const epics = useEpics(ticket.projectId);
  const canEdit = space.data?.permissions?.includes('Ticket.Edit') ?? false;
  const me = useCurrentUser();
  const people = (members.data ?? []).map((member) => member.user!);
  useViewingTicket({ id: ticket.id!, key: ticket.key! });
  useRecordVisit(ticket.id);

  const [conflict, setConflict] = useState(false);
  const [editingTitle, setEditingTitle] = useState(false);
  const [editingDescription, setEditingDescription] = useState(false);
  const [description, setDescription] = useState(ticket.description ?? '');
  const [descriptionOpen, setDescriptionOpen] = useState(true);
  const [detailsOpen, setDetailsOpen] = useState(true);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [addingSubtask, setAddingSubtask] = useState(false);
  const [tab, setTab] = useState('comments');
  const details = usePersistentWidth('sprint-boot.ticket-sidebar-width', DETAILS_WIDTH);
  const sidebarWidth = details.width;
  const [fieldError, setFieldError] = useState<string | null>(null);

  const update = useMutation({
    mutationFn: (fields: Record<string, unknown>) =>
      unwrap(
        apiClient.PUT('/api/v1/ticket/{id}', {
          params: { path: { id: ticket.id! } },
          // From the cache: a second edit can run before this view renders the first result.
          body: {
            concurrencyStamp: queryClient.getQueryData<Ticket>(['tickets', ticket.key])?.concurrencyStamp ?? ticket.concurrencyStamp!,
            fields: fields as never,
          },
        }),
      ),
    onSuccess: (next) => {
      setFieldError(null);
      queryClient.setQueryData(['tickets', ticket.key], next);
      void queryClient.invalidateQueries({ queryKey: ['tickets', ticket.id, 'activity'] });
      // The space overview shows dates, statuses and counts from its own lists, so they reload too.
      void queryClient.invalidateQueries({ queryKey: ['tickets', 'timeline', ticket.projectId] });
      void queryClient.invalidateQueries({ queryKey: ['tickets', 'overview', ticket.projectId] });
    },
    onError: (failure) => {
      if (failure instanceof ApiError && failure.status === 409) {
        setConflict(true);
      } else {
        setFieldError(toMessage(failure));
        toast('error', toMessage(failure));
      }
    },
  });

  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/ticket/{id}', { params: { path: { id: ticket.id! } } })),
    onSuccess: async () => {
      toast('success', t('tickets.deleted', { key: ticket.key }));
      await queryClient.invalidateQueries({ queryKey: ['tickets'] });
      await navigate(`/spaces/${ticket.projectKey}/tickets`);
    },
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setConfirmingDelete(false),
  });

  const saveTitle = (title: string) => {
    setEditingTitle(false);
    if (title.trim() && title !== ticket.title) {
      update.mutate({ title });
    }
  };

  const copyLink = async () => {
    setMenuAnchor(null);
    // Not the current address: the drawer opens over a board or backlog.
    await navigator.clipboard.writeText(`${window.location.origin}/browse/${ticket.key}`);
    toast('success', t('tickets.linkCopied'));
  };

  return (
    <div className={compact ? 'flex flex-col gap-4' : 'flex h-full min-h-0 flex-col gap-4'}>
      {/* In a panel everything down to the title stays pinned while the rest scrolls under it. */}
      <StickyHeader panel={panelHeader}>
        {conflict && (
          <div
            role="alert"
            className="flex items-center gap-2 px-3 py-2"
            style={{ backgroundColor: sem('feedback-warning-bg'), color: sem('feedback-warning-text'), borderRadius: radius.control }}
          >
            <span className="flex-1">{t('tickets.conflictBanner')}</span>
            <Button kind="secondary" onClick={() => { setConflict(false); void queryClient.invalidateQueries({ queryKey: ['tickets', ticket.key] }); }}>
              {t('common.reload')}
            </Button>
          </div>
        )}

        <nav aria-label="breadcrumb" className="flex items-center gap-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
          <Link component={RouterLink} to={`/spaces/${ticket.projectKey}/backlog`} color="inherit">{ticket.projectName}</Link>
          <span aria-hidden>/</span>
          {ticket.parent && (
            <>
              <Link component={RouterLink} to={`/browse/${ticket.parent.key}`} color="inherit" className="flex items-center gap-1">
                <TicketTypeIcon type={ticket.parent.typeIcon as IconType} />
                {ticket.parent.key}
              </Link>
              <span aria-hidden>/</span>
            </>
          )}
          <Box
            className="flex items-center gap-1"
            sx={{
              color: sem('text-primary'),
              '& .copy-link': { opacity: 0 },
              '&:hover .copy-link, & .copy-link:focus-visible': { opacity: 1 },
            }}
          >
            <TypeField ticket={ticket} canEdit={canEdit} onPick={(typeId) => update.mutate({ type: typeId })} />
            {ticket.key}
            <Tooltip title={t('tickets.copyLink')}>
              <IconButton className="copy-link" size="small" aria-label={t('tickets.copyLink')} onClick={() => void copyLink()}>
                <LinkIcon size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
            </Tooltip>
          </Box>
        </nav>

        <div className="flex items-center gap-2">
          <div className="min-w-0 flex-1">
            {editingTitle ? (
              <TitleEditor label={t('tickets.titleField')} initial={ticket.title ?? ''} onSave={saveTitle} onCancel={() => setEditingTitle(false)} />
            ) : (
              <Typography
                variant="h1"
                title={panelHeader ? ticket.title ?? undefined : undefined}
                sx={panelHeader ? clampedTitle : undefined}
                tabIndex={canEdit ? 0 : undefined}
                onClick={() => canEdit && !hasSelectedText() && setEditingTitle(true)}
                onKeyDown={(event) => event.key === 'Enter' && canEdit && setEditingTitle(true)}
              >
                {ticket.title}
              </Typography>
            )}
          </div>
          <WatchControl ticketId={ticket.id!} />
          <IconButton size="small" aria-label={t('tickets.more')} onClick={(event) => setMenuAnchor(event.currentTarget)}>
            <MoreIcon size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
          <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
            <MenuItem icon={LinkIcon} onClick={() => void copyLink()}>{t('tickets.copyLink')}</MenuItem>
            <MenuItem icon={DeleteIcon} danger onClick={() => { setMenuAnchor(null); setConfirmingDelete(true); }}>{t('common.delete')}</MenuItem>
          </Menu>
        </div>
      </StickyHeader>

      {/* On the full page the content and the sidebar scroll on their own; the panel has one scroll. */}
      <div className={compact ? 'flex flex-col gap-6' : 'flex min-h-0 flex-1 gap-3'}>
        <div className={compact ? 'flex min-w-0 flex-1 flex-col gap-6' : 'flex min-w-0 flex-1 flex-col gap-6 overflow-y-auto pr-2'}>
          <section className="flex flex-col gap-2">
            {ticket.description && !editingDescription ? (
              <CollapseHeading variant="h2" label={t('tickets.description')} open={descriptionOpen} onToggle={() => setDescriptionOpen(!descriptionOpen)} />
            ) : (
              <Typography variant="h2" component="h2">{t('tickets.description')}</Typography>
            )}
            {editingDescription ? (
              <>
                <RichTextEditor label={t('tickets.description')} initialValue={ticket.description ?? ''} people={people} projectId={ticket.projectId} autoFocus onChange={setDescription} />
                <div className="flex gap-2">
                  <Button onClick={() => { setEditingDescription(false); update.mutate({ description }); }}>{t('common.save')}</Button>
                  <Button kind="ghost" onClick={() => setEditingDescription(false)}>{t('common.cancel')}</Button>
                </div>
              </>
            ) : ticket.description ? (
              descriptionOpen && <Box
                role={canEdit ? 'button' : undefined}
                tabIndex={canEdit ? 0 : undefined}
                onClick={(event) => {
                  // A link opens, and a drag that selected text stays a selection; only a plain click edits.
                  const onLink = (event.target as HTMLElement).closest('a');
                  if (canEdit && !onLink && !hasSelectedText()) {
                    setEditingDescription(true);
                  }
                }}
                onKeyDown={(event) => event.key === 'Enter' && canEdit && setEditingDescription(true)}
                sx={{
                  padding: 1,
                  borderRadius: `${radius.control}px`,
                  '&:hover': hoverPane,
                }}
              >
                <RichTextView html={ticket.description} />
              </Box>
            ) : canEdit ? (
              <div><Button kind="ghost" onClick={() => setEditingDescription(true)}>{t('tickets.addDescription')}</Button></div>
            ) : null}
          </section>

          {ticket.type?.hierarchy !== 0 && (
            <section className="flex flex-col">
              <div className="flex items-center pb-2">
                <Typography variant="h2" component="h2" className="flex-1">{t(ticket.type?.hierarchy === 2 ? 'agile.childTickets' : 'tickets.subtasks')}</Typography>
                {canEdit && ticket.type?.hierarchy === 1 && (
                  <Button kind="link" startIcon={<AddIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setAddingSubtask(true)}>
                    {t('tickets.addSubtask')}
                  </Button>
                )}
              </div>
              {ticket.type?.hierarchy === 2 && <EpicProgress total={ticket.children?.length ?? 0} done={countIn(ticket, 2)} inProgress={countIn(ticket, 1)} />}
              {(ticket.children?.length ?? 0) > 0 && (
                <table className="w-full" aria-label={t(ticket.type?.hierarchy === 2 ? 'agile.childTickets' : 'tickets.subtasks')} style={{ borderCollapse: 'collapse', borderTop: `1px solid ${sem('border-default')}` }}>
                  <tbody>
                    {ticket.children?.map((child) => (
                      <Box
                        component="tr"
                        key={child.id}
                        sx={{ height: 36, borderBottom: `1px solid ${sem('border-default')}`, '&:hover': { backgroundColor: sem('bg-hover') } }}
                      >
                        <td className="pl-2 pr-3"><span className="flex"><TicketTypeIcon type={child.typeIcon as IconType} /></span></td>
                        <td className="whitespace-nowrap pr-3">
                          <Link component={RouterLink} to={`/browse/${child.key}`} color="inherit" variant="body2">{child.key}</Link>
                        </td>
                        {/* Zero max-width with full width lets the title truncate and take the spare room. */}
                        <td className="w-full pr-3" style={{ maxWidth: 0 }}>
                          <span className="flex"><QuickTitle ticket={child} canEdit={canEdit} /></span>
                        </td>
                        <td className="whitespace-nowrap pr-3"><QuickAssignee ticket={child} canEdit={canEdit} people={people} showName /></td>
                        <td className="whitespace-nowrap pr-12"><QuickStatus ticket={child} canEdit={canEdit} people={people} /></td>
                      </Box>
                    ))}
                  </tbody>
                </table>
              )}
              {(ticket.children?.length ?? 0) === 0 && !addingSubtask && (
                <Typography variant="body2" color="textSecondary" className="px-2">
                  {t(ticket.type?.hierarchy === 2 ? 'agile.noChildTickets' : 'tickets.noSubtasks')}
                </Typography>
              )}
              {addingSubtask && <SubtaskRow ticket={ticket} onClose={() => setAddingSubtask(false)} />}
            </section>
          )}

          <LinkedTickets ticket={ticket} canEdit={canEdit} />

          <Attachments ticket={ticket} canEdit={canEdit} />

          <section className="flex flex-col gap-3">
            <Tabs
              label={t('tickets.tabComments')}
              value={tab}
              onChange={setTab}
              items={[
                { value: 'all', label: t('tickets.tabAll') },
                { value: 'comments', label: t('tickets.tabComments') },
                { value: 'history', label: t('tickets.tabHistory') },
                { value: 'worklog', label: t('tickets.tabWorkLog') },
              ]}
            />
            <TicketActivityPanel tab={tab} ticket={ticket} people={people} canEdit={canEdit} canManage={space.data?.permissions?.includes('Comment.DeleteAny') ?? false} />
          </section>
        </div>

        {!compact && <Resizer edge="start" label={t('tickets.resizeSidebar')} {...details} onChange={details.change} />}
        <div className={compact ? 'flex shrink-0 flex-col gap-4' : 'flex shrink-0 flex-col gap-4 overflow-y-auto'} style={{ width: compact ? '100%' : sidebarWidth }}>
        <aside
          className="flex shrink-0 flex-col gap-1 p-3"
          style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
        >
          <CollapseHeading label={t('tickets.details')} open={detailsOpen} onToggle={() => setDetailsOpen(!detailsOpen)} />
          {detailsOpen && (
          <>
          <DetailRow label={t('tickets.status')}>
            <TransitionControl ticket={ticket} people={people} onConflict={() => setConflict(true)} />
          </DetailRow>
          <DetailRow label={t('tickets.assignee')}>
            {canEdit ? (
              <UserPicker
                hideLabel
                label={t('tickets.assignee')}
                options={people}
                value={ticket.assignee ?? null}
                emptyLabel={t('common.unassigned')}
                onChange={(user) => update.mutate({ assignee: user?.id ?? null })}
              />
            ) : (
              <Person name={ticket.assignee?.name} src={ticket.assignee?.avatarUrl} />
            )}
          </DetailRow>
          {canEdit && !ticket.assignee && (
            <div style={{ paddingLeft: DETAIL_LABEL_WIDTH + DETAIL_GAP }}>
              <Button kind="link" sx={{ minHeight: 0, padding: 0 }} onClick={() => update.mutate({ assignee: me.id })}>{t('tickets.assignToMe')}</Button>
            </div>
          )}
          <DetailRow label={t('tickets.reporter')}>
            {canEdit ? (
              <UserPicker
                hideLabel
                label={t('tickets.reporter')}
                options={people}
                value={ticket.reporter ?? null}
                onChange={(user) => update.mutate({ reporter: user?.id ?? null })}
              />
            ) : (
              <Person name={ticket.reporter?.name} src={ticket.reporter?.avatarUrl} />
            )}
          </DetailRow>
          <DetailRow label={t('tickets.priority')}>
            {canEdit ? (
              <SelectField
                quiet
                label={t('tickets.priority')}
                value={ticket.priority?.id ?? ''}
                onChange={(event) => update.mutate({ priority: event.target.value })}
                options={(priorities.data ?? []).map((item) => ({
                  value: item.id!,
                  label: item.name!,
                  icon: <PriorityIcon priority={priorityLevel(item)} />,
                }))}
              />
            ) : (
              <span className="flex items-center gap-2"><PriorityIcon priority={priorityLevel(ticket.priority)} />{ticket.priority?.name}</span>
            )}
          </DetailRow>
          {ticket.type?.hierarchy === 1 && space.data?.type === 0 && (
            <DetailRow label={t('agile.sprint')}>
              <SprintField ticket={ticket} canEdit={canEdit} onError={(failure) => toast('error', toMessage(failure))} />
            </DetailRow>
          )}
          {ticket.type?.hierarchy === 1 && (
            <DetailRow label={t('tickets.epic')}>
              <EpicField ticket={ticket} canEdit={canEdit} epics={epics.data?.items ?? []} onPick={(epicId) => update.mutate({ parent: epicId })} />
            </DetailRow>
          )}
          <DetailRow label={t('tickets.labels')}>
            <LabelsField
              ticket={ticket}
              disabled={!canEdit}
              onSave={(labels, onSettled) => update.mutate({ labels }, { onSettled: (_, failure) => onSettled(!failure) })}
            />
          </DetailRow>
          <DetailRow label={t('tickets.points')}>
            <PointsField
              value={ticket.storyPoints?.toString() ?? ''}
              disabled={!canEdit}
              onSave={(text, onSettled) => update.mutate({ storyPoints: text ? Number(text) : null }, { onSettled })}
            />
          </DetailRow>
          <DetailRow label={t('tickets.startDate')}>
            <DatePicker quiet label={t('tickets.startDate')} value={ticket.startDate ?? ''} rangeEnd={ticket.dueDate ?? ''} disabled={!canEdit} onChange={(text) => update.mutate({ startDate: text || null })} />
          </DetailRow>
          <DetailRow label={t('tickets.dueDate')}>
            <DatePicker quiet label={t('tickets.dueDate')} value={ticket.dueDate ?? ''} rangeStart={ticket.startDate ?? ''} disabled={!canEdit} onChange={(text) => update.mutate({ dueDate: text || null })} />
          </DetailRow>
          {fieldError && <Typography role="alert" variant="body2" color="error">{fieldError}</Typography>}
          </>
          )}
        </aside>
        <DevelopmentPanel ticketId={ticket.id!} label={`${ticket.key} ${ticket.title}`} canEdit={canEdit} />
        <div className="flex shrink-0 flex-col gap-2 px-3" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
          <span title={formatDateTime(ticket.createdAt, i18n.language)}>{t('tickets.createdAt')} {formatRelative(ticket.createdAt, i18n.language)}</span>
          <span title={formatDateTime(ticket.updatedAt, i18n.language)}>{t('tickets.updatedAt')} {formatRelative(ticket.updatedAt, i18n.language)}</span>
        </div>
        </div>
      </div>

      <DialogConfirm
        open={confirmingDelete}
        title={t('tickets.deleteTitle', { key: ticket.key })}
        description={ticket.children?.length ? t('tickets.deleteBodySubtasks', { count: ticket.children.length }) : t('tickets.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setConfirmingDelete(false)}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}

/** Only types of the same level: a task cannot become an epic or a subtask here. */
function TypeField({ ticket, canEdit, onPick }: { ticket: Ticket; canEdit: boolean; onPick: (typeId: string) => void }) {
  const { t } = useTranslation();
  const types = useTicketTypes(ticket.projectId);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const siblings = (types.data ?? []).filter((type) => type.hierarchy === ticket.type?.hierarchy);
  const icon = <TicketTypeIcon type={ticket.type?.icon as IconType} />;

  if (!canEdit || siblings.length < 2) {
    return icon;
  }

  return (
    <>
      <ButtonBase
        aria-label={`${t('tickets.ticketType')}: ${t(`ticket.type.${ticket.type?.icon}`)}`}
        aria-haspopup="dialog"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ width: 24, height: 24, borderRadius: `${radius.control}px`, '&:hover': { backgroundColor: sem('bg-hover') } }}
      >
        <span aria-hidden className="flex">{icon}</span>
      </ButtonBase>
      <PickerPopover
        label={t('tickets.ticketType')}
        width={132}
        anchor={anchor}
        selectedId={ticket.type?.id}
        items={siblings.map((type) => ({ id: type.id!, label: t(`ticket.type.${type.icon}`), icon: <TicketTypeIcon type={type.icon as IconType} /> }))}
        onClose={() => setAnchor(null)}
        onPick={(typeId) => {
          setAnchor(null);
          if (typeId !== ticket.type?.id) {
            onPick(typeId);
          }
        }}
      />
    </>
  );
}

const NO_EPIC = 'none';

const epicTagStyle = {
  display: '-webkit-box',
  WebkitLineClamp: 2,
  WebkitBoxOrient: 'vertical',
  overflow: 'hidden',
  overflowWrap: 'anywhere',
  padding: '2px 6px',
  textAlign: 'left',
  fontSize: font.size.xs,
  fontWeight: font.weight.medium,
  color: sem('status-merged-text'),
  backgroundColor: sem('status-merged-bg'),
  borderRadius: radius.control,
} as const;

function EpicField({ ticket, canEdit, epics, onPick }: { ticket: Ticket; canEdit: boolean; epics: { id?: string; key?: string | null; title?: string | null }[]; onPick: (epicId: string | null) => void }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const parent = ticket.parent;
  const name = parent ? `${parent.key} ${parent.title}` : null;
  const tag = name ? <span style={epicTagStyle} title={name}>{name}</span> : <span>{t('common.none')}</span>;

  if (!canEdit) {
    return parent ? <RouterLink to={`/browse/${parent.key}`} style={{ textDecoration: 'none' }}>{tag}</RouterLink> : tag;
  }

  return (
    <>
      <ButtonBase
        aria-haspopup="dialog"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ justifyContent: 'flex-start', width: '100%', minHeight: 32, paddingInline: 1, fontSize: 'inherit', borderRadius: `${radius.control}px` }}
      >
        <span className="sr-only">{t('tickets.epic')}:</span>
        {tag}
      </ButtonBase>
      <PickerPopover
        label={t('tickets.epic')}
        searchLabel={t('tickets.searchEpics')}
        anchor={anchor}
        selectedId={parent?.id ?? NO_EPIC}
        items={[
          { id: NO_EPIC, label: t('common.none') },
          ...epics.map((epic) => ({ id: epic.id!, label: `${epic.key} ${epic.title}` })),
        ]}
        onClose={() => setAnchor(null)}
        onPick={(epicId) => {
          setAnchor(null);
          onPick(epicId === NO_EPIC ? null : epicId);
        }}
      />
    </>
  );
}

const countIn = (ticket: Ticket, category: number) =>
  (ticket.children ?? []).filter((child) => child.status?.category === category).length;

function SprintField({ ticket, canEdit, onError }: { ticket: Ticket; canEdit: boolean; onError: (failure: unknown) => void }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const backlog = useBacklog(ticket.projectId);
  const current = useQuery({
    queryKey: ['tickets', ticket.id, 'sprint'],
    // No sprint comes back as 204 with no body; React Query keeps the old data when a query yields undefined.
    queryFn: async () => (await unwrap(apiClient.GET('/api/v1/sprint/for-ticket/{ticketId}', { params: { path: { ticketId: ticket.id! } } }))) ?? null,
  });

  const move = useMutation({
    mutationFn: (sprintId: string) =>
      unwrap(
        apiClient.POST('/api/v1/sprint/move-ticket/{ticketId}', {
          params: { path: { ticketId: ticket.id! } },
          body: { sprintId: sprintId || null },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tickets', ticket.id] });
      await queryClient.invalidateQueries({ queryKey: backlogKey(ticket.projectId) });
    },
    onError,
  });

  if (!canEdit) {
    const name = current.data?.state === 2 ? t('agile.closedSprint', { name: current.data.name }) : current.data?.name;
    return <span>{name ?? t('common.none')}</span>;
  }

  const options = (backlog.data?.sprints ?? []).map((item) => ({ value: item.sprint!.id!, label: item.sprint!.name! }));
  // A closed sprint is no longer a choice, but it is still where the ticket was done: show it, marked closed, rather than
  // None. Moving the ticket on to an open sprint stays possible.
  const closed = current.data?.state === 2 && current.data.id ? [{ value: current.data.id, label: t('agile.closedSprint', { name: current.data.name }), disabled: true }] : [];

  return (
    <SelectField
      quiet
      label={t('agile.sprint')}
      value={current.data?.id ?? ''}
      displayEmpty
      onChange={(event) => move.mutate(event.target.value)}
      options={[{ value: '', label: t('common.none') }, ...closed, ...options]}
    />
  );
}

function WatchControl({ ticketId }: { ticketId: string }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const key = ['tickets', ticketId, 'watchers'];

  const watchers = useQuery({
    queryKey: key,
    queryFn: () => unwrap(apiClient.GET('/api/v1/watcher', { params: { query: { ticketId } } })),
  });

  const toggle = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST(watchers.data?.watchedByMe ? '/api/v1/watcher/unwatch/{ticketId}' : '/api/v1/watcher/watch/{ticketId}', { params: { path: { ticketId } } })),
    onSuccess: (next) => queryClient.setQueryData(key, next),
  });

  return (
    <>
      <Button
        kind="ghost"
        aria-label={t('tickets.watchers', { count: watchers.data?.count ?? 0 })}
        startIcon={<ShowIcon size={16} strokeWidth={1.5} aria-hidden />}
        onClick={(event) => setAnchor(event.currentTarget)}
      >
        {watchers.data?.count ?? ''}
      </Button>
      <Popover width={264} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <div className="flex flex-col gap-2">
          <Button kind="secondary" onClick={() => toggle.mutate()}>
            {t(watchers.data?.watchedByMe ? 'tickets.stopWatching' : 'tickets.startWatching')}
          </Button>
          {(watchers.data?.users ?? []).map((user) => <Person key={user.id} name={user.name} src={user.avatarUrl} />)}
        </div>
      </Popover>
    </>
  );
}

function Person({ name, src }: { name?: string | null; src?: string | null }) {
  const { t } = useTranslation();
  return (
    <span className="flex items-center gap-2">
      {name ? <Avatar name={name} src={src} /> : <UnassignedAvatar />}
      {name ?? t('common.unassigned')}
    </span>
  );
}

/** Stays open after each subtask, so several can be typed in a row. */
function SubtaskRow({ ticket, onClose }: { ticket: Ticket; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const types = useTicketTypes(ticket.projectId);
  const typeId = types.data?.find((type) => type.hierarchy === 0)?.id;
  const [title, setTitle] = useState('');
  const input = useRef<HTMLInputElement>(null);

  const create = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/ticket', { body: { projectId: ticket.projectId!, typeId: typeId!, title: title.trim(), parentId: ticket.id } })),
    onSuccess: async () => {
      setTitle('');
      input.current?.focus();
      await queryClient.invalidateQueries({ queryKey: ['tickets', ticket.key] });
    },
  });
  const ready = Boolean(title.trim() && typeId) && !create.isPending;

  return (
    <div className="flex flex-col gap-2 px-2">
      <div className="flex items-center gap-3">
        <TicketTypeIcon type="subtask" />
        <div className="min-w-0 flex-1">
          <TextField
            quiet
            label={t('tickets.subtaskName')}
            autoFocus
            inputRef={input}
            placeholder={t('tickets.subtaskPlaceholder')}
            value={title}
            sx={{ width: '100%' }}
            onChange={(event) => setTitle(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && ready) {
                create.mutate();
              }
              if (event.key === 'Escape') {
                onClose();
              }
            }}
          />
        </div>
      </div>
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
      <div className="flex justify-end gap-2">
        <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
        <Button disabled={!ready} onClick={() => create.mutate()}>{t('tickets.createAction')}</Button>
      </div>
    </div>
  );
}

const POINTS_PATTERN = /^\d*\.?\d*$/;

function PointsField({ value, disabled, onSave }: { value: string; disabled: boolean; onSave: (value: string, onSettled: () => void) => void }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<string | null>(null);

  if (disabled) {
    return <span>{value || t('common.none')}</span>;
  }

  const commit = () => {
    if (draft === null || Number(draft) === Number(value)) {
      setDraft(null);
    } else {
      // The typed number stays until the saved one arrives, so the field never jumps back.
      onSave(draft, () => setDraft(null));
    }
  };

  return (
    <TextField
      quiet
      label={t('tickets.points')}
      inputMode="decimal"
      value={draft ?? value}
      onChange={(event) => POINTS_PATTERN.test(event.target.value) && setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          (event.target as HTMLElement).blur();
        }
        if (event.key === 'Escape') {
          setDraft(null);
        }
      }}
    />
  );
}

/** Tags show at once. Saves run one at a time, each from the result of the last. */
function LabelsField({ ticket, disabled, onSave }: { ticket: Ticket; disabled: boolean; onSave: (labels: string[], onSettled: (saved: boolean) => void) => void }) {
  const { t } = useTranslation();
  const known = useLabels(ticket.projectId);
  const stored = ticket.labels ?? [];
  const storedKey = stored.join('\n');
  const [tags, setTags] = useState(stored);
  const wanted = useRef(stored);
  const saving = useRef(false);
  const [editing, setEditing] = useState(false);

  useEffect(() => {
    if (!saving.current) {
      const next = storedKey ? storedKey.split('\n') : [];
      wanted.current = next;
      setTags(next);
    }
  }, [storedKey]);

  const tagList = tags.length > 0 ? (
    <span className="flex flex-wrap gap-1">
      {tags.map((tag) => (
        <span
          key={tag}
          data-tag
          className="px-1"
          style={{ fontSize: font.size.xs, border: `1px solid ${sem('text-tertiary')}`, borderRadius: 4 }}
        >
          {tag}
        </span>
      ))}
    </span>
  ) : t('common.none');

  if (disabled) {
    return tagList;
  }

  if (!editing) {
    return (
      <ButtonBase
        onClick={() => setEditing(true)}
        className="w-full"
        sx={{ justifyContent: 'flex-start', minHeight: 32, paddingInline: 1, fontSize: 'inherit', borderRadius: `${radius.control}px`, '&:hover': { backgroundColor: sem('bg-hover') } }}
      >
        <span className="sr-only">{t('tickets.labels')}:</span>
        {tagList}
      </ButtonBase>
    );
  }

  const push = () => {
    if (saving.current) {
      return;
    }
    saving.current = true;
    const sent = wanted.current;
    onSave(sent, (saved) => {
      saving.current = false;
      if (!saved) {
        wanted.current = stored;
        setTags(stored);
      } else if (wanted.current !== sent) {
        push();
      }
    });
  };

  return (
    <div
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setEditing(false);
        }
      }}
    >
      <TagInput
        hideLabel
        autoFocus
        label={t('tickets.labels')}
        value={tags}
        options={known.data ?? []}
        onChange={(next) => {
          wanted.current = next;
          setTags(next);
          push();
        }}
      />
    </div>
  );
}

function Attachments({ ticket, canEdit }: { ticket: Ticket; canEdit: boolean }) {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState<{ name: string; percent: number } | null>(null);
  // Kept after a failure, so the same file can be sent again with one click.
  const [failed, setFailed] = useState<{ file: File; message: string } | null>(null);

  const list = useQuery({
    queryKey: ['tickets', ticket.id, 'attachments'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/attachment', { params: { query: { ticketId: ticket.id } } })),
  });

  const upload = useMutation({
    mutationFn: (file: File) =>
      uploadAttachment(ticket.id!, file, (loaded, total) => setUploading({ name: file.name, percent: total ? Math.floor((loaded / total) * 100) : 0 })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tickets', ticket.id, 'attachments'] }),
    onError: (failure, file) => setFailed({ file, message: toMessage(failure) }),
    onSettled: () => setUploading(null),
  });

  const start = (file: File) => {
    setFailed(null);
    setUploading({ name: file.name, percent: 0 });
    upload.mutate(file);
  };

  const onPick = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      start(file);
    }
  };

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/attachment/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tickets', ticket.id] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const download = async (id: string, name: string) => {
    const { response } = await apiClient.GET('/api/v1/attachment/{id}/download', { params: { path: { id } }, parseAs: 'stream' });
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <section className="flex flex-col gap-2">
      <div className="flex items-center">
        <Typography variant="h2" component="h2" className="flex-1">{t('tickets.attachments')}</Typography>
        {canEdit && (
          <>
            <input ref={input} type="file" hidden aria-label={t('tickets.upload')} onChange={onPick} />
            <Button kind="link" startIcon={<UploadIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={() => input.current?.click()}>
              {t('tickets.upload')}
            </Button>
          </>
        )}
      </div>
      {uploading && (
        <Typography role="status" variant="body2" color="textSecondary">
          {t('tickets.uploadingPercent', { name: uploading.name, percent: uploading.percent })}
        </Typography>
      )}
      {failed && (
        <div role="alert" className="flex items-center gap-2">
          <Typography variant="body2" className="min-w-0 flex-1" sx={{ color: sem('feedback-danger-text') }}>
            {t('tickets.uploadFailed', { name: failed.file.name, message: failed.message })}
          </Typography>
          <Button kind="secondary" onClick={() => start(failed.file)}>{t('common.retry')}</Button>
          <Button kind="ghost" onClick={() => setFailed(null)}>{t('common.dismiss')}</Button>
        </div>
      )}
      {list.data?.length === 0 && !uploading && <Typography variant="body2" color="textSecondary">{t('tickets.noAttachments')}</Typography>}
      <div className="flex flex-wrap gap-2">
        {(list.data ?? []).map((attachment) => (
          <div
            key={attachment.id}
            className="flex items-center gap-1 pr-1"
            style={{ width: 240, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
          >
            <button
              type="button"
              className="flex min-w-0 flex-1 flex-col items-start px-3 py-2 text-left"
              title={formatDateTime(attachment.createdAt, i18n.language)}
              style={{ background: 'none', border: 0, cursor: 'pointer' }}
              onClick={() => void download(attachment.id!, attachment.fileName!)}
            >
              <span className="w-full truncate" style={{ fontSize: font.size.xs }}>{attachment.fileName}</span>
              <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{formatSize(attachment.size ?? 0)}</span>
            </button>
            {canEdit && (
              <IconButton size="small" aria-label={`${t('common.delete')} ${attachment.fileName}`} onClick={() => remove.mutate(attachment.id!)}>
                <DeleteIcon size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

// A long title in the panel stops at three lines, so the pinned header leaves room to read the ticket.
const clampedTitle = { display: '-webkit-box', WebkitLineClamp: 3, WebkitBoxOrient: 'vertical', overflow: 'hidden' } as const;

/** Pins its children to the top of a panel, with a divider once the panel has scrolled; on the full page it adds nothing. */
function StickyHeader({ panel, children }: { panel?: PanelHeader; children: ReactNode }) {
  if (!panel) {
    return <>{children}</>;
  }

  return (
    <Box
      className="sticky top-0 -mx-6 flex flex-col gap-4 px-6 pb-3 pt-6"
      sx={{
        zIndex: 2,
        // Clear until something scrolls under it; then the panel's own background, frosted, hides what passes beneath.
        backgroundColor: panel.scrolled ? 'var(--sb-overlay-bg)' : 'transparent',
        backdropFilter: panel.scrolled ? 'var(--sb-surface-filter)' : 'none',
        borderBottom: '1px solid',
        borderColor: panel.scrolled ? sem('border-default') : 'transparent',
      }}
    >
      {panel.close}
      {children}
    </Box>
  );
}
