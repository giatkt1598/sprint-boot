import ButtonBase from '@mui/material/ButtonBase';
import Chip from '@mui/material/Chip';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { DatePicker } from '../../components/DatePicker';
import { ColumnsIcon } from '../../components/icons';
import { Sheet } from '../../components/Sheet';
import { BufferedTextField } from '../../components/TextField';
import type { PickerUser } from '../../components/UserPicker';
import { formatDate } from '../../lib/format';
import { useCompact } from '../../lib/responsive';
import { font, radius, sem } from '../../theme/tokens';
import { FieldType, hasOptions, useFieldTags, type CustomField } from './api';

/** A custom field filter as the search API takes it: "key:operator:value". */
type Op = 'in' | 'is' | 'gte' | 'lte' | 'has' | 'empty' | 'any';

const PARAM = 'cf';

const parse = (filter: string) => {
  const [key, op, ...rest] = filter.split(':');
  return { key, op: op as Op, value: rest.join(':') };
};

/** Custom field filters, kept in the address like the other filters, so a filtered list can be shared. */
export function useCustomFilters() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => params.getAll(PARAM), [params]);

  const write = useCallback(
    (next: string[]) =>
      setParams((current) => {
        const copy = new URLSearchParams(current);
        copy.delete(PARAM);
        next.forEach((filter) => copy.append(PARAM, filter));
        return copy;
      }, { replace: true }),
    [setParams],
  );

  /** Sets one operator of one field; null removes it. "Empty" and "any" stand alone. */
  const set = useCallback(
    (key: string, op: Op, value: string | null) => {
      const others = filters.filter((filter) => {
        const item = parse(filter);
        return item.key !== key || (item.op !== op && op !== 'empty' && op !== 'any' && item.op !== 'empty' && item.op !== 'any');
      });
      write(value === null ? others : [...others, op === 'empty' || op === 'any' ? `${key}:${op}` : `${key}:${op}:${value}`]);
    },
    [filters, write],
  );

  const get = useCallback((key: string, op: Op) => filters.map(parse).find((item) => item.key === key && item.op === op), [filters]);

  return {
    filters,
    set,
    get,
    remove: (filter: string) => write(filters.filter((item) => item !== filter)),
    clear: () => write([]),
  };
}

export type CustomFilters = ReturnType<typeof useCustomFilters>;

/** The custom field columns of a space's ticket list, remembered in this browser. */
export function useCustomColumns(projectId: string | undefined) {
  const storageKey = `sb.ticket-columns.${projectId}`;
  const [columns, setColumns] = useState<string[]>(() => {
    try {
      return JSON.parse(localStorage.getItem(storageKey) ?? '[]') as string[];
    } catch {
      return [];
    }
  });
  const toggle = (key: string) => {
    const next = columns.includes(key) ? columns.filter((item) => item !== key) : [...columns, key];
    setColumns(next);
    try {
      localStorage.setItem(storageKey, JSON.stringify(next));
    } catch {
      // Private windows may refuse; the columns still change for now.
    }
  };
  return { columns, toggle };
}

/** The filter of one field, by its kind. */
/** The words a tags field holds in the space, each a checkbox; or any word that contains some text. */
function TagsFilter({ field, projectId, chosen, toggle, filters, presence }: {
  field: CustomField;
  projectId?: string;
  chosen: string[];
  toggle: (word: string) => void;
  filters: CustomFilters;
  presence: ReactNode;
}) {
  const { t } = useTranslation();
  const known = useFieldTags(field.id, projectId);
  const text = filters.get(field.key!, 'has')?.value ?? '';
  return (
    <>
      {(known.data ?? []).map((word) => <Checkbox key={word} label={word} checked={chosen.includes(word)} onChange={() => toggle(word)} />)}
      <BufferedTextField key={`has-${text}`} label={t('customFields.contains')} value={text} onCommit={(value) => filters.set(field.key!, 'has', value.trim() || null)} />
      {presence}
    </>
  );
}

function FieldFilter({ field, people, projectId, filters }: { field: CustomField; people: PickerUser[]; projectId?: string; filters: CustomFilters }) {
  const { t } = useTranslation();
  const key = field.key!;
  const chosen = filters.get(key, 'in')?.value.split(',').filter(Boolean) ?? [];
  const toggleIn = (id: string) => {
    const next = chosen.includes(id) ? chosen.filter((item) => item !== id) : [...chosen, id];
    filters.set(key, 'in', next.length ? next.join(',') : null);
  };
  const presence = (
    <>
      <Checkbox label={t('customFields.filterAny')} checked={Boolean(filters.get(key, 'any'))} onChange={(_, checked) => filters.set(key, 'any', checked ? '' : null)} />
      <Checkbox label={t('customFields.filterEmpty')} checked={Boolean(filters.get(key, 'empty'))} onChange={(_, checked) => filters.set(key, 'empty', checked ? '' : null)} />
    </>
  );

  if (field.type === FieldType.tags) {
    return <TagsFilter field={field} projectId={projectId} chosen={chosen} toggle={toggleIn} filters={filters} presence={presence} />;
  }
  if (hasOptions(field.type)) {
    return (
      <>
        {(field.options ?? []).map((option) => <Checkbox key={option.id} label={option.label ?? ''} checked={chosen.includes(option.id!)} onChange={() => toggleIn(option.id!)} />)}
        {presence}
      </>
    );
  }
  if (field.type === FieldType.user) {
    return (
      <>
        {people.map((user) => <Checkbox key={user.id} label={user.name ?? ''} checked={chosen.includes(user.id!)} onChange={() => toggleIn(user.id!)} />)}
        {presence}
      </>
    );
  }
  if (field.type === FieldType.checkbox) {
    const current = filters.get(key, 'is')?.value;
    return (
      <>
        <Checkbox label={t('customFields.yes')} checked={current === 'true'} onChange={(_, checked) => filters.set(key, 'is', checked ? 'true' : null)} />
        <Checkbox label={t('customFields.no')} checked={current === 'false'} onChange={(_, checked) => filters.set(key, 'is', checked ? 'false' : null)} />
      </>
    );
  }
  if (field.type === FieldType.number || field.type === FieldType.date) {
    const from = filters.get(key, 'gte')?.value ?? '';
    const to = filters.get(key, 'lte')?.value ?? '';
    return (
      <>
        <div className="grid grid-cols-2 gap-2">
          {field.type === FieldType.date ? (
            <>
              <DatePicker label={t('tickets.from')} value={from} rangeEnd={to} onChange={(value) => filters.set(key, 'gte', value || null)} />
              <DatePicker label={t('tickets.to')} value={to} rangeStart={from} onChange={(value) => filters.set(key, 'lte', value || null)} />
            </>
          ) : (
            <>
              <BufferedTextField key={`gte-${from}`} label={t('customFields.atLeast')} inputMode="decimal" value={from} onCommit={(value) => filters.set(key, 'gte', value.trim() && Number.isFinite(Number(value)) ? value.trim() : null)} />
              <BufferedTextField key={`lte-${to}`} label={t('customFields.atMost')} inputMode="decimal" value={to} onCommit={(value) => filters.set(key, 'lte', value.trim() && Number.isFinite(Number(value)) ? value.trim() : null)} />
            </>
          )}
        </div>
        {presence}
      </>
    );
  }
  const text = filters.get(key, 'has')?.value ?? '';
  return (
    <>
      <BufferedTextField key={`has-${text}`} label={t('customFields.contains')} value={text} onCommit={(value) => filters.set(key, 'has', value.trim() || null)} />
      {presence}
    </>
  );
}

/**
 * One button for the custom fields of a space's list: per field, show it as a column and filter by it. Filters apply as
 * they are picked, like the other filters.
 */
export function CustomFieldsButton({ fields, people, projectId, filters, columns, onToggleColumn, onChange }: {
  fields: CustomField[];
  projectId?: string;
  people: PickerUser[];
  filters: CustomFilters;
  columns: string[];
  onToggleColumn: (key: string) => void;
  onChange?: () => void;
}) {
  const { t } = useTranslation();
  const compact = useCompact();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [active, setActive] = useState<string | null>(null);
  if (fields.length === 0) {
    return null;
  }
  const field = fields.find((item) => item.key === active) ?? fields[0];
  const wrapped: CustomFilters = { ...filters, set: (...args) => { filters.set(...args); onChange?.(); } };

  return (
    <>
      <Button kind="secondary" startIcon={<ColumnsIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={(event) => setAnchor(event.currentTarget)}>
        {t('customFields.listButton')}
      </Button>
      <Sheet title={t('customFields.listButton')} width={508} anchor={anchor} onClose={() => setAnchor(null)}>
        <div className={compact ? 'flex flex-col gap-3' : 'flex gap-2'}>
          <div className={compact ? 'flex shrink-0 gap-1 overflow-x-auto pb-1' : 'flex shrink-0 flex-col gap-1'} style={compact ? undefined : { width: 160 }}>
            {fields.map((item) => (
              <ButtonBase
                key={item.id}
                aria-pressed={field.id === item.id}
                className={compact ? 'h-9 shrink-0 px-3' : 'h-8 px-2'}
                onClick={() => setActive(item.key!)}
                sx={{
                  justifyContent: 'flex-start',
                  borderRadius: `${radius.control}px`,
                  fontSize: font.size.md,
                  backgroundColor: field.id === item.id ? sem('bg-selected') : 'transparent',
                  color: sem(field.id === item.id ? 'text-accent' : 'text-primary'),
                }}
              >
                <span className="truncate">{item.name}</span>
              </ButtonBase>
            ))}
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <Checkbox label={t('customFields.showColumn')} checked={columns.includes(field.key!)} onChange={() => onToggleColumn(field.key!)} />
            <div className="pt-2" style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>{t('customFields.filterBy')}</div>
            <FieldFilter key={field.id} field={field} people={people} projectId={projectId} filters={wrapped} />
          </div>
        </div>
      </Sheet>
    </>
  );
}

/** The custom field filters in use, as chips to remove. */
export function CustomFilterChips({ fields, people, filters, onChange }: { fields: CustomField[]; people: PickerUser[]; filters: CustomFilters; onChange?: () => void }) {
  const { t, i18n } = useTranslation();
  if (filters.filters.length === 0) {
    return null;
  }

  const label = (filter: string) => {
    const { key, op, value } = parse(filter);
    const field = fields.find((item) => item.key === key);
    const name = field?.name ?? key;
    const show = (text: string) => (field?.type === FieldType.date ? formatDate(text, i18n.language) : text);
    switch (op) {
      case 'in':
        return t('customFields.chipIs', { name, value: value.split(',').map((id) => field?.options?.find((option) => option.id === id)?.label ?? people.find((user) => user.id === id)?.name ?? id).join(', ') });
      case 'is':
        return t('customFields.chipIs', { name, value: t(value === 'true' ? 'customFields.yes' : 'customFields.no') });
      case 'gte':
        return t('customFields.chipAtLeast', { name, value: show(value) });
      case 'lte':
        return t('customFields.chipAtMost', { name, value: show(value) });
      case 'has':
        return t('customFields.chipHas', { name, value });
      case 'empty':
        return t('customFields.chipIs', { name, value: t('customFields.filterEmpty') });
      default:
        return t('customFields.chipIs', { name, value: t('customFields.filterAny') });
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2">
      {filters.filters.map((filter) => (
        <Chip key={filter} size="small" label={label(filter)} onDelete={() => { filters.remove(filter); onChange?.(); }} />
      ))}
    </div>
  );
}
