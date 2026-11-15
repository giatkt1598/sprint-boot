import type { ReactNode } from 'react';
import { font, sem } from '../../theme/tokens';

/** The Details sidebar of a ticket: a label column, then the value. */
export const DETAIL_LABEL_WIDTH = 88;
export const DETAIL_GAP = 12;
// Where every value starts inside its row: the padding of the quiet fields, so read-only text lines up with them.
export const DETAIL_INSET = 8;

/** A read-only detail value, set in like the fields beside it. */
export function DetailText({ children }: { children: ReactNode }) {
  return <span className="flex min-h-8 min-w-0 items-center gap-2" style={{ paddingInline: DETAIL_INSET }}>{children}</span>;
}

export function DetailRow({ label, hint, children }: { label: string; hint?: string | null; children: ReactNode }) {
  return (
    <div className="flex min-h-9 items-center gap-3">
      <span title={hint ?? undefined} style={{ width: DETAIL_LABEL_WIDTH, flexShrink: 0, fontSize: font.size.xs, color: sem('text-tertiary'), overflowWrap: 'anywhere' }}>{label}</span>
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}
