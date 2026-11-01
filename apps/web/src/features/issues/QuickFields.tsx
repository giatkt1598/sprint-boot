import ButtonBase from '@mui/material/ButtonBase';
import Menu from '@mui/material/Menu';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError, apiClient, unwrap } from '../../api';
import { Avatar, UnassignedAvatar } from '../../components/Avatar';
import { StatusBadge } from '../../components/Badge';
import { PriorityIcon } from '../../components/IssueIcons';
import { MenuItem } from '../../components/Menu';
import { useToast } from '../../components/ToastHost';
import type { PickerUser } from '../../components/UserPicker';
import { TextField } from '../../components/TextField';
import { useErrorMessage } from '../../lib/errors';
import { hasSelectedText } from '../../lib/selection';
import { radius } from '../../theme/tokens';
import { TransitionControl, type TransitionIssue } from './TransitionControl';
import { priorityLevel, statusCategory, usePriorities, type Issue } from './api';

type QuickIssue = TransitionIssue & Partial<Pick<Issue, 'priority' | 'title'>>;

interface QuickProps {
  issue: QuickIssue;
  canEdit: boolean;
}

/** Rows sit inside drag sources, and portaled menus still bubble to them in React. */
const Isolated = ({ children }: { children: ReactNode }) => (
  <span className="inline-flex" onPointerDown={(event) => event.stopPropagation()}>{children}</span>
);

function useConflict() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();

  return () => {
    toast('error', t('errors.conflict'));
    void queryClient.invalidateQueries({ queryKey: ['issues'] });
  };
}

function useQuickUpdate(issue: QuickIssue) {
  const toMessage = useErrorMessage();
  const toast = useToast();
  const queryClient = useQueryClient();
  const onConflict = useConflict();

  return useMutation({
    mutationFn: (fields: Record<string, unknown>) =>
      unwrap(
        apiClient.PUT('/api/v1/issue/{id}', {
          params: { path: { id: issue.id! } },
          body: { concurrencyStamp: issue.concurrencyStamp!, fields: fields as never },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['issues'] }),
    onError: (failure) => {
      if (failure instanceof ApiError && failure.status === 409) {
        onConflict();
      } else {
        toast('error', toMessage(failure));
      }
    },
  });
}

/** Owns the text while typing, so keystrokes do not re-render the whole issue. */
export function TitleEditor({ label, initial, onSave, onCancel }: { label: string; initial: string; onSave: (title: string) => void; onCancel: () => void }) {
  const [title, setTitle] = useState(initial);
  const closed = useRef(false);
  const close = (action: () => void) => {
    if (!closed.current) {
      closed.current = true;
      action();
    }
  };

  return (
    <TextField
      quiet
      label={label}
      autoFocus
      value={title}
      onChange={(event) => setTitle(event.target.value)}
      onBlur={() => close(() => onSave(title))}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          close(() => onSave(title));
        }
        if (event.key === 'Escape') {
          close(onCancel);
        }
      }}
    />
  );
}

/** Click edits; a drag stays a text selection. */
export function QuickTitle({ issue, canEdit }: QuickProps) {
  const { t } = useTranslation();
  const update = useQuickUpdate(issue);
  const [editing, setEditing] = useState(false);
  const pending = update.isPending ? (update.variables.title as string) : issue.title;

  if (editing) {
    return (
      <div className="min-w-0 flex-1">
        <TitleEditor
          label={t('issues.titleField')}
          initial={issue.title ?? ''}
          onCancel={() => setEditing(false)}
          onSave={(title) => {
            setEditing(false);
            if (title.trim() && title !== issue.title) {
              update.mutate({ title });
            }
          }}
        />
      </div>
    );
  }

  return (
    <span
      className="min-w-0 flex-1 truncate"
      role={canEdit ? 'button' : undefined}
      tabIndex={canEdit ? 0 : undefined}
      onClick={() => canEdit && !hasSelectedText() && setEditing(true)}
      onKeyDown={(event) => event.key === 'Enter' && canEdit && setEditing(true)}
    >
      {pending}
    </span>
  );
}

export function QuickStatus({ issue, canEdit, people }: QuickProps & { people: PickerUser[] }) {
  const onConflict = useConflict();

  return canEdit ? (
    <Isolated><TransitionControl lazy issue={issue} people={people} onConflict={onConflict} /></Isolated>
  ) : (
    <StatusBadge status={statusCategory(issue.status)} label={issue.status?.name} />
  );
}

export function QuickPriority({ issue, canEdit }: QuickProps) {
  const { t } = useTranslation();
  const priorities = usePriorities();
  const update = useQuickUpdate(issue);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const icon = <PriorityIcon priority={priorityLevel(issue.priority)} />;

  if (!canEdit) {
    return icon;
  }

  return (
    <Isolated>
      <ButtonBase
        aria-label={`${t('issues.priority')}: ${issue.priority?.name} ${issue.key}`}
        aria-haspopup="menu"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ width: 24, height: 24, borderRadius: `${radius.control}px` }}
      >
        <span aria-hidden className="flex">{icon}</span>
      </ButtonBase>
      <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        {(priorities.data ?? []).map((priority) => (
          <MenuItem
            key={priority.id}
            onClick={() => {
              setAnchor(null);
              update.mutate({ priority: priority.id });
            }}
          >
            <span className="flex items-center gap-2">
              <span aria-hidden className="flex"><PriorityIcon priority={priorityLevel(priority)} /></span>
              {priority.name}
            </span>
          </MenuItem>
        ))}
      </Menu>
    </Isolated>
  );
}

export function QuickAssignee({ issue, canEdit, people, showName }: QuickProps & { people: PickerUser[]; showName?: boolean }) {
  const { t } = useTranslation();
  const update = useQuickUpdate(issue);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const name = issue.assignee?.name ?? t('common.unassigned');
  const face = issue.assignee?.name ? <Avatar name={issue.assignee.name} decorative /> : <UnassignedAvatar />;
  const shown = (
    <span className="flex items-center gap-2">
      {face}
      {showName && name}
    </span>
  );

  if (!canEdit) {
    return shown;
  }

  const pick = (id: string | null) => {
    setAnchor(null);
    update.mutate({ assignee: id });
  };

  return (
    <Isolated>
      <ButtonBase
        aria-label={`${t('issues.assignee')}: ${name} ${issue.key}`}
        aria-haspopup="menu"
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{ font: 'inherit', borderRadius: `${radius.control}px` }}
      >
        <span aria-hidden className="flex">{shown}</span>
      </ButtonBase>
      <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <MenuItem onClick={() => pick(null)}>
          <span className="flex items-center gap-2"><span aria-hidden className="flex"><UnassignedAvatar /></span>{t('common.unassigned')}</span>
        </MenuItem>
        {people.map((person) => (
          <MenuItem key={person.id} onClick={() => pick(person.id!)}>
            <span className="flex items-center gap-2"><span aria-hidden className="flex"><Avatar name={person.name ?? ''} /></span>{person.name}</span>
          </MenuItem>
        ))}
      </Menu>
    </Isolated>
  );
}
