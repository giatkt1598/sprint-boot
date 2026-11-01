import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Tooltip from '@mui/material/Tooltip';
import { Copy } from 'lucide-react';
import { TextField } from './TextField';
import { useToast } from './ToastHost';

/** A value people paste elsewhere: it cannot be edited, and one click copies it. */
export function CopyField({ label, value, copyLabel, copiedMessage }: { label: string; value: string; copyLabel: string; copiedMessage: string }) {
  const toast = useToast();

  const copy = async () => {
    await navigator.clipboard.writeText(value);
    toast('success', copiedMessage);
  };

  return (
    <TextField
      label={label}
      readOnly
      value={value}
      endAdornment={
        <InputAdornment position="end">
          <Tooltip title={copyLabel}>
            <IconButton size="small" aria-label={copyLabel} onClick={() => void copy()}>
              <Copy size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </Tooltip>
        </InputAdornment>
      }
    />
  );
}
