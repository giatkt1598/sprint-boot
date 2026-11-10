import Drawer from '@mui/material/Drawer';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useCompact } from '../lib/responsive';
import { radius, sem } from '../theme/tokens';
import { Button } from './Button';
import { Popover, type PopoverWidth } from './Popover';

interface SheetProps {
  /** Heads the sheet on small screens, where nothing on screen says what it is for. */
  title: string;
  anchor: HTMLElement | null;
  onClose: () => void;
  width?: PopoverWidth;
  children: ReactNode;
}

/**
 * A panel of choices: a popover beside its button on wide screens, a sheet rising from the bottom on small ones, where
 * a popover would be squeezed against the edges and hard to reach with a thumb.
 */
export function Sheet({ title, anchor, onClose, width, children }: SheetProps) {
  const { t } = useTranslation();
  const compact = useCompact();

  if (!compact) {
    return (
      <Popover width={width} open={Boolean(anchor)} anchorEl={anchor} onClose={onClose}>
        {children}
      </Popover>
    );
  }

  return (
    <Drawer
      anchor="bottom"
      open={Boolean(anchor)}
      onClose={onClose}
      slotProps={{
        paper: {
          role: 'dialog',
          'aria-label': title,
          sx: { maxHeight: '85vh', borderTopLeftRadius: radius.card, borderTopRightRadius: radius.card, borderTop: `1px solid ${sem('border-default')}` },
        },
      }}
    >
      <div className="flex items-center gap-2 px-4 pt-4 pb-2">
        <Typography variant="h2" component="h2" className="flex-1">{title}</Typography>
        <Button kind="ghost" onClick={onClose}>{t('common.done')}</Button>
      </div>
      <div className="min-h-0 overflow-y-auto px-4 pb-4">{children}</div>
    </Drawer>
  );
}
