import MuiMenuItem, { type MenuItemProps as MuiMenuItemProps } from '@mui/material/MenuItem';
import type { IconComponent } from './icons';
import { radius, sem, size } from '../theme/tokens';

export interface MenuItemProps extends MuiMenuItemProps {
  icon?: IconComponent;
  danger?: boolean;
}

export function MenuItem({ icon: Icon, danger, children, sx, ...props }: MenuItemProps) {
  return (
    <MuiMenuItem
      sx={[
        {
          minHeight: size.menuItem,
          gap: 2,
          paddingInline: 2,
          borderRadius: `${radius.control}px`,
          color: sem(danger ? 'feedback-danger-text' : 'text-primary'),
          '&:hover': { backgroundColor: sem('bg-hover') },
        },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
      {...props}
    >
      {Icon && <Icon size={16} strokeWidth={1.5} aria-hidden />}
      {children}
    </MuiMenuItem>
  );
}
