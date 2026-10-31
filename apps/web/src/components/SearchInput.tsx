import InputBase, { type InputBaseProps } from '@mui/material/InputBase';
import { Search } from 'lucide-react';
import { radius, sem, size } from '../theme/tokens';

export interface SearchInputProps extends Omit<InputBaseProps, 'startAdornment'> {
  label: string;
  width?: number;
}

export function SearchInput({ label, width = 200, ...props }: SearchInputProps) {
  return (
    <InputBase
      placeholder={label}
      inputProps={{ 'aria-label': label }}
      startAdornment={<Search size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />}
      sx={{
        width,
        height: size.control,
        gap: 2,
        paddingInline: 2,
        borderRadius: `${radius.control}px`,
        backgroundColor: sem('bg-subtle'),
        color: sem('text-primary'),
        '& input::placeholder': { color: sem('text-tertiary'), opacity: 1 },
        '&.Mui-focused': { outline: `2px solid ${sem('border-focus')}` },
      }}
      {...props}
    />
  );
}
