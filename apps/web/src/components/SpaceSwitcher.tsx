import ButtonBase from '@mui/material/ButtonBase';
import { ChevronsUpDown } from 'lucide-react';
import type { MouseEvent } from 'react';
import { font, radius, sem } from '../theme/tokens';

export interface SpaceSwitcherProps {
  spaceKey: string;
  name: string;
  label: string;
  onClick?: (event: MouseEvent<HTMLElement>) => void;
}

export function SpaceSwitcher({ spaceKey, name, label, onClick }: SpaceSwitcherProps) {
  return (
    <ButtonBase
      onClick={onClick}
      className="flex h-10 w-full items-center gap-2 px-2"
      sx={{
        justifyContent: 'flex-start',
        border: `1px solid ${sem('border-default')}`,
        borderRadius: `${radius.control}px`,
        '&:hover': { backgroundColor: sem('bg-hover') },
      }}
    >
      <span
        className="flex size-6 shrink-0 items-center justify-center"
        style={{
          backgroundColor: sem('accent-default'),
          color: sem('text-on-accent'),
          borderRadius: radius.control,
          fontSize: font.size.xs,
          fontWeight: font.weight.semibold,
        }}
      >
        {spaceKey}
      </span>
      <span className="flex-1 truncate text-left" style={{ fontSize: font.size.md, fontWeight: font.weight.medium }}>
        {name}
      </span>
      <span className="sr-only">{label}</span>
      <ChevronsUpDown size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />
    </ButtonBase>
  );
}
