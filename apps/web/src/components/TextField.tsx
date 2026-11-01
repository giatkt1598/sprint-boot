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
  quiet?: boolean;
  children: ReactNode;
}

/** Value-first look for property lists: no visible label, border only on hover and focus. */
const quietStyles = {
  '&:not(:hover):not(.Mui-focused) .MuiOutlinedInput-notchedOutline': { borderColor: 'transparent' },
  '& .MuiOutlinedInput-input': { paddingInline: '8px' },
};

function FieldFrame({ label, htmlFor, labelId, helperId, error, quiet, children }: FieldFrameProps) {
  return (
    <div className="flex flex-col gap-1">
      <label
        id={labelId}
        htmlFor={htmlFor}
        className={quiet ? 'sr-only' : undefined}
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
  quiet?: boolean;
}

export function TextField({ label, error, quiet, id, sx, ...props }: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const helperId = `${inputId}-helper`;

  return (
    <FieldFrame label={label} htmlFor={inputId} helperId={helperId} error={error} quiet={quiet}>
      <OutlinedInput
        id={inputId}
        error={Boolean(error)}
        aria-describedby={error ? helperId : undefined}
        sx={[disabledStyles, quiet ? quietStyles : {}, ...(Array.isArray(sx) ? sx : [sx])]}
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
  quiet?: boolean;
}

export function SelectField({ label, options, error, quiet, id, ...props }: SelectFieldProps) {
  const generatedId = useId();
  const selectId = id ?? generatedId;
  const labelId = `${selectId}-label`;

  return (
    <FieldFrame label={label} htmlFor={selectId} labelId={labelId} helperId={`${selectId}-helper`} error={error} quiet={quiet}>
      <Select
        id={selectId}
        labelId={labelId}
        error={Boolean(error)}
        input={<OutlinedInput sx={quiet ? quietStyles : undefined} />}
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
