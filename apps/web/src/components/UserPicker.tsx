import Autocomplete from '@mui/material/Autocomplete';
import InputAdornment from '@mui/material/InputAdornment';
import MuiTextField from '@mui/material/TextField';
import { useId } from 'react';
import { font, sem } from '../theme/tokens';
import { Avatar, UnassignedAvatar } from './Avatar';

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
  /** Shown like a value, with the default avatar, when nobody is picked. */
  emptyLabel?: string;
}

export function UserPicker({ label, hideLabel, options, value, onChange, onInputChange, emptyLabel }: UserPickerProps) {
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
        // The clear button sits on the arrow, so names get all the width up to it.
        sx={{
          '& .MuiAutocomplete-inputRoot.MuiOutlinedInput-root': { paddingRight: '28px' },
          '& .MuiAutocomplete-endAdornment': { right: '4px' },
          '& .MuiAutocomplete-clearIndicator': { position: 'absolute', right: 0, zIndex: 1, marginRight: 0, backgroundColor: sem('bg-surface') },
          '& .MuiAutocomplete-clearIndicator:hover': { backgroundColor: sem('bg-hover') },
        }}
        options={options}
        value={value}
        getOptionLabel={(option) => option.name ?? ''}
        isOptionEqualToValue={(option, selected) => option.id === selected.id}
        onChange={(_, next) => onChange(next)}
        onInputChange={(_, text) => onInputChange?.(text)}
        renderOption={({ key, ...props }, option) => (
          <li key={key} {...props}>
            <span className="flex items-center gap-2">
              <span aria-hidden className="flex"><Avatar name={option.name ?? ''} /></span>
              {option.name}
            </span>
          </li>
        )}
        renderInput={(params) => (
          <MuiTextField
            {...params}
            placeholder={emptyLabel}
            sx={[
              hideLabel ? { '&:not(:hover):not(:focus-within) fieldset': { borderColor: 'transparent' } } : {},
              emptyLabel ? { '& input::placeholder': { color: sem('text-primary'), opacity: 1 } } : {},
            ]}
            slotProps={{
              ...params.slotProps,
              input: {
                ...params.slotProps.input,
                startAdornment: value?.name ? (
                  <InputAdornment position="start" aria-hidden><Avatar name={value.name} /></InputAdornment>
                ) : emptyLabel ? (
                  <InputAdornment position="start" aria-hidden><UnassignedAvatar /></InputAdornment>
                ) : undefined,
              },
              htmlInput: { ...params.slotProps.htmlInput, 'aria-label': hideLabel ? label : undefined },
            }}
          />
        )}
      />
    </div>
  );
}
