import { BugIcon, ChevronDownIcon, ChevronUpIcon, EpicIcon, PriorityHighestIcon, PriorityLowestIcon, PriorityMediumIcon, StoryIcon, SubtaskIcon, TaskIcon, type IconComponent } from './icons';
import { useTranslation } from 'react-i18next';
import { sem, type SemanticToken } from '../theme/tokens';

export type IssueType = 'story' | 'task' | 'bug' | 'subtask' | 'epic';
export type Priority = 'highest' | 'high' | 'medium' | 'low' | 'lowest';

const issueTypes: Record<IssueType, [IconComponent, SemanticToken]> = {
  story: [StoryIcon, 'type-story'],
  task: [TaskIcon, 'type-task'],
  bug: [BugIcon, 'type-bug'],
  subtask: [SubtaskIcon, 'type-subtask'],
  epic: [EpicIcon, 'type-epic'],
};

const priorities: Record<Priority, [IconComponent, SemanticToken]> = {
  highest: [PriorityHighestIcon, 'feedback-danger-text'],
  high: [ChevronUpIcon, 'feedback-danger-text'],
  medium: [PriorityMediumIcon, 'text-secondary'],
  low: [ChevronDownIcon, 'text-secondary'],
  lowest: [PriorityLowestIcon, 'text-secondary'],
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
