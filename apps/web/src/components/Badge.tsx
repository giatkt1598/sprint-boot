import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { font, radius, sem, type SemanticToken } from '../theme/tokens';

export type BadgeTone = 'subtle' | 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'epic';

const tones: Record<BadgeTone, [SemanticToken, SemanticToken]> = {
  subtle: ['bg-subtle', 'text-secondary'],
  neutral: ['status-todo-bg', 'status-todo-text'],
  info: ['status-progress-bg', 'status-progress-text'],
  success: ['feedback-success-bg', 'feedback-success-text'],
  warning: ['feedback-warning-bg', 'feedback-warning-text'],
  danger: ['feedback-danger-bg', 'feedback-danger-text'],
  epic: ['status-merged-bg', 'status-merged-text'],
};

export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  const [background, color] = tones[tone];

  return (
    <span
      className="inline-flex h-6 max-w-full items-center whitespace-nowrap px-2"
      style={{
        backgroundColor: sem(background),
        color: sem(color),
        borderRadius: radius.control,
        fontSize: font.size.xs,
        fontWeight: font.weight.medium,
      }}
    >
      <span className="truncate">{children}</span>
    </span>
  );
}

export type StatusCategory = 'todo' | 'inProgress' | 'inReview' | 'done';

const statusTones: Record<StatusCategory, BadgeTone> = {
  todo: 'neutral',
  inProgress: 'info',
  inReview: 'info',
  done: 'success',
};

/** Pass label for a workflow status; its name is admin data and takes priority over the category. */
export function StatusBadge({ status, label }: { status: StatusCategory; label?: string | null }) {
  const { t } = useTranslation();
  return <Badge tone={statusTones[status]}>{label ?? t(`issue.status.${status}`)}</Badge>;
}
