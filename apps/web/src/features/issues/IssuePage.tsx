import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ellipsis, Link2, Plus, Trash2, Upload } from 'lucide-react';
import { useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import { ApiError, apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { StatusBadge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { DialogConfirm } from '../../components/Dialog';
import { IssueTypeIcon, PriorityIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { MenuItem } from '../../components/Menu';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { Tabs } from '../../components/Tabs';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { UserPicker } from '../../components/UserPicker';
import { useErrorMessage } from '../../lib/errors';
import { formatDateTime, formatSize } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { useMembers, useSpace } from '../spaces/api';
import { IssueActivityPanel } from './IssueActivityPanel';
import { CreateIssueDialog } from './CreateIssueDialog';
import { RichTextEditor, RichTextView } from './RichTextEditor';
import { priorityLevel, statusCategory, usePriorities, type Issue } from './api';

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-h-9 items-center gap-3">
      <span style={{ width: 88, flexShrink: 0, fontSize: font.size.xs, color: sem('text-tertiary') }}>{label}</span>
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
  return <IssueView key={issue.data.id} issue={issue.data} />;
}

function IssueView({ issue }: { issue: Issue }) {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const space = useSpace(issue.projectKey ?? undefined);
  const members = useMembers(issue.projectId);
  const priorities = usePriorities();
  const canEdit = space.data?.permissions?.includes('Issue.Edit') ?? false;
  const people = (members.data ?? []).map((member) => member.user!);

  const [conflict, setConflict] = useState(false);
  const [editingTitle, setEditingTitle] = useState(false);
  const [title, setTitle] = useState(issue.title ?? '');
  const [editingDescription, setEditingDescription] = useState(false);
  const [description, setDescription] = useState(issue.description ?? '');
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [addingSubtask, setAddingSubtask] = useState(false);
  const [tab, setTab] = useState('comments');
  const [fieldError, setFieldError] = useState<string | null>(null);

  const update = useMutation({
    mutationFn: (fields: Record<string, unknown>) =>
      unwrap(
        apiClient.PUT('/api/v1/issue/{id}', {
          params: { path: { id: issue.id! } },
          body: { concurrencyStamp: issue.concurrencyStamp!, fields: fields as never },
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

  const saveTitle = () => {
    setEditingTitle(false);
    if (title.trim() && title !== issue.title) {
      update.mutate({ title });
    } else {
      setTitle(issue.title ?? '');
    }
  };

  const copyLink = async () => {
    setMenuAnchor(null);
    await navigator.clipboard.writeText(window.location.href);
    toast('success', t('issues.linkCopied'));
  };

  return (
    <div className="flex flex-col gap-4">
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
        <Link component={RouterLink} to="/spaces" color="inherit">{t('spaces.title')}</Link>
        <span aria-hidden>/</span>
        <Link component={RouterLink} to={`/spaces/${issue.projectKey}/issues`} color="inherit">{issue.projectName}</Link>
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
        <span className="flex items-center gap-1" style={{ color: sem('text-primary') }}>
          <IssueTypeIcon type={issue.type?.icon as IconType} />
          {issue.key}
        </span>
      </nav>

      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          {editingTitle ? (
            <TextField
              label={t('issues.titleField')}
              autoFocus
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              onBlur={saveTitle}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  saveTitle();
                }
                if (event.key === 'Escape') {
                  setTitle(issue.title ?? '');
                  setEditingTitle(false);
                }
              }}
            />
          ) : (
            <Typography
              variant="h1"
              tabIndex={canEdit ? 0 : undefined}
              onClick={() => canEdit && setEditingTitle(true)}
              onKeyDown={(event) => event.key === 'Enter' && canEdit && setEditingTitle(true)}
            >
              {issue.title}
            </Typography>
          )}
        </div>
        <IconButton size="small" aria-label={t('issues.more')} onClick={(event) => setMenuAnchor(event.currentTarget)}>
          <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
        <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
          <MenuItem icon={Link2} onClick={() => void copyLink()}>{t('issues.copyLink')}</MenuItem>
          <MenuItem icon={Trash2} danger onClick={() => { setMenuAnchor(null); setConfirmingDelete(true); }}>{t('common.delete')}</MenuItem>
        </Menu>
      </div>

      <div className="flex items-start gap-8">
        <div className="flex min-w-0 flex-1 flex-col gap-6">
          <section className="flex flex-col gap-2">
            <Typography variant="h3" component="h2">{t('issues.description')}</Typography>
            {editingDescription ? (
              <>
                <RichTextEditor label={t('issues.description')} initialValue={issue.description ?? ''} people={people} autoFocus onChange={setDescription} />
                <div className="flex gap-2">
                  <Button onClick={() => { setEditingDescription(false); update.mutate({ description }); }}>{t('common.save')}</Button>
                  <Button kind="ghost" onClick={() => setEditingDescription(false)}>{t('common.cancel')}</Button>
                </div>
              </>
            ) : issue.description ? (
              <div
                role={canEdit ? 'button' : undefined}
                tabIndex={canEdit ? 0 : undefined}
                onClick={() => canEdit && setEditingDescription(true)}
                onKeyDown={(event) => event.key === 'Enter' && canEdit && setEditingDescription(true)}
              >
                <RichTextView html={issue.description} />
              </div>
            ) : canEdit ? (
              <div><Button kind="ghost" onClick={() => setEditingDescription(true)}>{t('issues.addDescription')}</Button></div>
            ) : null}
          </section>

          {issue.type?.hierarchy !== 0 && (
            <section className="flex flex-col gap-2">
              <div className="flex items-center">
                <Typography variant="h3" component="h2" className="flex-1">{t('issues.subtasks')}</Typography>
                {canEdit && issue.type?.hierarchy === 1 && (
                  <Button kind="ghost" startIcon={<Plus size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setAddingSubtask(true)}>
                    {t('issues.addSubtask')}
                  </Button>
                )}
              </div>
              {(issue.children ?? []).map((child) => (
                <div key={child.id} className="flex h-9 items-center gap-3 px-2">
                  <IssueTypeIcon type={child.typeIcon as IconType} />
                  <Link component={RouterLink} to={`/issues/${child.key}`} color="inherit" variant="body2">{child.key}</Link>
                  <span className="min-w-0 flex-1 truncate">{child.title}</span>
                  <StatusBadge status={statusCategory(child.status)} />
                </div>
              ))}
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

        <aside
          className="flex shrink-0 flex-col gap-1 p-3"
          style={{ width: 320, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
        >
          <Typography variant="h3" component="h2" className="pb-1">{t('issues.details')}</Typography>
          <DetailRow label={t('issues.status')}><StatusBadge status={statusCategory(issue.status)} /></DetailRow>
          <DetailRow label={t('issues.assignee')}>
            {canEdit ? (
              <UserPicker
                hideLabel
                label={t('issues.assignee')}
                options={people}
                value={issue.assignee ?? null}
                onChange={(user) => update.mutate({ assignee: user?.id ?? null })}
              />
            ) : (
              <Person name={issue.assignee?.name} />
            )}
          </DetailRow>
          <DetailRow label={t('issues.reporter')}><Person name={issue.reporter?.name} /></DetailRow>
          <DetailRow label={t('issues.priority')}>
            {canEdit ? (
              <SelectField
                label={t('issues.priority')}
                value={issue.priority?.id ?? ''}
                onChange={(event) => update.mutate({ priority: event.target.value })}
                options={(priorities.data ?? []).map((item) => ({ value: item.id!, label: item.name! }))}
              />
            ) : (
              <span className="flex items-center gap-2"><PriorityIcon priority={priorityLevel(issue.priority)} />{issue.priority?.name}</span>
            )}
          </DetailRow>
          {issue.parent && (
            <DetailRow label={t('issues.epic')}>
              <Link component={RouterLink} to={`/issues/${issue.parent.key}`}>{issue.parent.key} {issue.parent.title}</Link>
            </DetailRow>
          )}
          <DetailRow label={t('issues.labels')}>
            <EditableText
              label={t('issues.labels')}
              value={(issue.labels ?? []).join(', ')}
              disabled={!canEdit}
              onSave={(text) => update.mutate({ labels: text.split(',').map((item) => item.trim()).filter(Boolean) })}
            />
          </DetailRow>
          <DetailRow label={t('issues.points')}>
            <EditableText
              label={t('issues.points')}
              type="number"
              value={issue.storyPoints?.toString() ?? ''}
              disabled={!canEdit}
              onSave={(text) => update.mutate({ storyPoints: text ? Number(text) : null })}
            />
          </DetailRow>
          <DetailRow label={t('issues.startDate')}>
            <EditableText label={t('issues.startDate')} type="date" value={issue.startDate ?? ''} disabled={!canEdit} onSave={(text) => update.mutate({ startDate: text || null })} />
          </DetailRow>
          <DetailRow label={t('issues.dueDate')}>
            <EditableText label={t('issues.dueDate')} type="date" value={issue.dueDate ?? ''} disabled={!canEdit} onSave={(text) => update.mutate({ dueDate: text || null })} />
          </DetailRow>
          {fieldError && <Typography role="alert" variant="body2" color="error">{fieldError}</Typography>}
          <DetailRow label={t('issues.createdAt')}>{formatDateTime(issue.createdAt, i18n.language)}</DetailRow>
          <DetailRow label={t('issues.updatedAt')}>{formatDateTime(issue.updatedAt, i18n.language)}</DetailRow>
        </aside>
      </div>

      <DialogConfirm
        open={confirmingDelete}
        title={t('issues.deleteTitle', { key: issue.key })}
        description={issue.children?.length ? t('issues.deleteBodySubtasks', { count: issue.children.length }) : t('issues.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setConfirmingDelete(false)}
        onConfirm={() => remove.mutate()}
      />
      {addingSubtask && (
        <CreateIssueDialog
          parent={{ id: issue.id!, projectKey: issue.projectKey! }}
          onClose={() => setAddingSubtask(false)}
        />
      )}
    </div>
  );
}

function Person({ name }: { name?: string | null }) {
  const { t } = useTranslation();
  return name ? (
    <span className="flex items-center gap-2"><Avatar name={name} />{name}</span>
  ) : (
    <span style={{ color: sem('text-tertiary') }}>{t('common.unassigned')}</span>
  );
}

function EditableText({ label, value, type = 'text', disabled, onSave }: { label: string; value: string; type?: string; disabled: boolean; onSave: (value: string) => void }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<string | null>(null);

  if (disabled) {
    return <span>{value || t('common.none')}</span>;
  }

  const commit = () => {
    if (draft !== null && draft !== value) {
      onSave(draft);
    }
    setDraft(null);
  };

  return (
    <TextField
      label={label}
      type={type}
      value={draft ?? value}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => event.key === 'Enter' && commit()}
    />
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
    mutationFn: (file: File) =>
      unwrap(
        apiClient.POST('/api/v1/attachment/upload/{issueId}', {
          params: { path: { issueId: issue.id! } },
          body: { file } as never,
          bodySerializer: () => {
            const form = new FormData();
            form.append('file', file);
            return form;
          },
        }),
      ),
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
        <Typography variant="h3" component="h2" className="flex-1">{t('issues.attachments')}</Typography>
        {canEdit && (
          <>
            <input ref={input} type="file" hidden aria-label={t('issues.upload')} onChange={onPick} />
            <Button kind="ghost" startIcon={<Upload size={16} strokeWidth={1.5} aria-hidden />} onClick={() => input.current?.click()}>
              {t('issues.upload')}
            </Button>
          </>
        )}
      </div>
      {uploading && <Typography role="status" variant="body2" color="textSecondary">{t('issues.uploading', { name: uploading })}</Typography>}
      {list.data?.length === 0 && !uploading && <Typography variant="body2" color="textSecondary">{t('issues.noAttachments')}</Typography>}
      <div className="flex flex-wrap gap-2">
        {(list.data ?? []).map((attachment) => (
          <button
            key={attachment.id}
            type="button"
            className="flex flex-col items-start px-3 py-2 text-left"
            title={formatDateTime(attachment.createdAt, i18n.language)}
            style={{ width: 240, background: 'none', cursor: 'pointer', border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
            onClick={() => void download(attachment.id!, attachment.fileName!)}
          >
            <span className="w-full truncate" style={{ fontSize: font.size.xs }}>{attachment.fileName}</span>
            <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{formatSize(attachment.size ?? 0)}</span>
          </button>
        ))}
      </div>
    </section>
  );
}
