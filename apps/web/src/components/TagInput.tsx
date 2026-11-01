import Autocomplete from '@mui/material/Autocomplete';
import Chip from '@mui/material/Chip';
import MuiTextField from '@mui/material/TextField';
import { useId, useState } from 'react';
import { font, sem } from '../theme/tokens';

export interface TagInputProps {
  label: string;
  value: string[];
  options: string[];
  /** For property lists: no visible label, border only on hover and focus. */
  hideLabel?: boolean;
  onChange: (tags: string[]) => void;
}

const SEPARATOR = /[,\n]/;

/** Enter or a comma turns the text into a tag; leaving the field keeps what was typed. */
export function TagInput({ label, value, options, hideLabel, onChange }: TagInputProps) {
  const id = useId();
  const [text, setText] = useState('');

  const unique = (tags: string[]) => {
    const merged: string[] = [];
    for (const tag of tags.map((item) => item.trim()).filter(Boolean)) {
      if (!merged.some((item) => item.toLowerCase() === tag.toLowerCase())) {
        merged.push(tag);
      }
    }
    return merged;
  };

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className={hideLabel ? 'sr-only' : undefined} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
        {label}
      </label>
      <Autocomplete
        id={id}
        multiple
        freeSolo
        autoSelect
        filterSelectedOptions
        size="small"
        options={options}
        value={value}
        inputValue={text}
        onInputChange={(_, next, reason) => {
          if (reason === 'input' && SEPARATOR.test(next)) {
            onChange(unique([...value, ...next.split(SEPARATOR)]));
            setText('');
          } else {
            setText(next);
          }
        }}
        onChange={(_, next) => onChange(unique(next))}
        renderValue={(tags, getItemProps) =>
          tags.map((tag, index) => {
            const { key, ...item } = getItemProps({ index });
            return <Chip key={key} size="small" label={tag} {...item} />;
          })
        }
        renderInput={(params) => (
          <MuiTextField
            {...params}
            sx={hideLabel ? { '&:not(:hover):not(:focus-within) fieldset': { borderColor: 'transparent' } } : undefined}
          />
        )}
      />
    </div>
  );
}
