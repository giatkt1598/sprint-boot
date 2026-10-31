import { CircleAlert, CircleCheck, Info, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { font, radius, sem, shadow, type SemanticToken } from '../theme/tokens';

export type ToastKind = 'success' | 'error' | 'info';

const kinds: Record<ToastKind, [LucideIcon, SemanticToken]> = {
  success: [CircleCheck, 'feedback-success-text'],
  error: [CircleAlert, 'feedback-danger-text'],
  info: [Info, 'text-accent'],
};

export interface ToastProps {
  kind: ToastKind;
  message: string;
  action?: ReactNode;
}

export function Toast({ kind, message, action }: ToastProps) {
  const [Icon, color] = kinds[kind];

  return (
    <div
      role={kind === 'error' ? 'alert' : 'status'}
      className="flex min-h-12 items-center gap-2 px-3"
      style={{
        width: 420,
        maxWidth: '100%',
        backgroundColor: sem('bg-surface'),
        border: `1px solid ${sem('border-default')}`,
        borderRadius: radius.card,
        boxShadow: shadow.popover,
        fontSize: font.size.xs,
      }}
    >
      <Icon size={16} strokeWidth={1.5} color={sem(color)} aria-hidden style={{ flexShrink: 0 }} />
      <span className="flex-1">{message}</span>
      {action}
    </div>
  );
}
