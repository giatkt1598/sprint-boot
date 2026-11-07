import FormHelperText from '@mui/material/FormHelperText';
import Tooltip from '@mui/material/Tooltip';
import { CircleHelp } from 'lucide-react';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput, { type OutlinedInputProps } from '@mui/material/OutlinedInput';
import Select, { type SelectProps } from '@mui/material/Select';
import { useId, useState, type ReactNode } from 'react';
import { font, sem } from '../theme/tokens';

interface FieldFrameProps {
  label: string;
  /** Shown in a tooltip on a ? icon right of the label. */
  hint?: string;
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

function FieldFrame({ label, hint, htmlFor, labelId, helperId, error, quiet, children }: FieldFrameProps) {
  const labelElement = (
    <label
      id={labelId}
      htmlFor={htmlFor}
      className={quiet ? 'sr-only' : undefined}
      style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}
    >
      {label}
    </label>
  );

  return (
    <div className="flex flex-col gap-1">
      {hint && !quiet ? (
        <span className="flex items-center gap-1">
          {labelElement}
          <Tooltip title={hint} describeChild>
            <span
              role="img"
              tabIndex={0}
              aria-label={hint}
              className="flex"
              style={{ color: sem('text-tertiary'), cursor: 'help' }}
            >
              <CircleHelp size={14} strokeWidth={1.5} aria-hidden />
            </span>
          </Tooltip>
        </span>
      ) : (
        labelElement
      )}
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
  /** Shown in a tooltip on a ? icon right of the label. */
  hint?: string;
  error?: string;
  quiet?: boolean;
}

export function TextField({ label, hint, error, quiet, id, sx, ...props }: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const helperId = `${inputId}-helper`;

  return (
    <FieldFrame label={label} hint={hint} htmlFor={inputId} helperId={helperId} error={error} quiet={quiet}>
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

export interface BufferedTextFieldProps extends Omit<TextFieldProps, 'value' | 'onChange'> {
  value: string;
  /** Called on blur, so keystrokes re-render this field and not the whole form. */
  onCommit: (value: string) => void;
  /** Checks the text as it is typed, once there has been typing. */
  validate?: (draft: string) => string | undefined;
}

/**
 * For large forms. Holds its own text and takes the initial value once:
 * give it a new key to reset it.
 */
export function BufferedTextField({ value, onCommit, validate, error, onBlur, ...props }: BufferedTextFieldProps) {
  const [draft, setDraft] = useState(value);
  const [typed, setTyped] = useState(false);

  return (
    <TextField
      {...props}
      value={draft}
      error={error ?? (typed ? validate?.(draft) : undefined)}
      onChange={(event) => {
        setDraft(event.target.value);
        setTyped(true);
      }}
      onBlur={(event) => {
        onCommit(draft);
        onBlur?.(event);
      }}
    />
  );
}

export interface SelectOption {
  value: string;
  label: string;
  /** Decorative: the label already says what it means. */
  icon?: ReactNode;
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
            {option.icon ? (
              <span className="flex items-center gap-2">
                <span aria-hidden className="flex">{option.icon}</span>
                {option.label}
              </span>
            ) : option.label}
          </MenuItem>
        ))}
      </Select>
    </FieldFrame>
  );
}
