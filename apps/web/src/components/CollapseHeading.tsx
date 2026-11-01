import ButtonBase from '@mui/material/ButtonBase';
import Typography, { type TypographyProps } from '@mui/material/Typography';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { radius } from '../theme/tokens';

interface CollapseHeadingProps {
  label: string;
  open: boolean;
  variant?: TypographyProps['variant'];
  onToggle: () => void;
}

/** A section heading that folds the section under it. */
export function CollapseHeading({ label, open, variant = 'h3', onToggle }: CollapseHeadingProps) {
  const Icon = open ? ChevronDown : ChevronRight;

  return (
    <Typography variant={variant} component="h2">
      <ButtonBase
        aria-expanded={open}
        onClick={onToggle}
        className="flex items-center gap-1"
        sx={{ font: 'inherit', borderRadius: `${radius.control}px` }}
      >
        <Icon size={16} strokeWidth={1.5} aria-hidden />
        {label}
      </ButtonBase>
    </Typography>
  );
}
