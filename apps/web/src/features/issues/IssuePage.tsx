import Box from '@mui/material/Box';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { DevelopmentPanel } from './DevelopmentPanel';
import { Ellipsis, Eye, Link2, Plus, Trash2, Upload } from 'lucide-react';
import { useEffect, useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import { ApiError, apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Avatar, UnassignedAvatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { CollapseHeading } from '../../components/CollapseHeading';
import { DialogConfirm } from '../../components/Dialog';
import { IssueTypeIcon, PriorityIcon, type IssueType as IconType } from '../../components/IssueIcons';
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
import { hasSelectedText } from '../../lib/selection';
import { formatDateTime, formatRelative, formatSize } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { useMembers, useSpace } from '../spaces/api';
import { IssueActivityPanel } from './IssueActivityPanel';
import { QuickAssignee, QuickStatus, QuickTitle, TitleEditor } from './QuickFields';
import { TransitionControl } from './TransitionControl';
import { EpicProgress } from '../agile/EpicProgress';
import { backlogKey, useBacklog } from '../agile/api';
import { RichTextEditor, RichTextView } from './RichTextEditor';
import { priorityLevel, uploadAttachment, useEpics, useIssueTypes, useLabels, usePriorities, type Issue } from './api';
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

const useIssue = (key: string | undefined) =>
  useQuery({
    queryKey: ['issues', key],
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/by-key', { params: { query: { key } } })),
    retry: (count, failure) => !(failure instanceof ApiError && failure.status < 500) && count < 2,
  });

export function IssuePage() {
  const { issueKey } = useParams();
  return <IssueContent issueKey={issueKey} />;
}

/** Same content on the full page and in the board panel; compact stacks the details. */
export function IssueContent({ issueKey, compact }: { issueKey: string | undefined; compact?: boolean }) {
  const { t } = useTranslation();
  const issue = useIssue(issueKey);

  if (issue.isPending) {
    return <LoadingState />;
  }
  if (issue.isError) {
    return issue.error instanceof ApiError && issue.error.status < 500 ? (
      <Typography role="alert">{t('issues.notFound')}</Typography>
    ) : (
      <ErrorState onRetry={() => void issue.refetch()} />
    );
  }
  return <IssueView key={issue.data.id} issue={issue.data} compact={compact} />;
}

const SIDEBAR_WIDTH_KEY = 'sprint-boot.issue-sidebar-width';
// Wide enough that the assignee and reporter pickers show a full name next to the label column.
const SIDEBAR_MIN = 320;
const SIDEBAR_MAX = 560;
const SIDEBAR_STEP = 16;

const clampSidebar = (width: number) => Math.min(SIDEBAR_MAX, Math.max(SIDEBAR_MIN, width));

function useSidebarWidth() {
  const [width, setWidth] = useState(() => clampSidebar(Number(localStorage.getItem(SIDEBAR_WIDTH_KEY)) || 360));
  const change = (next: number) => {
    const clamped = clampSidebar(next);
    setWidth(clamped);
    localStorage.setItem(SIDEBAR_WIDTH_KEY, String(clamped));
  };
  return [width, change] as const;
}

/** The line only shows on hover or focus; dragging left widens the details. */
function SidebarResizer({ width, onChange }: { width: number; onChange: (width: number) => void }) {
  const { t } = useTranslation();

  const startDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    const startX = event.clientX;
    const startWidth = width;
    const handle = event.currentTarget;
    handle.setPointerCapture(event.pointerId);
    const move = (next: PointerEvent) => onChange(startWidth + startX - next.clientX);
    const stop = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', stop);
      handle.removeEventListener('pointercancel', stop);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', stop);
    handle.addEventListener('pointercancel', stop);
  };

  return (
    <Box
      role="separator"
      aria-orientation="vertical"
      aria-label={t('issues.resizeSidebar')}
      aria-valuemin={SIDEBAR_MIN}
      aria-valuemax={SIDEBAR_MAX}
      aria-valuenow={width}
      tabIndex={0}
      className="flex w-2 shrink-0 cursor-col-resize justify-center"
      onPointerDown={startDrag}
      onKeyDown={(event) => {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault();
          onChange(width + (event.key === 'ArrowLeft' ? SIDEBAR_STEP : -SIDEBAR_STEP));
        }
      }}
      sx={{
        touchAction: 'none',
        '&::after': { content: '""', width: '2px', backgroundColor: 'transparent' },
        '&:hover::after, &:focus-visible::after, &:active::after': { backgroundColor: sem('border-focus') },
      }}
    />
  );
}

function IssueView({ issue, compact }: { issue: Issue; compact?: boolean }) {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const space = useSpace(issue.projectKey ?? undefined);
  const members = useMembers(issue.projectId);
  const priorities = usePriorities();
  const epics = useEpics(issue.projectId);
  const canEdit = space.data?.permissions?.includes('Issue.Edit') ?? false;
  const me = useCurrentUser();
  const people = (members.data ?? []).map((member) => member.user!);

  const [conflict, setConflict] = useState(false);
  const [editingTitle, setEditingTitle] = useState(false);
  const [editingDescription, setEditingDescription] = useState(false);
  const [description, setDescription] = useState(issue.description ?? '');
  const [descriptionOpen, setDescriptionOpen] = useState(true);
  const [detailsOpen, setDetailsOpen] = useState(true);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [addingSubtask, setAddingSubtask] = useState(false);
  const [tab, setTab] = useState('comments');
  const [sidebarWidth, setSidebarWidth] = useSidebarWidth();
  const [fieldError, setFieldError] = useState<string | null>(null);

  const update = useMutation({
    mutationFn: (fields: Record<string, unknown>) =>
      unwrap(
        apiClient.PUT('/api/v1/issue/{id}', {
          params: { path: { id: issue.id! } },
          // From the cache: a second edit can run before this view renders the first result.
          body: {
            concurrencyStamp: queryClient.getQueryData<Issue>(['issues', issue.key])?.concurrencyStamp ?? issue.concurrencyStamp!,
            fields: fields as never,
          },
        }),
      ),
    onSuccess: (next) => {
      setFieldError(null);
      queryClient.setQueryData(['issues', issue.key], next);
      void queryClient.invalidateQueries({ queryKey: ['issues', issue.id, 'activity'] });
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
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/issue/{id}', { params: { path: { id: issue.id! } } })),
    onSuccess: async () => {
      toast('success', t('issues.deleted', { key: issue.key }));
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      await navigate(`/spaces/${issue.projectKey}/issues`);
    },
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setConfirmingDelete(false),
  });

  const saveTitle = (title: string) => {
    setEditingTitle(false);
    if (title.trim() && title !== issue.title) {
      update.mutate({ title });
    }
  };

  const copyLink = async () => {
    setMenuAnchor(null);
    // Not the current address: the drawer opens over a board or backlog.
    await navigator.clipboard.writeText(`${window.location.origin}/issues/${issue.key}`);
    toast('success', t('issues.linkCopied'));
  };

  return (
    <div className={compact ? 'flex flex-col gap-4' : 'flex h-full min-h-0 flex-col gap-4'}>
      {conflict && (
        <div
          role="alert"
          className="flex items-center gap-2 px-3 py-2"
          style={{ backgroundColor: sem('feedback-warning-bg'), color: sem('feedback-warning-text'), borderRadius: radius.control }}
        >
          <span className="flex-1">{t('issues.conflictBanner')}</span>
          <Button kind="secondary" onClick={() => { setConflict(false); void queryClient.invalidateQueries({ queryKey: ['issues', issue.key] }); }}>
            {t('common.reload')}
          </Button>
        </div>
      )}

      <nav aria-label="breadcrumb" className="flex items-center gap-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
        <Link component={RouterLink} to={`/spaces/${issue.projectKey}/backlog`} color="inherit">{issue.projectName}</Link>
        <span aria-hidden>/</span>
        {issue.parent && (
          <>
            <Link component={RouterLink} to={`/issues/${issue.parent.key}`} color="inherit" className="flex items-center gap-1">
              <IssueTypeIcon type={issue.parent.typeIcon as IconType} />
              {issue.parent.key}
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
          <TypeField issue={issue} canEdit={canEdit} onPick={(typeId) => update.mutate({ type: typeId })} />
          {issue.key}
          <Tooltip title={t('issues.copyLink')}>
            <IconButton className="copy-link" size="small" aria-label={t('issues.copyLink')} onClick={() => void copyLink()}>
              <Link2 size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </Tooltip>
        </Box>
      </nav>

      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          {editingTitle ? (
            <TitleEditor label={t('issues.titleField')} initial={issue.title ?? ''} onSave={saveTitle} onCancel={() => setEditingTitle(false)} />
          ) : (
            <Typography
              variant="h1"
              tabIndex={canEdit ? 0 : undefined}
              onClick={() => canEdit && !hasSelectedText() && setEditingTitle(true)}
              onKeyDown={(event) => event.key === 'Enter' && canEdit && setEditingTitle(true)}
            >
              {issue.title}
            </Typography>
          )}
        </div>
        <WatchControl issueId={issue.id!} />
        <IconButton size="small" aria-label={t('issues.more')} onClick={(event) => setMenuAnchor(event.currentTarget)}>
          <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
        <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
          <MenuItem icon={Link2} onClick={() => void copyLink()}>{t('issues.copyLink')}</MenuItem>
          <MenuItem icon={Trash2} danger onClick={() => { setMenuAnchor(null); setConfirmingDelete(true); }}>{t('common.delete')}</MenuItem>
        </Menu>
      </div>

      {/* On the full page the content and the sidebar scroll on their own; the panel has one scroll. */}
      <div className={compact ? 'flex flex-col gap-6' : 'flex min-h-0 flex-1 gap-3'}>
        <div className={compact ? 'flex min-w-0 flex-1 flex-col gap-6' : 'flex min-w-0 flex-1 flex-col gap-6 overflow-y-auto pr-2'}>
          <section className="flex flex-col gap-2">
            {issue.description && !editingDescription ? (
              <CollapseHeading variant="h2" label={t('issues.description')} open={descriptionOpen} onToggle={() => setDescriptionOpen(!descriptionOpen)} />
            ) : (
              <Typography variant="h2" component="h2">{t('issues.description')}</Typography>
            )}
            {editingDescription ? (
              <>
                <RichTextEditor label={t('issues.description')} initialValue={issue.description ?? ''} people={people} projectId={issue.projectId} autoFocus onChange={setDescription} />
                <div className="flex gap-2">
                  <Button onClick={() => { setEditingDescription(false); update.mutate({ description }); }}>{t('common.save')}</Button>
                  <Button kind="ghost" onClick={() => setEditingDescription(false)}>{t('common.cancel')}</Button>
                </div>
              </>
            ) : issue.description ? (
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
                sx={{ padding: 1, borderRadius: `${radius.control}px`, '&:hover': { backgroundColor: sem('bg-page') } }}
              >
                <RichTextView html={issue.description} />
              </Box>
            ) : canEdit ? (
              <div><Button kind="ghost" onClick={() => setEditingDescription(true)}>{t('issues.addDescription')}</Button></div>
            ) : null}
          </section>

          {issue.type?.hierarchy !== 0 && (
            <section className="flex flex-col">
              <div className="flex items-center pb-2">
                <Typography variant="h2" component="h2" className="flex-1">{t(issue.type?.hierarchy === 2 ? 'agile.childIssues' : 'issues.subtasks')}</Typography>
                {canEdit && issue.type?.hierarchy === 1 && (
                  <Button kind="link" startIcon={<Plus size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setAddingSubtask(true)}>
                    {t('issues.addSubtask')}
                  </Button>
                )}
              </div>
              {issue.type?.hierarchy === 2 && <EpicProgress total={issue.children?.length ?? 0} done={countIn(issue, 2)} inProgress={countIn(issue, 1)} />}
              {(issue.children?.length ?? 0) > 0 && (
                <table className="w-full" aria-label={t(issue.type?.hierarchy === 2 ? 'agile.childIssues' : 'issues.subtasks')} style={{ borderCollapse: 'collapse', borderTop: `1px solid ${sem('border-default')}` }}>
                  <tbody>
                    {issue.children?.map((child) => (
                      <Box
                        component="tr"
                        key={child.id}
                        sx={{ height: 36, borderBottom: `1px solid ${sem('border-default')}`, '&:hover': { backgroundColor: sem('bg-hover') } }}
                      >
                        <td className="pl-2 pr-3"><span className="flex"><IssueTypeIcon type={child.typeIcon as IconType} /></span></td>
                        <td className="whitespace-nowrap pr-3">
                          <Link component={RouterLink} to={`/issues/${child.key}`} color="inherit" variant="body2">{child.key}</Link>
                        </td>
                        {/* Zero max-width with full width lets the title truncate and take the spare room. */}
                        <td className="w-full pr-3" style={{ maxWidth: 0 }}>
                          <span className="flex"><QuickTitle issue={child} canEdit={canEdit} /></span>
                        </td>
                        <td className="whitespace-nowrap pr-3"><QuickAssignee issue={child} canEdit={canEdit} people={people} showName /></td>
                        <td className="whitespace-nowrap pr-12"><QuickStatus issue={child} canEdit={canEdit} people={people} /></td>
                      </Box>
                    ))}
                  </tbody>
                </table>
              )}
              {addingSubtask && <SubtaskRow issue={issue} onClose={() => setAddingSubtask(false)} />}
            </section>
          )}

          <Attachments issue={issue} canEdit={canEdit} />

          <section className="flex flex-col gap-3">
            <Tabs
              label={t('issues.tabComments')}
              value={tab}
              onChange={setTab}
              items={[
                { value: 'all', label: t('issues.tabAll') },
                { value: 'comments', label: t('issues.tabComments') },
                { value: 'history', label: t('issues.tabHistory') },
                { value: 'worklog', label: t('issues.tabWorkLog') },
              ]}
            />
            <IssueActivityPanel tab={tab} issue={issue} people={people} canEdit={canEdit} canManage={space.data?.permissions?.includes('Comment.DeleteAny') ?? false} />
          </section>
        </div>

        {!compact && <SidebarResizer width={sidebarWidth} onChange={setSidebarWidth} />}
        <div className={compact ? 'flex shrink-0 flex-col gap-4' : 'flex shrink-0 flex-col gap-4 overflow-y-auto'} style={{ width: compact ? '100%' : sidebarWidth }}>
        <aside
          className="flex shrink-0 flex-col gap-1 p-3"
          style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
        >
          <CollapseHeading label={t('issues.details')} open={detailsOpen} onToggle={() => setDetailsOpen(!detailsOpen)} />
          {detailsOpen && (
          <>
          <DetailRow label={t('issues.status')}>
            <TransitionControl issue={issue} people={people} onConflict={() => setConflict(true)} />
          </DetailRow>
          <DetailRow label={t('issues.assignee')}>
            {canEdit ? (
              <UserPicker
                hideLabel
                label={t('issues.assignee')}
                options={people}
                value={issue.assignee ?? null}
                emptyLabel={t('common.unassigned')}
                onChange={(user) => update.mutate({ assignee: user?.id ?? null })}
              />
            ) : (
              <Person name={issue.assignee?.name} src={issue.assignee?.avatarUrl} />
            )}
          </DetailRow>
          {canEdit && !issue.assignee && (
            <div style={{ paddingLeft: DETAIL_LABEL_WIDTH + DETAIL_GAP }}>
              <Button kind="link" sx={{ minHeight: 0, padding: 0 }} onClick={() => update.mutate({ assignee: me.id })}>{t('issues.assignToMe')}</Button>
            </div>
          )}
          <DetailRow label={t('issues.reporter')}>
            {canEdit ? (
              <UserPicker
                hideLabel
                label={t('issues.reporter')}
                options={people}
                value={issue.reporter ?? null}
                onChange={(user) => update.mutate({ reporter: user?.id ?? null })}
              />
            ) : (
              <Person name={issue.reporter?.name} src={issue.reporter?.avatarUrl} />
            )}
          </DetailRow>
          <DetailRow label={t('issues.priority')}>
            {canEdit ? (
              <SelectField
                quiet
                label={t('issues.priority')}
                value={issue.priority?.id ?? ''}
                onChange={(event) => update.mutate({ priority: event.target.value })}
                options={(priorities.data ?? []).map((item) => ({
                  value: item.id!,
                  label: item.name!,
                  icon: <PriorityIcon priority={priorityLevel(item)} />,
                }))}
              />
            ) : (
              <span className="flex items-center gap-2"><PriorityIcon priority={priorityLevel(issue.priority)} />{issue.priority?.name}</span>
            )}
          </DetailRow>
          {issue.type?.hierarchy === 1 && space.data?.type === 0 && (
            <DetailRow label={t('agile.sprint')}>
              <SprintField issue={issue} canEdit={canEdit} onError={(failure) => toast('error', toMessage(failure))} />
            </DetailRow>
          )}
          {issue.type?.hierarchy === 1 && (
            <DetailRow label={t('issues.epic')}>
              <EpicField issue={issue} canEdit={canEdit} epics={epics.data?.items ?? []} onPick={(epicId) => update.mutate({ parent: epicId })} />
            </DetailRow>
          )}
          <DetailRow label={t('issues.labels')}>
            <LabelsField
              issue={issue}
              disabled={!canEdit}
              onSave={(labels, onSettled) => update.mutate({ labels }, { onSettled: (_, failure) => onSettled(!failure) })}
            />
          </DetailRow>
          <DetailRow label={t('issues.points')}>
            <PointsField
              value={issue.storyPoints?.toString() ?? ''}
              disabled={!canEdit}
              onSave={(text, onSettled) => update.mutate({ storyPoints: text ? Number(text) : null }, { onSettled })}
            />
          </DetailRow>
          <DetailRow label={t('issues.startDate')}>
            <DatePicker quiet label={t('issues.startDate')} value={issue.startDate ?? ''} disabled={!canEdit} onChange={(text) => update.mutate({ startDate: text || null })} />
          </DetailRow>
          <DetailRow label={t('issues.dueDate')}>
            <DatePicker quiet label={t('issues.dueDate')} value={issue.dueDate ?? ''} disabled={!canEdit} onChange={(text) => update.mutate({ dueDate: text || null })} />
          </DetailRow>
          {fieldError && <Typography role="alert" variant="body2" color="error">{fieldError}</Typography>}
          </>
          )}
        </aside>
        <DevelopmentPanel issueId={issue.id!} label={`${issue.key} ${issue.title}`} canEdit={canEdit} />
        <div className="flex shrink-0 flex-col gap-2 px-3" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
          <span title={formatDateTime(issue.createdAt, i18n.language)}>{t('issues.createdAt')} {formatRelative(issue.createdAt, i18n.language)}</span>
          <span title={formatDateTime(issue.updatedAt, i18n.language)}>{t('issues.updatedAt')} {formatRelative(issue.updatedAt, i18n.language)}</span>
        </div>
        </div>
      </div>

      <DialogConfirm
        open={confirmingDelete}
        title={t('issues.deleteTitle', { key: issue.key })}
        description={issue.children?.length ? t('issues.deleteBodySubtasks', { count: issue.children.length }) : t('issues.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setConfirmingDelete(false)}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}

/** Only types of the same level: a task cannot become an epic or a subtask here. */
function TypeField({ issue, canEdit, onPick }: { issue: Issue; canEdit: boolean; onPick: (typeId: string) => void }) {
  const { t } = useTranslation();
  const types = useIssueTypes(issue.projectId);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const siblings = (types.data ?? []).filter((type) => type.hierarchy === issue.type?.hierarchy);
  const icon = <IssueTypeIcon type={issue.type?.icon as IconType} />;

  if (!canEdit || siblings.length < 2) {
    return icon;
  }

  return (
    <>
      <ButtonBase
        aria-label={`${t('issues.issueType')}: ${t(`issue.type.${issue.type?.icon}`)}`}
        aria-haspopup="dialog"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ width: 24, height: 24, borderRadius: `${radius.control}px`, '&:hover': { backgroundColor: sem('bg-hover') } }}
      >
        <span aria-hidden className="flex">{icon}</span>
      </ButtonBase>
      <PickerPopover
        label={t('issues.issueType')}
        anchor={anchor}
        selectedId={issue.type?.id}
        items={siblings.map((type) => ({ id: type.id!, label: t(`issue.type.${type.icon}`), icon: <IssueTypeIcon type={type.icon as IconType} /> }))}
        onClose={() => setAnchor(null)}
        onPick={(typeId) => {
          setAnchor(null);
          if (typeId !== issue.type?.id) {
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

function EpicField({ issue, canEdit, epics, onPick }: { issue: Issue; canEdit: boolean; epics: { id?: string; key?: string | null; title?: string | null }[]; onPick: (epicId: string | null) => void }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const parent = issue.parent;
  const name = parent ? `${parent.key} ${parent.title}` : null;
  const tag = name ? <span style={epicTagStyle} title={name}>{name}</span> : <span>{t('common.none')}</span>;

  if (!canEdit) {
    return parent ? <RouterLink to={`/issues/${parent.key}`} style={{ textDecoration: 'none' }}>{tag}</RouterLink> : tag;
  }

  return (
    <>
      <ButtonBase
        aria-haspopup="dialog"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ justifyContent: 'flex-start', width: '100%', minHeight: 32, paddingInline: 1, fontSize: 'inherit', borderRadius: `${radius.control}px` }}
      >
        <span className="sr-only">{t('issues.epic')}:</span>
        {tag}
      </ButtonBase>
      <PickerPopover
        label={t('issues.epic')}
        searchLabel={t('issues.searchEpics')}
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

const countIn = (issue: Issue, category: number) =>
  (issue.children ?? []).filter((child) => child.status?.category === category).length;

function SprintField({ issue, canEdit, onError }: { issue: Issue; canEdit: boolean; onError: (failure: unknown) => void }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const backlog = useBacklog(issue.projectId);
  const current = useQuery({
    queryKey: ['issues', issue.id, 'sprint'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/sprint/for-issue/{issueId}', { params: { path: { issueId: issue.id! } } })),
  });

  const move = useMutation({
    mutationFn: (sprintId: string) =>
      unwrap(
        apiClient.POST('/api/v1/sprint/move-issue/{issueId}', {
          params: { path: { issueId: issue.id! } },
          body: { sprintId: sprintId || null },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issues', issue.id] });
      await queryClient.invalidateQueries({ queryKey: backlogKey(issue.projectId) });
    },
    onError,
  });

  if (!canEdit) {
    return <span>{current.data?.name ?? t('common.none')}</span>;
  }

  const closed = current.data?.state === 2;
  const options = (backlog.data?.sprints ?? []).map((item) => ({ value: item.sprint!.id!, label: item.sprint!.name! }));

  return (
    <SelectField
      quiet
      label={t('agile.sprint')}
      value={closed ? '' : (current.data?.id ?? '')}
      displayEmpty
      onChange={(event) => move.mutate(event.target.value)}
      options={[{ value: '', label: t('common.none') }, ...options]}
    />
  );
}

function WatchControl({ issueId }: { issueId: string }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const key = ['issues', issueId, 'watchers'];

  const watchers = useQuery({
    queryKey: key,
    queryFn: () => unwrap(apiClient.GET('/api/v1/watcher', { params: { query: { issueId } } })),
  });

  const toggle = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST(watchers.data?.watchedByMe ? '/api/v1/watcher/unwatch/{issueId}' : '/api/v1/watcher/watch/{issueId}', { params: { path: { issueId } } })),
    onSuccess: (next) => queryClient.setQueryData(key, next),
  });

  return (
    <>
      <Button
        kind="ghost"
        aria-label={t('issues.watchers', { count: watchers.data?.count ?? 0 })}
        startIcon={<Eye size={16} strokeWidth={1.5} aria-hidden />}
        onClick={(event) => setAnchor(event.currentTarget)}
      >
        {watchers.data?.count ?? ''}
      </Button>
      <Popover width={264} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <div className="flex flex-col gap-2">
          <Button kind="secondary" onClick={() => toggle.mutate()}>
            {t(watchers.data?.watchedByMe ? 'issues.stopWatching' : 'issues.startWatching')}
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
function SubtaskRow({ issue, onClose }: { issue: Issue; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const types = useIssueTypes(issue.projectId);
  const typeId = types.data?.find((type) => type.hierarchy === 0)?.id;
  const [title, setTitle] = useState('');
  const input = useRef<HTMLInputElement>(null);

  const create = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/issue', { body: { projectId: issue.projectId!, typeId: typeId!, title: title.trim(), parentId: issue.id } })),
    onSuccess: async () => {
      setTitle('');
      input.current?.focus();
      await queryClient.invalidateQueries({ queryKey: ['issues', issue.key] });
    },
  });
  const ready = Boolean(title.trim() && typeId) && !create.isPending;

  return (
    <div className="flex flex-col gap-2 px-2">
      <div className="flex items-center gap-3">
        <IssueTypeIcon type="subtask" />
        <div className="min-w-0 flex-1">
          <TextField
            quiet
            label={t('issues.subtaskName')}
            autoFocus
            inputRef={input}
            placeholder={t('issues.subtaskPlaceholder')}
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
        <Button disabled={!ready} onClick={() => create.mutate()}>{t('issues.createAction')}</Button>
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
      label={t('issues.points')}
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
function LabelsField({ issue, disabled, onSave }: { issue: Issue; disabled: boolean; onSave: (labels: string[], onSettled: (saved: boolean) => void) => void }) {
  const { t } = useTranslation();
  const known = useLabels(issue.projectId);
  const stored = issue.labels ?? [];
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
        <span className="sr-only">{t('issues.labels')}:</span>
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
        label={t('issues.labels')}
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

function Attachments({ issue, canEdit }: { issue: Issue; canEdit: boolean }) {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState<string | null>(null);

  const list = useQuery({
    queryKey: ['issues', issue.id, 'attachments'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/attachment', { params: { query: { issueId: issue.id } } })),
  });

  const upload = useMutation({
    mutationFn: (file: File) => uploadAttachment(issue.id!, file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['issues', issue.id, 'attachments'] }),
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setUploading(null),
  });

  const onPick = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      setUploading(file.name);
      upload.mutate(file);
    }
  };

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/attachment/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['issues', issue.id] }),
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
        <Typography variant="h2" component="h2" className="flex-1">{t('issues.attachments')}</Typography>
        {canEdit && (
          <>
            <input ref={input} type="file" hidden aria-label={t('issues.upload')} onChange={onPick} />
            <Button kind="link" startIcon={<Upload size={16} strokeWidth={1.5} aria-hidden />} onClick={() => input.current?.click()}>
              {t('issues.upload')}
            </Button>
          </>
        )}
      </div>
      {uploading && <Typography role="status" variant="body2" color="textSecondary">{t('issues.uploading', { name: uploading })}</Typography>}
      {list.data?.length === 0 && !uploading && <Typography variant="body2" color="textSecondary">{t('issues.noAttachments')}</Typography>}
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
                <Trash2 size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
