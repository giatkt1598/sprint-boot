import FormHelperText from '@mui/material/FormHelperText';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput, { type OutlinedInputProps } from '@mui/material/OutlinedInput';
import Select, { type SelectProps } from '@mui/material/Select';
import { useId, type ReactNode } from 'react';
import { font, sem } from '../theme/tokens';

interface FieldFrameProps {
  label: string;
  htmlFor: string;
  labelId?: string;
  helperId: string;
  error?: string;
  children: ReactNode;
}

function FieldFrame({ label, htmlFor, labelId, helperId, error, children }: FieldFrameProps) {
  return (
    <div className="flex flex-col gap-1">
      <label
        id={labelId}
        htmlFor={htmlFor}
        style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}
      >
        {label}
      </label>
      {children}
      {error && (
        <FormHelperText id={helperId} error sx={{ margin: 0, fontSize: font.size.xs }}>
          {error}
        </FormHelperText>
      )}
    </div>
  );
}

const disabledStyles = {
  '&.Mui-disabled': { backgroundColor: sem('bg-subtle') },
  '&.Mui-disabled .MuiOutlinedInput-notchedOutline': { border: 'none' },
};

export interface TextFieldProps extends Omit<OutlinedInputProps, 'error' | 'label'> {
  label: string;
  error?: string;
}

export function TextField({ label, error, id, sx, ...props }: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const helperId = `${inputId}-helper`;

  return (
    <FieldFrame label={label} htmlFor={inputId} helperId={helperId} error={error}>
      <OutlinedInput
        id={inputId}
        error={Boolean(error)}
        aria-describedby={error ? helperId : undefined}
        sx={[disabledStyles, ...(Array.isArray(sx) ? sx : [sx])]}
        {...props}
      />
    </FieldFrame>
  );
}

export interface SelectOption {
  value: string;
  label: string;
}

export interface SelectFieldProps extends Omit<SelectProps<string>, 'error' | 'label' | 'variant'> {
  label: string;
  options: SelectOption[];
  error?: string;
}

export function SelectField({ label, options, error, id, ...props }: SelectFieldProps) {
  const generatedId = useId();
  const selectId = id ?? generatedId;
  const labelId = `${selectId}-label`;

  return (
    <FieldFrame label={label} htmlFor={selectId} labelId={labelId} helperId={`${selectId}-helper`} error={error}>
      <Select
        id={selectId}
        labelId={labelId}
        error={Boolean(error)}
        input={<OutlinedInput />}
        {...props}
      >
        {options.map((option) => (
          <MenuItem key={option.value} value={option.value}>
            {option.label}
          </MenuItem>
        ))}
      </Select>
    </FieldFrame>
  );
}
