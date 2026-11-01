import Autocomplete from '@mui/material/Autocomplete';
import MuiTextField from '@mui/material/TextField';
import { useId } from 'react';
import { font, sem } from '../theme/tokens';

export interface PickerUser {
  id?: string;
  name?: string | null;
  email?: string | null;
}

export interface UserPickerProps {
  label: string;
  hideLabel?: boolean;
  options: PickerUser[];
  value: PickerUser | null;
  onChange: (user: PickerUser | null) => void;
  onInputChange?: (text: string) => void;
}

export function UserPicker({ label, hideLabel, options, value, onChange, onInputChange }: UserPickerProps) {
  const id = useId();

  return (
    <div className="flex flex-col gap-1">
      {!hideLabel && (
        <label htmlFor={id} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
          {label}
        </label>
      )}
      <Autocomplete
        id={id}
        size="small"
        options={options}
        value={value}
        getOptionLabel={(option) => option.name ?? ''}
        isOptionEqualToValue={(option, selected) => option.id === selected.id}
        onChange={(_, next) => onChange(next)}
        onInputChange={(_, text) => onInputChange?.(text)}
        renderInput={(params) => (
          <MuiTextField
            {...params}
            sx={hideLabel ? { '&:not(:hover):not(:focus-within) fieldset': { borderColor: 'transparent' } } : undefined}
            slotProps={{
              ...params.slotProps,
              htmlInput: { ...params.slotProps.htmlInput, 'aria-label': hideLabel ? label : undefined },
            }}
          />
        )}
      />
    </div>
  );
}
