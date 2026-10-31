import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { font, radius, sem, type SemanticToken } from '../theme/tokens';

export type BadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

const tones: Record<BadgeTone, [SemanticToken, SemanticToken]> = {
  neutral: ['status-todo-bg', 'status-todo-text'],
  info: ['status-progress-bg', 'status-progress-text'],
  success: ['feedback-success-bg', 'feedback-success-text'],
  warning: ['feedback-warning-bg', 'feedback-warning-text'],
  danger: ['feedback-danger-bg', 'feedback-danger-text'],
};

export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  const [background, color] = tones[tone];

  return (
    <span
      className="inline-flex h-6 items-center px-2"
      style={{
        backgroundColor: sem(background),
        color: sem(color),
        borderRadius: radius.control,
        fontSize: font.size.xs,
        fontWeight: font.weight.medium,
      }}
    >
      {children}
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

export function StatusBadge({ status }: { status: StatusCategory }) {
  const { t } = useTranslation();
  return <Badge tone={statusTones[status]}>{t(`issue.status.${status}`)}</Badge>;
}
