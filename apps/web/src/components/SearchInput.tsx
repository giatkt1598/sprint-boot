import InputBase, { type InputBaseProps } from '@mui/material/InputBase';
import { SearchIcon } from './icons';
import { radius, sem, size } from '../theme/tokens';

export interface SearchInputProps extends Omit<InputBaseProps, 'startAdornment'> {
  label: string;
  width?: number | string;
}

export function SearchInput({ label, width = 200, sx, ...props }: SearchInputProps) {
  return (
    <InputBase
      placeholder={label}
      inputProps={{ 'aria-label': label }}
      startAdornment={<SearchIcon size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />}
      sx={[{
        width,
        height: size.control,
        gap: 2,
        paddingInline: 2,
        borderRadius: `${radius.control}px`,
        backgroundColor: 'var(--sb-subtle-field-bg)',
        color: sem('text-primary'),
        '& input::placeholder': { color: sem('text-tertiary'), opacity: 1 },
        '&.Mui-focused': { outline: `2px solid ${sem('border-focus')}` },
      }, ...(Array.isArray(sx) ? sx : [sx])]}
      {...props}
    />
  );
}
