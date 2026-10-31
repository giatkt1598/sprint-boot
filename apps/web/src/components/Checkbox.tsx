import FormControlLabel from '@mui/material/FormControlLabel';
import MuiCheckbox, { type CheckboxProps as MuiCheckboxProps } from '@mui/material/Checkbox';
import MuiSwitch, { type SwitchProps } from '@mui/material/Switch';
import { Check } from 'lucide-react';
import { sem } from '../theme/tokens';

const box = { width: 16, height: 16, borderRadius: 3, display: 'flex', alignItems: 'center', justifyContent: 'center' };

export interface CheckboxProps extends MuiCheckboxProps {
  label: string;
}

export function Checkbox({ label, ...props }: CheckboxProps) {
  return (
    <FormControlLabel
      label={label}
      sx={{ marginInline: 0, gap: 2 }}
      control={
        <MuiCheckbox
          sx={{ padding: 1 }}
          icon={<span style={{ ...box, border: `1px solid ${sem('border-strong')}` }} />}
          checkedIcon={
            <span style={{ ...box, backgroundColor: sem('accent-default'), color: sem('text-on-accent') }}>
              <Check size={12} strokeWidth={2.5} aria-hidden />
            </span>
          }
          {...props}
        />
      }
    />
  );
}

export interface ToggleProps extends SwitchProps {
  label: string;
}

export function Toggle({ label, ...props }: ToggleProps) {
  return (
    <FormControlLabel
      label={label}
      sx={{ marginInline: 0, gap: 2 }}
      control={
        <MuiSwitch
          disableRipple
          sx={{
            width: 36,
            height: 20,
            padding: 0,
            '& .MuiSwitch-switchBase': {
              padding: '2px',
              '&.Mui-checked': { transform: 'translateX(16px)', color: sem('text-on-accent') },
              '&.Mui-checked + .MuiSwitch-track': { backgroundColor: sem('accent-default'), opacity: 1 },
            },
            '& .MuiSwitch-thumb': { width: 16, height: 16, color: sem('bg-surface') },
            '& .MuiSwitch-track': { borderRadius: 999, backgroundColor: sem('text-tertiary'), opacity: 1 },
          }}
          {...props}
        />
      }
    />
  );
}
