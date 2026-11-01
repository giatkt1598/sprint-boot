import IconButton from '@mui/material/IconButton';
import InputBase from '@mui/material/InputBase';
import type { PopoverActions } from '@mui/material/Popover';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ellipsis, Link2, Pencil, Reply, SmilePlus, ThumbsUp, Trash2 } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ComponentProps } from 'react';
import type { components } from '@sprint-boot/api-client';
import { useTranslation } from 'react-i18next';
import Menu from '@mui/material/Menu';
import { useSearchParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { DatePicker } from '../../components/DatePicker';
import { MenuItem } from '../../components/Menu';
import { Popover } from '../../components/Popover';
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
const QUICK_EMOJIS = [LIKE, '👎', '❤️', '🌸', '🔥', '😂', '😢', '🚀'];
const MORE_EMOJIS = [
  '😀', '😃', '😄', '😁', '😆', '😅', '🤣', '🙂', '😉', '😊', '😇', '🥰', '😍', '🤩', '😘', '😋',
  '😜', '🤔', '🤨', '😐', '😑', '🙄', '😏', '😴', '😎', '🤓', '😮', '😲', '😳', '🥺', '😭', '😱',
  '😤', '😡', '🤯', '🥳', '🤝', '👏', '🙌', '🙏', '💪', '👌', '✌️', '🤞', '👀', '💯', '✅', '❌',
  '⭐', '✨', '🎉', '🎊', '🏆', '💡', '🐛', '🔧', '📌', '📎', '☕', '🍕', '🌈', '☀️', '⚡', '💙',
];
const COMMENT_PAGE = 3;
const REPLY_PAGE = 2;
const LOAD_MORE_STEP = 6;

const roundSx = (pressed = false) => ({
  // MUI's faint default colour also fades colour emoji.
  color: sem('text-primary'),
  borderRadius: '50%',
  backgroundColor: pressed ? sem('bg-selected') : undefined,
  '&:hover': { backgroundColor: pressed ? sem('bg-selected') : sem('bg-hover') },
});

const reactionChipSx = (mine: boolean) => ({
  minWidth: 0,
  minHeight: 0,
  height: 24,
  padding: '0 6px',
  fontSize: font.size.xs,
  borderRadius: '12px',
  backgroundColor: mine ? sem('bg-selected') : undefined,
});

function ActionButton({ label, pressed, ...props }: { label: string; pressed?: boolean } & ComponentProps<typeof IconButton>) {
  return <Tooltip title={label}><RoundButton label={label} pressed={pressed} {...props} /></Tooltip>;
}

function RoundButton({ label, pressed, ...props }: { label: string; pressed?: boolean } & ComponentProps<typeof IconButton>) {
  return <IconButton size="small" aria-label={label} aria-pressed={pressed} sx={roundSx(pressed)} {...props} />;
}

function EmojiPicker({ used, onPick, onResize }: { used: Set<string>; onPick: (emoji: string) => void; onResize: () => void }) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);

  // The popover grows upward from its trigger, so it must be placed again.
  useEffect(onResize, [expanded, onResize]);

  const emojiButton = (emoji: string) => (
    <RoundButton key={emoji} label={t('issues.reactWith', { emoji })} pressed={used.has(emoji)} onClick={() => onPick(emoji)}>
      <span style={{ fontSize: font.size.lg, lineHeight: 1 }}>{emoji}</span>
    </RoundButton>
  );

  return (
    <div className="flex flex-col-reverse gap-1 p-1">
      <div className="flex items-center gap-1">
        {QUICK_EMOJIS.map(emojiButton)}
        <ActionButton label={t('issues.moreReactions')} aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
          <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
        </ActionButton>
      </div>
      {expanded && <div className="flex max-h-48 flex-wrap gap-1 overflow-y-auto">{MORE_EMOJIS.map(emojiButton)}</div>}
    </div>
  );
}

function LoadMore({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation();
  return <Button kind="link" sx={{ alignSelf: 'flex-start' }} onClick={onClick}>{t('issues.loadMore')}</Button>;
}

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
      // Closes first: text typed during the refetch would vanish with the editor.
      setOpen(false);
      setBody('');
      onDone?.();
      await queryClient.invalidateQueries({ queryKey: ['issues', issueId, 'comments'] });
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
  const [emojiAnchor, setEmojiAnchor] = useState<HTMLElement | null>(null);
  const emojiPopover = useRef<PopoverActions>(null);
  const placeEmojiPopover = useCallback(() => emojiPopover.current?.updatePosition(), []);
  const [params] = useSearchParams();
  const linked = params.get('comment');
  const newestReplies = replies.toReversed();
  // A link to a reply has to land on it, however old it is.
  const [replyCount, setReplyCount] = useState(Math.max(REPLY_PAGE, newestReplies.findIndex((reply) => reply.id === linked) + 1));
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const isMine = comment.author?.id === me.id;

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['issues', issue.id, 'comments'] });
  const onError = (failure: unknown) => toast('error', toMessage(failure));

  const react = useMutation({
    mutationFn: (emoji: string) => unwrap(apiClient.POST('/api/v1/comment/{id}/toggle-reaction', { params: { path: { id: comment.id! } }, body: { emoji } })),
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

  const copyLink = async () => {
    setMenuAnchor(null);
    const url = new URL(window.location.href);
    url.search = `?comment=${comment.id}`;
    await navigator.clipboard.writeText(url.toString());
    toast('success', t('issues.linkCopied'));
  };

  return (
    <article id={`comment-${comment.id}`} className="flex flex-col gap-1">
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
            <ActionButton label={t('issues.addReaction')} onClick={(event) => setEmojiAnchor(event.currentTarget)}>
              <SmilePlus size={16} strokeWidth={1.5} aria-hidden />
            </ActionButton>
          )}
          <Popover above width={360} action={emojiPopover} open={Boolean(emojiAnchor)} anchorEl={emojiAnchor} onClose={() => setEmojiAnchor(null)}>
            <EmojiPicker
              used={new Set((comment.reactions ?? []).filter((reaction) => reaction.reactedByMe).map((reaction) => reaction.emoji!))}
              onPick={(emoji) => { react.mutate(emoji); setEmojiAnchor(null); }}
              onResize={placeEmojiPopover}
            />
          </Popover>
          {/* A shortcut to the 👍 reaction, which shows with the others. */}
          {canEdit && !comment.reactions?.some((reaction) => reaction.reactedByMe) && (
            <ActionButton label={t('issues.like')} onClick={() => react.mutate(LIKE)}>
              <ThumbsUp size={16} strokeWidth={1.5} aria-hidden />
            </ActionButton>
          )}
          {(comment.reactions ?? []).map((reaction) => (
            <Button
              key={reaction.emoji}
              kind="ghost"
              disabled={!canEdit}
              aria-label={t('issues.reactWith', { emoji: reaction.emoji })}
              aria-pressed={reaction.reactedByMe ?? false}
              sx={reactionChipSx(reaction.reactedByMe ?? false)}
              onClick={() => react.mutate(reaction.emoji!)}
            >
              {reaction.emoji} {reaction.count}
            </Button>
          ))}
          {isMine && (
            <ActionButton label={t('common.edit')} onClick={() => setEditing(true)}>
              <Pencil size={16} strokeWidth={1.5} aria-hidden />
            </ActionButton>
          )}
          {canEdit && !comment.parentId && (
            <Button kind="ghost" startIcon={<Reply size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setReplying(true)}>{t('issues.reply')}</Button>
          )}
          <ActionButton label={t('issues.commentMore')} onClick={(event) => setMenuAnchor(event.currentTarget)}>
            <Ellipsis size={16} strokeWidth={1.5} aria-hidden />
          </ActionButton>
          <Menu open={Boolean(menuAnchor)} anchorEl={menuAnchor} onClose={() => setMenuAnchor(null)}>
            <MenuItem icon={Link2} onClick={() => void copyLink()}>{t('issues.copyLink')}</MenuItem>
            {(isMine || canManage) && (
              <MenuItem icon={Trash2} danger onClick={() => { setMenuAnchor(null); remove.mutate(); }}>{t('common.delete')}</MenuItem>
            )}
          </Menu>
        </div>
        <div className="flex flex-col gap-3 pt-2">
          {newestReplies.slice(0, replyCount).map((reply) => (
            <CommentItem key={reply.id} comment={reply} issue={issue} replies={[]} people={people} canEdit={canEdit} canManage={canManage} />
          ))}
          {replies.length > replyCount && <LoadMore onClick={() => setReplyCount(replyCount + LOAD_MORE_STEP)} />}
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
      <DatePicker label={t('issues.workDate')} value={workDate} onChange={setWorkDate} />
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
  const [params] = useSearchParams();
  const linkedComment = params.get('comment');
  const [commentCount, setCommentCount] = useState(COMMENT_PAGE);

  useEffect(() => {
    if (linkedComment && comments.isSuccess) {
      document.getElementById(`comment-${linkedComment}`)?.scrollIntoView({ block: 'center' });
    }
  }, [linkedComment, comments.isSuccess]);

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
        ].sort((a, b) => b.at.localeCompare(a.at))
      : topLevel.toReversed().map((comment) => ({ at: '', node: renderComment(comment) }));

  // The list may be cut short, and a link must still land on its comment.
  const linked = (comments.data ?? []).find((comment) => comment.id === linkedComment);
  const linkedIndex = topLevel.toReversed().findIndex((comment) => comment.id === (linked?.parentId ?? linked?.id));
  const visible = tab === 'all' ? Math.max(commentCount, COMMENT_PAGE) : Math.max(commentCount, linkedIndex + 1);

  return (
    <div className="flex flex-col gap-4">
      {canEdit && <Composer issueId={issue.id!} people={people} placeholder={t('issues.addComment')} />}
      {comments.isSuccess && feed.length === 0 && <EmptyState title={t('issues.noComments')} />}
      {feed.slice(0, visible).map((entry) => entry.node)}
      {feed.length > visible && <LoadMore onClick={() => setCommentCount(visible + LOAD_MORE_STEP)} />}
    </div>
  );
}
