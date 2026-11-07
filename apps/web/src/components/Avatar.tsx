import MuiAvatar from '@mui/material/Avatar';
import { User } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { font, sem } from '../theme/tokens';

const initials = (name: string) => {
  const words = name.trim().split(/\s+/);
  const letters = words.length > 1 ? [words[0], words[words.length - 1]] : [words[0]];
  return letters.map((word) => word?.[0]?.toUpperCase() ?? '').join('');
};

const avatarStyles = {
  width: 24,
  height: 24,
  fontSize: font.size.xs,
  fontWeight: font.weight.medium,
  backgroundColor: sem('status-todo-bg'),
  color: sem('status-todo-text'),
};

/**
 * `decorative` draws the initials in CSS, for controls whose accessible name is the person's full name.
 * Without a picture, or while it fails to load, the initials show instead.
 * `noTitle` drops the browser tooltip, for places that show a styled one instead.
 */
export function Avatar({ name, src, decorative, size = 24, noTitle }: { name: string; src?: string | null; decorative?: boolean; size?: number; noTitle?: boolean }) {
  return (
    <MuiAvatar
      src={src ?? undefined}
      alt={name}
      title={noTitle ? undefined : name}
      sx={[
        avatarStyles,
        size === 24 ? {} : { width: size, height: size, fontSize: size >= 48 ? font.size.lg : font.size.xs },
        { '& [data-initials]::before': { content: 'attr(data-initials)' } },
      ]}
    >
      {decorative ? <span data-initials={initials(name)} /> : initials(name)}
    </MuiAvatar>
  );
}

/** Stands in for the person on anything nobody is assigned to. */
export function UnassignedAvatar({ noTitle }: { noTitle?: boolean } = {}) {
  const { t } = useTranslation();
  const label = t('common.unassigned');

  return (
    <MuiAvatar role="img" aria-label={label} title={noTitle ? undefined : label} sx={avatarStyles}>
      <User size={16} strokeWidth={1.5} aria-hidden />
    </MuiAvatar>
  );
}
