import Autocomplete from '@mui/material/Autocomplete';
import MuiTextField from '@mui/material/TextField';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useId, useState } from 'react';
import { apiClient, unwrap } from '../api';
import { font, sem } from '../theme/tokens';
import { Avatar } from './Avatar';
import type { PickerUser } from './UserPicker';

const SEARCH_DELAY = 250;

export interface EmailAutocompleteProps {
  label: string;
  value: string;
  onChange: (email: string) => void;
  /** People to leave out, such as those already in the space. */
  excludeIds?: string[];
  autoFocus?: boolean;
}

/**
 * Any email can be typed; people who already have an account in this organization are
 * suggested as you type. The directory search only ever returns users of the caller's tenant.
 */
export function EmailAutocomplete({ label, value, onChange, excludeIds = [], autoFocus }: EmailAutocompleteProps) {
  const id = useId();
  const [search, setSearch] = useState(value.trim());

  useEffect(() => {
    const timer = setTimeout(() => setSearch(value.trim()), SEARCH_DELAY);
    return () => clearTimeout(timer);
  }, [value]);

  const people = useQuery({
    queryKey: ['users', 'search', search],
    enabled: search.length > 0,
    staleTime: 30_000,
    queryFn: () => unwrap(apiClient.GET('/api/v1/user-directory/search', { params: { query: { search } } })),
  });
  const options = (people.data ?? []).filter((person) => person.email && !excludeIds.includes(person.id ?? ''));

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
        {label}
      </label>
      <Autocomplete<PickerUser, false, false, true>
        id={id}
        size="small"
        freeSolo
        options={options}
        filterOptions={(list) => list}
        inputValue={value}
        onInputChange={(_, text, reason) => reason !== 'reset' && onChange(text)}
        onChange={(_, picked) => onChange(typeof picked === 'string' ? picked : (picked?.email ?? ''))}
        getOptionLabel={(option) => (typeof option === 'string' ? option : (option.email ?? ''))}
        renderOption={({ key, ...props }, option) => (
          <li key={key} {...props}>
            <span className="flex items-center gap-2">
              <span aria-hidden className="flex"><Avatar name={option.name ?? ''} src={option.avatarUrl} /></span>
              <span className="flex flex-col">
                <span>{option.name}</span>
                <span style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{option.email}</span>
              </span>
            </span>
          </li>
        )}
        renderInput={(params) => <MuiTextField {...params} type="email" autoFocus={autoFocus} />}
      />
    </div>
  );
}
