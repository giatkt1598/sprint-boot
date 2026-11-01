import ButtonBase from '@mui/material/ButtonBase';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { components } from '@sprint-boot/api-client';
import { ApiError, apiClient, unwrap } from '../../api';
import { StatusBadge } from '../../components/Badge';
import { DatePicker } from '../../components/DatePicker';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { MenuItem } from '../../components/Menu';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { UserPicker, type PickerUser } from '../../components/UserPicker';
import { useErrorMessage } from '../../lib/errors';
import { font, radius, sem } from '../../theme/tokens';
import { statusCategory, type Issue } from './api';

export type Available = components['schemas']['AvailableTransitionDto'];

const DATE_FIELDS = ['startDate', 'dueDate'];

export type TransitionIssue = Pick<Issue, 'id' | 'key' | 'status' | 'assignee' | 'concurrencyStamp'>;

interface ControlProps {
  issue: TransitionIssue;
  people: PickerUser[];
  onConflict: () => void;
  /** For rows in a list: loads the transitions on open, and names the issue for screen readers. */
  lazy?: boolean;
}

function useExecute(issue: TransitionIssue, onConflict: () => void, onDone: () => void) {
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: { transitionId: string; comment?: string; fields?: Record<string, unknown> }) =>
      unwrap(
        apiClient.POST('/api/v1/transition/execute/{issueId}', {
          params: { path: { issueId: issue.id! } },
          body: { ...body, concurrencyStamp: issue.concurrencyStamp!, fields: body.fields as never },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      onDone();
    },
    onError: (failure) => {
      if (failure instanceof ApiError && failure.status === 409) {
        onConflict();
        onDone();
      } else {
        toast('error', toMessage(failure));
      }
    },
  });
}

export function TransitionDialog({ issue, transition, people, onConflict, onClose }: ControlProps & { transition: Available; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const execute = useExecute(issue, onConflict, onClose);
  const [comment, setComment] = useState('');
  const [assignee, setAssignee] = useState<PickerUser | null>(issue.assignee ?? null);
  const [values, setValues] = useState<Record<string, string>>({});

  const missing = transition.missingFields ?? [];
  const problems = transition.problems ?? [];
  const fieldsFilled = missing.every((field) => (field === 'assignee' ? Boolean(assignee) : Boolean(values[field])));
  const commentMissing = transition.requiresComment && !comment.trim();

  const fieldValue = (field: string) => {
    if (field === 'assignee') {
      return assignee?.id;
    }
    if (field === 'storyPoints') {
      return Number(values[field]);
    }
    return field === 'labels' ? values[field]!.split(',').map((item) => item.trim()) : values[field];
  };

  const fieldLabel = (field: string) => (i18n.exists(`issues.field.${field}`) ? t(`issues.field.${field}`) : field);

  return (
    <Dialog
      open
      title={transition.toStatus?.name ?? ''}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={transition.toStatus?.name ?? ''}
          confirmDisabled={problems.length > 0 || !fieldsFilled || Boolean(commentMissing) || execute.isPending}
          onCancel={onClose}
          onConfirm={() =>
            execute.mutate({
              transitionId: transition.id!,
              comment: comment || undefined,
              fields: Object.fromEntries(missing.map((field) => [field, fieldValue(field)])),
            })
          }
        />
      }
    >
      <Typography color="textSecondary">
        {t('issues.moveSummary', { key: issue.key, from: issue.status?.name, to: transition.toStatus?.name })}
      </Typography>
      {problems.map((problem) => (
        <div
          key={problem}
          role="alert"
          className="flex items-start gap-2 p-3"
          style={{ backgroundColor: sem('feedback-danger-bg'), color: sem('feedback-danger-text'), borderRadius: radius.control, fontSize: font.size.xs }}
        >
          <TriangleAlert size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />
          {i18n.exists(`errors.${problem.split(':')[1]}`) ? t(`errors.${problem.split(':')[1]}`) : t('errors.TransitionValidationFailed')}
        </div>
      ))}
      {missing.map((field) =>
        field === 'assignee' ? (
          <UserPicker key={field} label={fieldLabel(field)} options={people} value={assignee} onChange={setAssignee} />
        ) : DATE_FIELDS.includes(field) ? (
          <DatePicker key={field} label={fieldLabel(field)} value={values[field] ?? ''} onChange={(value) => setValues({ ...values, [field]: value })} />
        ) : (
          <TextField
            key={field}
            label={fieldLabel(field)}
            type={field === 'storyPoints' ? 'number' : 'text'}
            value={values[field] ?? ''}
            onChange={(event) => setValues({ ...values, [field]: event.target.value })}
          />
        ),
      )}
      {transition.requiresComment && (
        <TextField label={t('issues.commentRequired')} value={comment} onChange={(event) => setComment(event.target.value)} />
      )}
    </Dialog>
  );
}

export function TransitionControl({ issue, people, onConflict, lazy }: ControlProps) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [dialog, setDialog] = useState<Available | null>(null);
  const [moving, setMoving] = useState<Available['toStatus'] | null>(null);
  const execute = useExecute(issue, onConflict, () => undefined);

  const available = useQuery({
    queryKey: ['issues', issue.id, 'transitions', issue.status?.id, issue.concurrencyStamp],
    enabled: !lazy || Boolean(anchor),
    // The key changes with every move; the control must not vanish while the next list loads.
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(apiClient.GET('/api/v1/transition/available/{issueId}', { params: { path: { issueId: issue.id! } } })),
  });

  // Shown at once, so the status does not lag behind the click while the issue reloads.
  const shown = execute.isPending && moving ? moving : issue.status;
  const badge = <StatusBadge status={statusCategory(shown)} label={shown?.name} />;
  // People pick a status, so two transitions into one show as one, preferring the one that can run.
  const targets = [...new Map([...(available.data ?? [])].reverse().sort((first, second) => (second.problems?.length ?? 0) - (first.problems?.length ?? 0)).map((item) => [item.toStatus?.id, item])).values()];
  if (!lazy && !available.data?.length) {
    return badge;
  }

  const pick = (transition: Available) => {
    setAnchor(null);
    const needsDialog = transition.requiresComment || transition.missingFields?.length || transition.problems?.length;
    if (needsDialog) {
      setDialog(transition);
    } else {
      setMoving(transition.toStatus);
      execute.mutate({ transitionId: transition.id! });
    }
  };

  return (
    <>
      <ButtonBase
        aria-label={`${t('issues.status')}: ${issue.status?.name}${lazy ? ` ${issue.key}` : ''}`}
        aria-haspopup="menu"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ borderRadius: `${radius.control}px` }}
      >
        {badge}
      </ButtonBase>
      <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <Typography variant="body2" color="textSecondary" component="li" role="presentation" className="px-2 pb-1">
          {t('issues.moveTo')}
        </Typography>
        {targets.length === 0 && !available.isPending && (
          <Typography variant="body2" color="textSecondary" component="li" role="presentation" className="px-2">{t('issues.noTransitions')}</Typography>
        )}
        {!available.isPlaceholderData && targets.map((transition) => (
          <MenuItem key={transition.id} onClick={() => pick(transition)} sx={{ minWidth: 160 }}>
            <StatusBadge status={statusCategory(transition.toStatus)} label={transition.toStatus?.name} />
          </MenuItem>
        ))}
      </Menu>
      {dialog && (
        <TransitionDialog issue={issue} transition={dialog} people={people} onConflict={onConflict} onClose={() => setDialog(null)} />
      )}
    </>
  );
}
