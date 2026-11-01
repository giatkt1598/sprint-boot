import IconButton from '@mui/material/IconButton';
import InputBase from '@mui/material/InputBase';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Reply, ThumbsUp, Trash2 } from 'lucide-react';
import { useState } from 'react';
import type { components } from '@sprint-boot/api-client';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { EmptyState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate, formatDateTime, formatMinutes, parseDuration } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { RichTextEditor, RichTextView, type MentionUser } from './RichTextEditor';
import type { Activity, Comment, Issue } from './api';

const LIKE = '👍';

interface PanelProps {
  tab: string;
  issue: Issue;
  people: MentionUser[];
  canEdit: boolean;
  canManage: boolean;
}

function useComments(issueId: string) {
  return useQuery({
    queryKey: ['issues', issueId, 'comments'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/comment', { params: { query: { issueId } } })),
  });
}

function useActivity(issueId: string) {
  return useQuery({
    queryKey: ['issues', issueId, 'activity'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/{id}/activity', { params: { path: { id: issueId } } })),
  });
}

function Composer({ issueId, parentId, people, placeholder, onDone }: { issueId: string; parentId?: string; people: MentionUser[]; placeholder: string; onDone?: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(Boolean(parentId));
  const [body, setBody] = useState('');

  const post = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/comment', { body: { issueId, parentId: parentId ?? null, body } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issues', issueId, 'comments'] });
      setOpen(false);
      setBody('');
      onDone?.();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  if (!open) {
    return (
      <InputBase
        placeholder={placeholder}
        inputProps={{ 'aria-label': placeholder }}
        onFocus={() => setOpen(true)}
        sx={{ height: 36, paddingInline: 3, borderRadius: `${radius.control}px`, backgroundColor: sem('bg-subtle'), '& input::placeholder': { color: sem('text-tertiary'), opacity: 1 } }}
      />
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <RichTextEditor label={placeholder} people={people} autoFocus onChange={setBody} />
      <div className="flex gap-2">
        <Button disabled={!body || post.isPending} onClick={() => post.mutate()}>{t('common.save')}</Button>
        <Button kind="ghost" onClick={() => { setOpen(false); onDone?.(); }}>{t('common.cancel')}</Button>
      </div>
    </div>
  );
}

function CommentItem({ comment, issue, replies, people, canEdit, canManage }: { comment: Comment; issue: Issue; replies: Comment[]; people: MentionUser[]; canEdit: boolean; canManage: boolean }) {
  const { t, i18n } = useTranslation();
  const me = useCurrentUser();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [replying, setReplying] = useState(false);
  const [editing, setEditing] = useState(false);
  const [body, setBody] = useState(comment.body ?? '');
  const isMine = comment.author?.id === me.id;
  const like = comment.reactions?.find((reaction) => reaction.emoji === LIKE);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['issues', issue.id, 'comments'] });
  const onError = (failure: unknown) => toast('error', toMessage(failure));

  const react = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/comment/{id}/toggle-reaction', { params: { path: { id: comment.id! } }, body: { emoji: LIKE } })),
    onSuccess: refresh,
    onError,
  });
  const save = useMutation({
    mutationFn: () => unwrap(apiClient.PUT('/api/v1/comment/{id}', { params: { path: { id: comment.id! } }, body: { body } })),
    onSuccess: async () => {
      await refresh();
      setEditing(false);
    },
    onError,
  });
  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/comment/{id}', { params: { path: { id: comment.id! } } })),
    onSuccess: refresh,
    onError,
  });

  return (
    <article className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <Avatar name={comment.author?.name ?? ''} />
        <span style={{ fontWeight: font.weight.medium }}>{comment.author?.name}</span>
        <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>
          {formatDateTime(comment.createdAt, i18n.language)}{comment.isEdited ? ` · ${t('issues.edited')}` : ''}
        </span>
      </div>
      <div className="pl-8">
        {editing ? (
          <div className="flex flex-col gap-2">
            <RichTextEditor label={t('common.edit')} initialValue={comment.body ?? ''} people={people} autoFocus onChange={setBody} />
            <div className="flex gap-2">
              <Button onClick={() => save.mutate()}>{t('common.save')}</Button>
              <Button kind="ghost" onClick={() => setEditing(false)}>{t('common.cancel')}</Button>
            </div>
          </div>
        ) : (
          <RichTextView html={comment.body ?? ''} />
        )}
        <div className="flex items-center gap-1">
          {canEdit && (
            <Button
              kind="ghost"
              aria-label={t('issues.like')}
              aria-pressed={like?.reactedByMe ?? false}
              startIcon={<ThumbsUp size={16} strokeWidth={1.5} aria-hidden />}
              onClick={() => react.mutate()}
            >
              {like?.count ?? ''}
            </Button>
          )}
          {!canEdit && (like?.count ?? 0) > 0 && <span style={{ fontSize: font.size.xs }}>{LIKE} {like?.count}</span>}
          {canEdit && !comment.parentId && (
            <Button kind="ghost" startIcon={<Reply size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setReplying(true)}>{t('issues.reply')}</Button>
          )}
          {isMine && (
            <IconButton size="small" aria-label={t('common.edit')} onClick={() => setEditing(true)}>
              <Pencil size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          )}
          {(isMine || canManage) && (
            <IconButton size="small" aria-label={t('common.delete')} onClick={() => remove.mutate()}>
              <Trash2 size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          )}
        </div>
        <div className="flex flex-col gap-3 pt-2">
          {replies.map((reply) => (
            <CommentItem key={reply.id} comment={reply} issue={issue} replies={[]} people={people} canEdit={canEdit} canManage={canManage} />
          ))}
          {replying && (
            <Composer issueId={issue.id!} parentId={comment.id} people={people} placeholder={t('issues.reply')} onDone={() => setReplying(false)} />
          )}
        </div>
      </div>
    </article>
  );
}

function ActivityLine({ item }: { item: Activity }) {
  const { t, i18n } = useTranslation();
  const field = i18n.exists(`issues.field.${item.field}`) ? t(`issues.field.${item.field}`) : item.field;
  const showValues = item.field !== 'description';

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <Avatar name={item.actor?.name ?? ''} />
        <span>{item.actor?.name} {t('issues.historyLine', { field })}</span>
        <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{formatDateTime(item.createdAt, i18n.language)}</span>
      </div>
      {showValues && (
        <Typography variant="body2" color="textSecondary" className="pl-8">
          {item.oldValue || t('common.none')} → {item.newValue || t('common.none')}
        </Typography>
      )}
    </div>
  );
}

type WorkLog = components['schemas']['WorkLogDto'];

function WorkLogDialog({ issueId, log: existing, onClose }: { issueId: string; log?: WorkLog; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [time, setTime] = useState(existing ? formatMinutes(existing.minutes ?? 0) : '');
  const [workDate, setWorkDate] = useState(existing?.workDate ?? new Date().toISOString().slice(0, 10));
  const [note, setNote] = useState(existing?.note ?? '');
  const [touched, setTouched] = useState(false);
  const minutes = parseDuration(time);
  const invalid = !minutes;

  const log = useMutation({
    mutationFn: () =>
      unwrap<unknown>(
        existing
          ? apiClient.PUT('/api/v1/work-log/{id}', { params: { path: { id: existing.id! } }, body: { minutes: minutes!, workDate, note: note || null } })
          : apiClient.POST('/api/v1/work-log', { body: { issueId, minutes: minutes!, workDate, note: note || null } }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issues', issueId] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('issues.logWork')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('issues.logWork')}
          confirmDisabled={log.isPending}
          onCancel={onClose}
          onConfirm={() => {
            setTouched(true);
            if (!invalid) {
              log.mutate();
            }
          }}
        />
      }
    >
      <TextField label={t('issues.timeSpent')} autoFocus placeholder={t('issues.timeHint')} value={time} error={touched && invalid ? t('issues.timeInvalid') : undefined} onChange={(event) => setTime(event.target.value)} />
      <TextField label={t('issues.workDate')} type="date" value={workDate} onChange={(event) => setWorkDate(event.target.value)} />
      <TextField label={t('issues.note')} value={note} onChange={(event) => setNote(event.target.value)} />
      {log.isError && <Typography role="alert" color="error" variant="body2">{toMessage(log.error)}</Typography>}
    </Dialog>
  );
}

function WorkLogPanel({ issue, canEdit, canManage }: { issue: Issue; canEdit: boolean; canManage: boolean }) {
  const { t, i18n } = useTranslation();
  const me = useCurrentUser();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [logging, setLogging] = useState(false);
  const [editingLog, setEditingLog] = useState<WorkLog | null>(null);

  const logs = useQuery({
    queryKey: ['issues', issue.id, 'worklogs'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/work-log', { params: { query: { issueId: issue.id } } })),
  });

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/work-log/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['issues', issue.id] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center">
        <span className="flex-1">{t('issues.totalLogged', { time: formatMinutes(logs.data?.totalMinutes ?? 0) })}</span>
        {canEdit && <Button kind="secondary" onClick={() => setLogging(true)}>{t('issues.logWork')}</Button>}
      </div>
      {logs.isPending && <LoadingState />}
      {logs.data?.items?.length === 0 && <EmptyState title={t('issues.noWorkLogs')} />}
      {(logs.data?.items ?? []).map((log) => (
        <div key={log.id} className="flex items-center gap-2">
          <Avatar name={log.author?.name ?? ''} />
          <span>{log.author?.name}</span>
          <span style={{ fontWeight: font.weight.medium }}>{formatMinutes(log.minutes ?? 0)}</span>
          <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{formatDate(log.workDate, i18n.language)}</span>
          <span className="flex-1 truncate">{log.note}</span>
          {log.author?.id === me.id && (
            <IconButton size="small" aria-label={t('common.edit')} onClick={() => setEditingLog(log)}>
              <Pencil size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          )}
          {(log.author?.id === me.id || canManage) && (
            <IconButton size="small" aria-label={t('common.delete')} onClick={() => remove.mutate(log.id!)}>
              <Trash2 size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          )}
        </div>
      ))}
      {logging && <WorkLogDialog issueId={issue.id!} onClose={() => setLogging(false)} />}
      {editingLog && <WorkLogDialog issueId={issue.id!} log={editingLog} onClose={() => setEditingLog(null)} />}
    </div>
  );
}

export function IssueActivityPanel({ tab, issue, people, canEdit, canManage }: PanelProps) {
  const { t } = useTranslation();
  const comments = useComments(issue.id!);
  const activity = useActivity(issue.id!);

  if (tab === 'worklog') {
    return <WorkLogPanel issue={issue} canEdit={canEdit} canManage={canManage} />;
  }

  const topLevel = (comments.data ?? []).filter((comment) => !comment.parentId);
  const repliesOf = (id?: string) => (comments.data ?? []).filter((comment) => comment.parentId === id);
  const renderComment = (comment: Comment) => (
    <CommentItem key={comment.id} comment={comment} issue={issue} replies={repliesOf(comment.id)} people={people} canEdit={canEdit} canManage={canManage} />
  );

  if (tab === 'history') {
    return activity.data?.length === 0 ? (
      <EmptyState title={t('issues.noHistory')} />
    ) : (
      <div className="flex flex-col gap-3">{(activity.data ?? []).map((item) => <ActivityLine key={item.id} item={item} />)}</div>
    );
  }

  const feed =
    tab === 'all'
      ? [
          ...topLevel.map((comment) => ({ at: comment.createdAt ?? '', node: renderComment(comment) })),
          ...(activity.data ?? []).map((item) => ({ at: item.createdAt ?? '', node: <ActivityLine key={item.id} item={item} /> })),
        ].sort((a, b) => a.at.localeCompare(b.at))
      : topLevel.map((comment) => ({ at: '', node: renderComment(comment) }));

  return (
    <div className="flex flex-col gap-4">
      {canEdit && <Composer issueId={issue.id!} people={people} placeholder={t('issues.addComment')} />}
      {comments.isSuccess && feed.length === 0 && <EmptyState title={t('issues.noComments')} />}
      {feed.map((entry) => entry.node)}
    </div>
  );
}
