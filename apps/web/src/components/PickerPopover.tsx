import ButtonBase from '@mui/material/ButtonBase';
import { Check } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { font, radius, sem } from '../theme/tokens';
import { Popover } from './Popover';
import { SearchInput } from './SearchInput';

export interface PickerItem {
  id: string;
  label: string;
  /** Decorative: the label already says what it means. */
  icon?: ReactNode;
}

interface PickerPopoverProps {
  label: string;
  anchor: HTMLElement | null;
  items: PickerItem[];
  selectedId?: string | null;
  /** Adds a search box above the list. */
  searchLabel?: string;
  onClose: () => void;
  onPick: (id: string) => void;
}

/** Everything that picks one value from a popover looks and behaves the same. */
export function PickerPopover({ label, anchor, items, selectedId, searchLabel, onClose, onPick }: PickerPopoverProps) {
  const [search, setSearch] = useState('');
  const input = useRef<HTMLInputElement>(null);
  const text = search.toLowerCase();
  const matches = items.filter((item) => item.label.toLowerCase().includes(text));

  const close = (action: () => void) => {
    setSearch('');
    action();
  };

  return (
    <Popover
      open={Boolean(anchor)}
      anchorEl={anchor}
      onClose={() => close(onClose)}
      // The popover moves focus to itself while opening, which undoes autoFocus.
      slotProps={{ transition: { onEntered: () => input.current?.focus() } }}
    >
      <div role="group" aria-label={label} className="flex flex-col gap-1 p-1">
        {searchLabel && (
          <SearchInput label={searchLabel} width="100%" inputRef={input} value={search} onChange={(event) => setSearch(event.target.value)} />
        )}
        <div className="flex max-h-64 flex-col gap-px overflow-y-auto">
          {matches.map((item) => {
            const selected = item.id === selectedId;
            return (
              <ButtonBase
                key={item.id}
                aria-pressed={selected}
                onClick={() => close(() => onPick(item.id))}
                className="flex items-center gap-2"
                sx={{
                  justifyContent: 'flex-start',
                  padding: 1,
                  textAlign: 'left',
                  fontSize: font.size.md,
                  borderRadius: `${radius.control}px`,
                  backgroundColor: selected ? sem('border-default') : undefined,
                  '&:hover': { backgroundColor: sem(selected ? 'border-default' : 'bg-hover') },
                }}
              >
                {item.icon && <span aria-hidden className="flex shrink-0">{item.icon}</span>}
                <span className="min-w-0 flex-1">{item.label}</span>
                {selected && <Check size={16} strokeWidth={1.5} color={sem('accent-default')} aria-hidden style={{ flexShrink: 0 }} />}
              </ButtonBase>
            );
          })}
        </div>
      </div>
    </Popover>
  );
}
