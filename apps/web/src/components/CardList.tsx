import IconButton from '@mui/material/IconButton';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { font, radius, sem } from '../theme/tokens';
import { ArrowDownIcon, ArrowUpIcon } from './icons';
import type { SortDirection, TableSort } from './Table';
import { SelectField } from './TextField';

/**
 * A list page's rows as cards, for small screens where its table cannot fit. Each card reads top to bottom: the key
 * line, the title, then the people and dates.
 */
export function CardList({ label, children }: { label: string; children: ReactNode }) {
  return <ul aria-label={label} className="m-0 flex list-none flex-col gap-2 p-0">{children}</ul>;
}

export function ListCard({ children }: { children: ReactNode }) {
  return (
    <li
      className="sb-card flex flex-col gap-2 p-3"
      style={{ backgroundColor: 'var(--sb-card-bg)', border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
    >
      {children}
    </li>
  );
}

/** A card's line of small secondary text, such as its key and status or its people and dates. */
export function CardMeta({ children }: { children: ReactNode }) {
  return <div className="flex min-w-0 items-center gap-2" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{children}</div>;
}

/** The column headers sort on wide screens; cards have none, so a select and a direction button do it. */
export function SortSelect({ sort, columns, onChange }: { sort: TableSort | null; columns: { key: string; label: string }[]; onChange: (key: string, direction: SortDirection) => void }) {
  const { t } = useTranslation();
  const direction = sort?.direction ?? 'asc';
  const DirectionIcon = direction === 'asc' ? ArrowUpIcon : ArrowDownIcon;

  return (
    <div className="flex items-end gap-1">
      <div className="min-w-0 flex-1">
        <SelectField
          label={t('tickets.sortBy')}
          value={sort?.key ?? ''}
          displayEmpty
          options={[{ value: '', label: t('tickets.defaultOrder') }, ...columns.map((column) => ({ value: column.key, label: column.label }))]}
          onChange={(event) => onChange(String(event.target.value), direction)}
        />
      </div>
      <IconButton
        aria-label={t(direction === 'asc' ? 'tickets.sortAscending' : 'tickets.sortDescending')}
        disabled={!sort}
        onClick={() => sort && onChange(sort.key, direction === 'asc' ? 'desc' : 'asc')}
      >
        <DirectionIcon size={20} strokeWidth={1.5} aria-hidden />
      </IconButton>
    </div>
  );
}
