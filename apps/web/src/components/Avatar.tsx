import MuiAvatar from '@mui/material/Avatar';
import { font, sem } from '../theme/tokens';

const initials = (name: string) => {
  const words = name.trim().split(/\s+/);
  const letters = words.length > 1 ? [words[0], words[words.length - 1]] : [words[0]];
  return letters.map((word) => word?.[0]?.toUpperCase() ?? '').join('');
};

export function Avatar({ name, src }: { name: string; src?: string }) {
  return (
    <MuiAvatar
      src={src}
      alt={name}
      title={name}
      sx={{
        width: 24,
        height: 24,
        fontSize: font.size.xs,
        fontWeight: font.weight.medium,
        backgroundColor: sem('status-todo-bg'),
        color: sem('status-todo-text'),
      }}
    >
      {initials(name)}
    </MuiAvatar>
  );
}
