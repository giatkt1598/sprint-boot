import Slider from '@mui/material/Slider';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Avatar } from '../../components/Avatar';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Button } from '../../components/Button';
import { DatePicker } from '../../components/DatePicker';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate, formatDateTime, todayIso } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { RichTextEditor, RichTextView } from '../issues/RichTextEditor';
import { SpacePermission, useMembers, type Space } from '../spaces/api';
import { goalsKey, useGoalUpdates, useGoals, type Goal } from './api';

/** Matches the API's GoalStatus. */
const STATUSES = ['pending', 'onTrack', 'atRisk', 'offTrack', 'paused', 'done'] as const;
const STATUS_TONES: BadgeTone[] = ['neutral', 'success', 'warning', 'danger', 'subtle', 'info'];
const NO_OWNER = 'none';

const statusLabel = (t: (key: string) => string, status: number | undefined) => t(`goals.status.${STATUSES[status ?? 0]}`);

/** What the space works toward, each goal with an owner, a target date and a status they report on. */
export function GoalsTab({ space }: { space: Space }) {
  const { t, i18n } = useTranslation();
  const [archived, setArchived] = useState(false);
  const [editing, setEditing] = useState<Goal | 'new' | null>(null);
  const [viewing, setViewing] = useState<string | null>(null);
  const goals = useGoals(space.id, archived);
  const manage = Boolean(space.permissions?.includes(SpacePermission.manage));
  const open = goals.data?.find((goal) => goal.id === viewing);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Typography variant="h2" className="flex-1">{t(archived ? 'goals.archived' : 'goals.title')}</Typography>
        <Button kind="secondary" onClick={() => setArchived(!archived)}>{t(archived ? 'goals.showActive' : 'goals.showArchived')}</Button>
        {manage && !archived && <Button onClick={() => setEditing('new')}>{t('goals.new')}</Button>}
      </div>

      {goals.isPending && <LoadingState />}
      {goals.isError && <ErrorState onRetry={() => void goals.refetch()} />}
      {goals.data?.length === 0 && (
        <EmptyState
          title={t(archived ? 'goals.noneArchived' : 'goals.none')}
          action={manage && !archived ? <Button kind="secondary" onClick={() => setEditing('new')}>{t('goals.new')}</Button> : undefined}
        />
      )}
      {(goals.data?.length ?? 0) > 0 && (
        <Table<Goal>
          label={t('goals.title')}
          rows={goals.data ?? []}
          rowKey={(goal) => goal.id!}
          columns={[
            {
              key: 'name',
              header: t('goals.name'),
              render: (goal) => (
                <Button kind="link" sx={{ fontSize: font.size.md, color: sem('text-primary'), padding: 0, minHeight: 0 }} onClick={() => setViewing(goal.id!)}>
                  {goal.name}
                </Button>
              ),
            },
            {
              key: 'owner',
              header: t('goals.owner'),
              render: (goal) =>
                goal.owner ? (
                  <span className="flex items-center gap-2"><Avatar name={goal.owner.name ?? ''} src={goal.owner.avatarUrl} />{goal.owner.name}</span>
                ) : (
                  <span style={{ color: sem('text-tertiary') }}>{t('goals.noOwner')}</span>
                ),
            },
            { key: 'status', header: t('goals.statusLabel'), render: (goal) => <Badge tone={STATUS_TONES[goal.status ?? 0]}>{statusLabel(t, goal.status)}</Badge> },
            { key: 'progress', header: t('goals.progress'), render: (goal) => <ProgressBar value={goal.progress ?? 0} /> },
            {
              key: 'target',
              header: t('goals.target'),
              render: (goal) =>
                goal.targetDate ? (
                  <span style={{ color: isOverdue(goal) ? sem('feedback-danger-text') : undefined }}>
                    {formatDate(goal.targetDate, i18n.language)}
                  </span>
                ) : (
                  <span style={{ color: sem('text-tertiary') }}>—</span>
                ),
            },
          ]}
        />
      )}

      {editing && <GoalDialog space={space} goal={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {open && (
        <GoalDetail
          goal={open}
          manage={manage}
          onClose={() => setViewing(null)}
          onEdit={() => { setEditing(open); setViewing(null); }}
        />
      )}
    </div>
  );
}

/** Past its target and not finished. */
const isOverdue = (goal: Goal) => Boolean(goal.targetDate && goal.status !== 5 && !goal.archivedAt && goal.targetDate < todayIso());

function ProgressBar({ value }: { value: number }) {
  return (
    <span className="flex items-center gap-2">
      <span className="h-2 w-24 overflow-hidden" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.full }}>
        <span className="block h-full" style={{ width: `${value}%`, backgroundColor: sem('accent-default'), borderRadius: radius.full }} />
      </span>
      <span style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{value}%</span>
    </span>
  );
}

/** Creates a goal, or edits the name, description, owner and dates of one; status and progress change by reporting on it. */
function GoalDialog({ space, goal, onClose }: { space: Space; goal?: Goal; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const members = useMembers(space.id);
  const [name, setName] = useState(goal?.name ?? '');
  const [description, setDescription] = useState(goal?.description ?? '');
  const [ownerId, setOwnerId] = useState(goal?.owner?.id ?? NO_OWNER);
  const [startDate, setStartDate] = useState(goal?.startDate ?? '');
  const [targetDate, setTargetDate] = useState(goal?.targetDate ?? '');
  const datesInvalid = Boolean(startDate && targetDate && targetDate < startDate);

  const save = useMutation({
    mutationFn: () => {
      const body = { name, description, ownerId: ownerId === NO_OWNER ? null : ownerId, startDate: startDate || null, targetDate: targetDate || null };
      return goal
        ? unwrap(apiClient.PUT('/api/v1/goal/{id}', { params: { path: { id: goal.id! } }, body }))
        : unwrap(apiClient.POST('/api/v1/goal', { params: { query: { projectId: space.id! } }, body }));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: goalsKey(space.id) });
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <Dialog
      open
      width={640}
      title={t(goal ? 'goals.edit' : 'goals.new')}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('common.save')} confirmDisabled={!name.trim() || datesInvalid || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />}
    >
      <TextField label={t('goals.name')} autoFocus required value={name} inputProps={{ maxLength: 128 }} onChange={(event) => setName(event.target.value)} />
      <RichTextEditor label={t('goals.description')} showLabel initialValue={description} people={[]} onChange={setDescription} />
      <SelectField
        label={t('goals.owner')}
        value={ownerId}
        onChange={(event) => setOwnerId(event.target.value)}
        options={[{ value: NO_OWNER, label: t('goals.noOwner') }, ...(members.data ?? []).map((member) => ({ value: member.user!.id!, label: member.user!.name ?? '' }))]}
      />
      <div className="grid grid-cols-2 gap-4">
        <DatePicker label={t('goals.start')} value={startDate} onChange={setStartDate} />
        <DatePicker label={t('goals.target')} value={targetDate} onChange={setTargetDate} />
      </div>
      {datesInvalid && <Typography role="alert" color="error" variant="body2">{t('errors.GoalDatesInvalid')}</Typography>}
    </Dialog>
  );
}

/** One goal in full: what it is, how it stands, the reports on it, and, for admins, what can be done with it. */
function GoalDetail({ goal, manage, onClose, onEdit }: { goal: Goal; manage: boolean; onClose: () => void; onEdit: () => void }) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const updates = useGoalUpdates(goal.id);
  const [reporting, setReporting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [status, setStatus] = useState(String(goal.status ?? 0));
  const [progress, setProgress] = useState(goal.progress ?? 0);
  const [note, setNote] = useState('');

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['goals'] });
  const fail = (failure: unknown) => toast('error', toMessage(failure));

  const report = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/goal/{id}/update', { params: { path: { id: goal.id! } }, body: { status: Number(status) as never, progress, note: note.trim() || null } })),
    onSuccess: async () => {
      await refresh();
      setReporting(false);
      setNote('');
    },
    onError: fail,
  });
  const archive = useMutation({
    mutationFn: () => unwrap(apiClient.POST(goal.archivedAt ? '/api/v1/goal/{id}/unarchive' : '/api/v1/goal/{id}/archive', { params: { path: { id: goal.id! } } })),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: fail,
  });
  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/goal/{id}', { params: { path: { id: goal.id! } } })),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: fail,
    onSettled: () => setDeleting(false),
  });

  // Archived goals take no changes, but an admin can still bring one back or remove it.
  const canChange = Boolean(goal.canEdit);

  return (
    <>
      <Dialog open width={640} title={goal.name ?? ''} onClose={onClose}>
        <div className="flex flex-wrap items-center gap-3">
          <Badge tone={STATUS_TONES[goal.status ?? 0]}>{statusLabel(t, goal.status)}</Badge>
          <ProgressBar value={goal.progress ?? 0} />
          {goal.owner && <span className="flex items-center gap-2"><Avatar name={goal.owner.name ?? ''} src={goal.owner.avatarUrl} />{goal.owner.name}</span>}
          {(goal.startDate || goal.targetDate) && (
            <Typography variant="body2" color="textSecondary">
              {[goal.startDate && formatDate(goal.startDate, i18n.language), goal.targetDate && formatDate(goal.targetDate, i18n.language)].filter(Boolean).join(' – ')}
            </Typography>
          )}
          {goal.archivedAt && <Badge tone="subtle">{t('goals.archivedBadge')}</Badge>}
        </div>
        {goal.description && <RichTextView html={goal.description} />}

        <div className="flex items-center gap-2 pt-2">
          <Typography variant="h3" component="h3" className="flex-1">{t('goals.history')}</Typography>
          {canChange && !reporting && <Button kind="secondary" onClick={() => setReporting(true)}>{t('goals.addUpdate')}</Button>}
        </div>
        {reporting && (
          <div className="flex flex-col gap-3 p-3" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
            <SelectField label={t('goals.statusLabel')} value={status} onChange={(event) => setStatus(event.target.value)} options={STATUSES.map((value, index) => ({ value: String(index), label: t(`goals.status.${value}`) }))} />
            <div className="flex flex-col gap-1">
              <span id="goal-progress" style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>{t('goals.progress')}: {status === '5' ? 100 : progress}%</span>
              <Slider aria-labelledby="goal-progress" min={0} max={100} step={5} disabled={status === '5'} value={status === '5' ? 100 : progress} onChange={(_, value) => setProgress(value)} />
            </div>
            <TextField label={t('goals.note')} multiline minRows={2} value={note} inputProps={{ maxLength: 1000 }} onChange={(event) => setNote(event.target.value)} />
            <div className="flex justify-end gap-2">
              <Button kind="ghost" onClick={() => setReporting(false)}>{t('common.cancel')}</Button>
              <Button disabled={report.isPending} onClick={() => report.mutate()}>{t('goals.saveUpdate')}</Button>
            </div>
          </div>
        )}
        {updates.isPending && <LoadingState />}
        {updates.data?.length === 0 && <Typography color="textSecondary" variant="body2">{t('goals.noUpdates')}</Typography>}
        <ul className="m-0 flex list-none flex-col gap-3 p-0">
          {updates.data?.map((update) => (
            <li key={update.id} className="flex gap-2">
              <Avatar name={update.author?.name ?? ''} src={update.author?.avatarUrl} />
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <span className="flex flex-wrap items-center gap-2" style={{ fontSize: font.size.md }}>
                  <span style={{ fontWeight: font.weight.medium }}>{update.author?.name ?? t('common.deactivatedUser')}</span>
                  <Badge tone={STATUS_TONES[update.status ?? 0]}>{statusLabel(t, update.status)}</Badge>
                  <span style={{ color: sem('text-secondary') }}>{update.progress}%</span>
                  <span style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{formatDateTime(update.createdAt, i18n.language)}</span>
                </span>
                {update.note && <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{update.note}</Typography>}
              </div>
            </li>
          ))}
        </ul>

        <div className="flex flex-wrap justify-end gap-2 pb-4 pt-2">
          {canChange && <Button kind="secondary" onClick={onEdit}>{t('goals.edit')}</Button>}
          {manage && <ArchiveButton goal={goal} busy={archive.isPending} onClick={() => archive.mutate()} />}
          {manage && <Button kind="ghost" onClick={() => setDeleting(true)} sx={{ color: sem('feedback-danger-text') }}>{t('common.delete')}</Button>}
          <Button kind="ghost" onClick={onClose}>{t('common.close')}</Button>
        </div>
      </Dialog>
      <DialogConfirm
        open={deleting}
        title={t('goals.deleteTitle')}
        description={t('goals.deleteBody', { name: goal.name })}
        confirmLabel={t('common.delete')}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </>
  );
}

function ArchiveButton({ goal, busy, onClick }: { goal: Goal; busy: boolean; onClick: () => void }) {
  const { t } = useTranslation();
  return <Button kind="secondary" disabled={busy} onClick={onClick}>{t(goal.archivedAt ? 'goals.unarchive' : 'goals.archive')}</Button>;
}
