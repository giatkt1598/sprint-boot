import { Bookmark, Bug, ChevronsDown, ChevronsUp, CornerDownRight, Equal, SquareCheck, Zap, type LucideIcon } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { sem, type SemanticToken } from '../theme/tokens';

export type IssueType = 'story' | 'task' | 'bug' | 'subtask' | 'epic';
export type Priority = 'high' | 'medium' | 'low';

const issueTypes: Record<IssueType, [LucideIcon, SemanticToken]> = {
  story: [Bookmark, 'type-story'],
  task: [SquareCheck, 'type-task'],
  bug: [Bug, 'type-bug'],
  subtask: [CornerDownRight, 'type-subtask'],
  epic: [Zap, 'type-epic'],
};

const priorities: Record<Priority, [LucideIcon, SemanticToken]> = {
  high: [ChevronsUp, 'feedback-danger-text'],
  medium: [Equal, 'text-secondary'],
  low: [ChevronsDown, 'text-secondary'],
};

export function IssueTypeIcon({ type }: { type: IssueType }) {
  const { t } = useTranslation();
  const [Icon, color] = issueTypes[type];
  return <Icon size={16} strokeWidth={1.5} color={sem(color)} role="img" aria-label={t(`issue.type.${type}`)} />;
}

export function PriorityIcon({ priority }: { priority: Priority }) {
  const { t } = useTranslation();
  const [Icon, color] = priorities[priority];
  return <Icon size={16} strokeWidth={1.5} color={sem(color)} role="img" aria-label={t(`issue.priority.${priority}`)} />;
}
