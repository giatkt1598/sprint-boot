import MuiButton, { type ButtonProps as MuiButtonProps } from '@mui/material/Button';
import type { ElementType } from 'react';
import { font, sem } from '../theme/tokens';

export type ButtonKind = 'primary' | 'secondary' | 'ghost' | 'danger' | 'link';

const kindStyles = {
  primary: {
    backgroundColor: sem('accent-default'),
    color: sem('text-on-accent'),
    '&:hover': { backgroundColor: sem('accent-hover') },
    '&.Mui-disabled': { backgroundColor: sem('accent-default'), color: sem('text-on-accent') },
  },
  secondary: {
    border: `1px solid ${sem('border-strong')}`,
    color: sem('text-primary'),
    '&:hover': { backgroundColor: sem('bg-hover') },
    '&.Mui-disabled': { border: `1px solid ${sem('border-strong')}`, color: sem('text-primary') },
  },
  ghost: {
    color: sem('text-secondary'),
    '&:hover': { backgroundColor: sem('bg-hover') },
    '&.Mui-disabled': { color: sem('text-secondary') },
  },
  link: {
    fontSize: font.size.xs,
    color: sem('text-accent'),
    '&:hover': { backgroundColor: sem('bg-hover') },
  },
  danger: {
    backgroundColor: sem('feedback-danger-text'),
    color: sem('text-on-accent'),
    '&:hover': { backgroundColor: sem('feedback-danger-hover') },
    '&.Mui-disabled': { backgroundColor: sem('feedback-danger-text'), color: sem('text-on-accent') },
  },
} as const;

export interface ButtonProps extends Omit<MuiButtonProps, 'variant' | 'color'> {
  kind?: ButtonKind;
  component?: ElementType;
  to?: string;
}

export function Button({ kind = 'primary', sx, ...props }: ButtonProps) {
  return <MuiButton variant="text" sx={[kindStyles[kind], ...(Array.isArray(sx) ? sx : [sx])]} {...props} />;
}
