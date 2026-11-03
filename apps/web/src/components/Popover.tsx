import MuiPopover, { type PopoverProps as MuiPopoverProps } from '@mui/material/Popover';

/** 132 is for short pickers such as issue types, whose labels are a word or two. */
export type PopoverWidth = 132 | 200 | 264 | 360 | 508;

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
