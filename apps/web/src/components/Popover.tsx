import MuiPopover, { type PopoverProps as MuiPopoverProps } from '@mui/material/Popover';

export type PopoverWidth = 200 | 264 | 508;

export interface PopoverProps extends Omit<MuiPopoverProps, 'anchorOrigin'> {
  width?: PopoverWidth;
}

export function Popover({ width = 264, slotProps, ...props }: PopoverProps) {
  return (
    <MuiPopover
      anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
      slotProps={{ ...slotProps, paper: { sx: { width, marginTop: 1 } } }}
      {...props}
    />
  );
}
