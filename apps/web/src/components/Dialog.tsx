import MuiDialog, { type DialogProps as MuiDialogProps } from '@mui/material/Dialog';
import Typography from '@mui/material/Typography';
import { createContext, use, useEffect, useId, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { sem } from '../theme/tokens';
import { Button, type ButtonKind } from './Button';

export type DialogWidth = 480 | 520 | 560 | 640;

export interface DialogProps extends Omit<MuiDialogProps, 'title' | 'maxWidth'> {
  /** Runs once the opening transition ends; autoFocus alone does not survive it. */
  onOpened?: () => void;
  title: string;
  width?: DialogWidth;
  /** Stays in view below the body, which scrolls when it is too tall for the window. */
  footer?: ReactNode;
}

/** The footer of the dialog around a component, for {@link DialogActions}; null outside a dialog. */
const FooterSlot = createContext<HTMLElement | null>(null);

/** True while the body is taller than the room it has, so the edges show where it scrolls. */
function useOverflows(element: HTMLElement | null) {
  const [overflows, setOverflows] = useState(false);
  useEffect(() => {
    if (!element) {
      return;
    }
    const check = () => setOverflows(element.scrollHeight > element.clientHeight + 1);
    const observer = new ResizeObserver(check);
    observer.observe(element);
    for (const child of Array.from(element.children)) {
      observer.observe(child);
    }
    const mutations = new MutationObserver(check);
    mutations.observe(element, { childList: true, subtree: true });
    check();
    return () => {
      observer.disconnect();
      mutations.disconnect();
    };
  }, [element]);
  return overflows;
}

export function Dialog({ title, width = 480, footer, onOpened, children, ...props }: DialogProps) {
  const titleId = useId();
  const [body, setBody] = useState<HTMLDivElement | null>(null);
  const [footerSlot, setFooterSlot] = useState<HTMLDivElement | null>(null);
  const overflows = useOverflows(body);
  const edge = overflows ? `1px solid ${sem('border-default')}` : '1px solid transparent';

  return (
    <MuiDialog
      aria-labelledby={titleId}
      maxWidth={false}
      slotProps={{ paper: { sx: { width, maxWidth: 'calc(100% - 32px)', overflow: 'hidden' } }, transition: { onEntered: onOpened } }}
      {...props}
    >
      <div className="shrink-0 px-6 pt-6 pb-4" style={{ borderBottom: edge }}>
        <Typography id={titleId} variant="h2" component="h2">
          {title}
        </Typography>
      </div>
      {/* The small vertical padding keeps focus rings of the first and last field from being cut off. */}
      <div ref={setBody} className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-6 py-1">
        <FooterSlot value={footerSlot}>{children}</FooterSlot>
      </div>
      <div ref={setFooterSlot} className="shrink-0 px-6 pt-4 pb-6 empty:pt-2" style={{ borderTop: edge }}>
        {footer}
      </div>
    </MuiDialog>
  );
}

/**
 * Actions rendered by a component that also appears outside dialogs: inside one they move to its fixed footer,
 * elsewhere they stay where they are.
 */
export function DialogActions({ children }: { children: ReactNode }) {
  const slot = use(FooterSlot);
  return slot ? createPortal(children, slot) : children;
}

export interface DialogFooterProps {
  confirmLabel: string;
  confirmKind?: ButtonKind;
  confirmDisabled?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

export function DialogFooter({ confirmLabel, confirmKind = 'primary', confirmDisabled, onCancel, onConfirm }: DialogFooterProps) {
  const { t } = useTranslation();

  return (
    <div className="flex justify-end gap-2">
      <Button kind="ghost" onClick={onCancel}>
        {t('common.cancel')}
      </Button>
      <Button kind={confirmKind} disabled={confirmDisabled} onClick={onConfirm}>
        {confirmLabel}
      </Button>
    </div>
  );
}

export interface DialogConfirmProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  onCancel: () => void;
  onConfirm: () => void;
}

export function DialogConfirm({ open, title, description, confirmLabel, onCancel, onConfirm }: DialogConfirmProps) {
  return (
    <Dialog
      open={open}
      title={title}
      onClose={onCancel}
      footer={
        <DialogFooter confirmLabel={confirmLabel} confirmKind="danger" onCancel={onCancel} onConfirm={onConfirm} />
      }
    >
      <Typography color="textSecondary">{description}</Typography>
    </Dialog>
  );
}
