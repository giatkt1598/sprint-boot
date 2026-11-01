import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { DatePicker } from '../../components/DatePicker';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { SelectField, TextField } from '../../components/TextField';
import { useErrorMessage } from '../../lib/errors';
import { IssueRow } from './BacklogPage';
import type { SprintSummary } from './api';

const BACKLOG_TARGET = 'backlog';

const addDays = (iso: string, days: number) => {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
};

interface SprintFormProps {
  mode: 'create' | 'edit' | 'start';
  projectId: string;
  sprint?: SprintSummary;
  onClose: () => void;
}

export function SprintDialog({ mode, projectId, sprint, onClose }: SprintFormProps) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [name, setName] = useState(sprint?.name ?? '');
  const [goal, setGoal] = useState(sprint?.goal ?? '');
  const [startDate, setStartDate] = useState(sprint?.startDate ?? '');
  const [endDate, setEndDate] = useState(sprint?.endDate ?? '');
  // Starting a sprint planned without dates needs them just as much as creating one.
  const needsDates = mode === 'create' || (mode === 'start' && !sprint?.startDate && !sprint?.endDate);
  const defaults = useQuery({
    queryKey: ['sprints', projectId, 'new-defaults'],
    enabled: needsDates,
    // Always asked afresh: the previous answer predates the sprint just created.
    gcTime: 0,
    staleTime: 0,
    refetchOnMount: 'always',
    queryFn: () => unwrap(apiClient.GET('/api/v1/sprint/new-defaults/{projectId}', { params: { path: { projectId } } })),
  });

  useEffect(() => {
    if (defaults.data) {
      if (mode === 'create') {
        setName(defaults.data.name ?? '');
      }
      setStartDate(defaults.data.startDate ?? '');
      setEndDate(defaults.data.endDate ?? '');
    }
  }, [defaults.data, mode]);

  const pickStart = (next: string) => {
    setStartDate(next);
    // Picking a start on a new sprint keeps the space's sprint length.
    if (next && defaults.data?.lengthDays) {
      setEndDate(addDays(next, defaults.data.lengthDays - 1));
    }
  };
  const datesInvalid = Boolean(startDate && endDate && endDate < startDate);
  const incomplete = !name.trim() || (mode === 'start' && (!startDate || !endDate));

  const save = useMutation({
    mutationFn: () => {
      const body = { name, goal: goal || null, startDate: startDate || null, endDate: endDate || null };
      if (mode === 'create') {
        return unwrap<unknown>(apiClient.POST('/api/v1/sprint', { body: { ...body, projectId } }));
      }
      const params = { path: { id: sprint!.id! } };
      return unwrap<unknown>(
        mode === 'start'
          ? apiClient.POST('/api/v1/sprint/{id}/start', { params, body })
          : apiClient.PUT('/api/v1/sprint/{id}', { params, body }),
      );
    },
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: ['sprints', projectId, 'new-defaults'] });
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      onClose();
    },
  });

  const title = { create: 'agile.createSprint', edit: 'agile.editSprint', start: 'agile.startSprint' }[mode];

  return (
    <Dialog
      open
      title={t(title)}
      onClose={onClose}
      footer={
        <DialogFooter confirmLabel={t(mode === 'start' ? 'agile.start' : 'common.save')} confirmDisabled={incomplete || datesInvalid || save.isPending} onCancel={onClose} onConfirm={() => save.mutate()} />
      }
    >
      <TextField label={t('account.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      <div className="grid grid-cols-2 gap-4">
        <DatePicker label={t('issues.startDate')} value={startDate} onChange={pickStart} />
        <DatePicker label={t('agile.endDate')} value={endDate} onChange={setEndDate} />
      </div>
      <TextField label={t('agile.goal')} value={goal} onChange={(event) => setGoal(event.target.value)} />
      {datesInvalid && <Typography role="alert" color="error" variant="body2">{t('errors.SprintEndBeforeStart')}</Typography>}
      {save.isError && <Typography role="alert" color="error" variant="body2">{toMessage(save.error)}</Typography>}
    </Dialog>
  );
}

export function CompleteSprintDialog({ sprint, futureSprints, onClose }: { sprint: SprintSummary; futureSprints: SprintSummary[]; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [target, setTarget] = useState(BACKLOG_TARGET);

  const preview = useQuery({
    queryKey: ['sprints', sprint.id, 'preview'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/sprint/{id}/complete-preview', { params: { path: { id: sprint.id! } } })),
  });

  const complete = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/v1/sprint/{id}/complete', {
          params: { path: { id: sprint.id! } },
          body: target === BACKLOG_TARGET ? { toBacklog: true } : { targetSprintId: target, toBacklog: false },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('agile.completeTitle', { name: sprint.name })}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('agile.completeSprint')} confirmDisabled={!preview.data || complete.isPending} onCancel={onClose} onConfirm={() => complete.mutate()} />}
    >
      <Typography color="textSecondary">{t('agile.completeSummary', { done: preview.data?.doneCount ?? 0, open: preview.data?.openCount ?? 0 })}</Typography>
      {(preview.data?.openCount ?? 0) > 0 && (
        <SelectField
          label={t('agile.moveOpenTo')}
          value={target}
          onChange={(event) => setTarget(event.target.value)}
          options={[
            { value: BACKLOG_TARGET, label: t('agile.backlog') },
            ...futureSprints.map((item) => ({ value: item.id!, label: item.name! })),
          ]}
        />
      )}
      {complete.isError && <Typography role="alert" color="error" variant="body2">{toMessage(complete.error)}</Typography>}
    </Dialog>
  );
}

export function ClosedSprintDialog({ sprintId, onClose, onOpenIssue }: { sprintId: string; onClose: () => void; onOpenIssue: (key: string) => void }) {
  const { t } = useTranslation();
  const closed = useQuery({
    queryKey: ['sprints', sprintId, 'closed'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/sprint/{id}/closed', { params: { path: { id: sprintId } } })),
  });

  return (
    <Dialog open width={640} title={closed.data?.sprint?.name ?? ''} onClose={onClose}>
      <Typography variant="h3" component="h3">{t('agile.completed')}</Typography>
      {(closed.data?.completed ?? []).map((card) => <IssueRow key={card.id} card={card} onOpen={onOpenIssue} />)}
      <Typography variant="h3" component="h3">{t('agile.notCompleted')}</Typography>
      {(closed.data?.notCompleted ?? []).map((card) => <IssueRow key={card.id} card={card} onOpen={onOpenIssue} />)}
    </Dialog>
  );
}
