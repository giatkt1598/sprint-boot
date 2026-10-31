import ButtonBase from '@mui/material/ButtonBase';
import type { LucideIcon } from 'lucide-react';
import { NavLink } from 'react-router';
import { font, radius, sem } from '../theme/tokens';

export interface SidebarItemProps {
  to: string;
  label: string;
  icon: LucideIcon;
}

export function SidebarItem({ to, label, icon: Icon }: SidebarItemProps) {
  return (
    <ButtonBase
      component={NavLink}
      to={to}
      className="flex h-8 w-full items-center gap-2 px-2"
      sx={{
        justifyContent: 'flex-start',
        borderRadius: `${radius.control}px`,
        fontSize: font.size.md,
        fontWeight: font.weight.medium,
        color: sem('text-secondary'),
        '&:hover': { backgroundColor: sem('bg-hover') },
        '&.active': { backgroundColor: sem('bg-selected'), color: sem('text-accent') },
      }}
    >
      <Icon size={16} strokeWidth={1.5} aria-hidden />
      {label}
    </ButtonBase>
  );
}
