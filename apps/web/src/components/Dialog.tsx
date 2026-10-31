import MuiDialog, { type DialogProps as MuiDialogProps } from '@mui/material/Dialog';
import Typography from '@mui/material/Typography';
import { useId, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, type ButtonKind } from './Button';

export type DialogWidth = 480 | 520 | 560 | 640;

export interface DialogProps extends Omit<MuiDialogProps, 'title' | 'maxWidth'> {
  title: string;
  width?: DialogWidth;
  footer?: ReactNode;
}

export function Dialog({ title, width = 480, footer, children, ...props }: DialogProps) {
  const titleId = useId();

  return (
    <MuiDialog
      aria-labelledby={titleId}
      maxWidth={false}
      slotProps={{ paper: { sx: { width, maxWidth: 'calc(100% - 32px)' } } }}
      {...props}
    >
      <div className="flex flex-col gap-4 p-6">
        <Typography id={titleId} variant="h2" component="h2">
          {title}
        </Typography>
        {children}
        {footer}
      </div>
    </MuiDialog>
  );
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
