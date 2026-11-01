import MuiPopover, { type PopoverProps as MuiPopoverProps } from '@mui/material/Popover';

export type PopoverWidth = 200 | 264 | 360 | 508;

export interface PopoverProps extends Omit<MuiPopoverProps, 'anchorOrigin'> {
  width?: PopoverWidth;
  /** Opens upward, for triggers near the end of the content. */
  above?: boolean;
}

export function Popover({ width = 264, above, slotProps, ...props }: PopoverProps) {
  return (
    <MuiPopover
      anchorOrigin={{ vertical: above ? 'top' : 'bottom', horizontal: 'left' }}
      transformOrigin={{ vertical: above ? 'bottom' : 'top', horizontal: 'left' }}
      slotProps={{ ...slotProps, paper: { sx: { width, marginTop: above ? -1 : 1 } } }}
      {...props}
    />
  );
}
