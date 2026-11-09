import ButtonBase from '@mui/material/ButtonBase';
import Chip from '@mui/material/Chip';
import { FilterIcon } from '../../components/icons';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { DatePicker } from '../../components/DatePicker';
import { Popover } from '../../components/Popover';
import { font, radius, sem } from '../../theme/tokens';
import { useMembers } from '../spaces/api';
import { useEpics, useTicketTypes, useLabels, usePriorities, useStatuses } from './api';

const MAX_AVATARS = 5;

export type ValueFilter = 'assignee' | 'reporter' | 'type' | 'status' | 'priority' | 'epic' | 'label' | 'sprint';
type DateFilter = 'created' | 'updated';
type DatePreset = 'any' | 'today' | 'last7' | 'last30' | 'custom';

interface DateRange {
  preset: DatePreset;
  from: string;
  to: string;
}

export interface FilterableCard {
  assignee?: { id?: string } | null;
  reporter?: { id?: string } | null;
  type?: { id?: string } | null;
  status?: { id?: string } | null;
  priority?: { id?: string } | null;
  epicId?: string | null;
  labels?: string[] | null;
  createdAt?: string;
  updatedAt?: string;
}

const ANY_TIME: DateRange = { preset: 'any', from: '', to: '' };
const NO_VALUES: Record<ValueFilter, string[]> = { assignee: [], reporter: [], type: [], status: [], priority: [], epic: [], label: [], sprint: [] };

function rangeToQuery({ preset, from, to }: DateRange): { from?: string; to?: string } {
  if (preset === 'any') {
    return {};
  }
  if (preset === 'custom') {
    // The picked days are the viewer's own days, from the start of the first to the end of the last.
    const end = to ? new Date(`${to}T00:00:00`) : undefined;
    end?.setDate(end.getDate() + 1);
    return {
      from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
      to: end ? new Date(end.getTime() - 1).toISOString() : undefined,
    };
  }
  const days = { today: 0, last7: 7, last30: 30 }[preset];
  const start = new Date();
  start.setDate(start.getDate() - days);
  start.setHours(0, 0, 0, 0);
  return { from: start.toISOString() };
}

const VALUE_FILTERS = Object.keys(NO_VALUES) as ValueFilter[];
const PRESETS: DatePreset[] = ['today', 'last7', 'last30'];

/** A date filter in the address: a preset name, or "from~to" for a custom range, "~" while no day is picked yet. */
export function readRange(raw: string | null): DateRange {
  if (!raw) {
    return ANY_TIME;
  }
  if (PRESETS.includes(raw as DatePreset)) {
    return { preset: raw as DatePreset, from: '', to: '' };
  }
  const [from = '', to = ''] = raw.split('~');
  const day = /^\d{4}-\d{2}-\d{2}$/;
  // A bare "~" is a custom range still being picked: reading it as any time would hide the date pickers.
  return !raw.includes('~') || (from && !day.test(from)) || (to && !day.test(to)) ? ANY_TIME : { preset: 'custom', from, to };
}

export function writeRange(range: DateRange): string | null {
  if (range.preset === 'any') {
    return null;
  }
  return range.preset === 'custom' ? `${range.from}~${range.to}` : range.preset;
}

/**
 * The search box text of a list, kept in the address with the filters, so it survives opening a ticket and coming
 * back, a reload, or a copied link.
 */
export function useSearchText(): [string, (text: string) => void] {
  const [params, setParams] = useSearchParams();
  const inAddress = params.get('text') ?? '';
  // The box shows its own copy, updated as each key lands. Bound to the address it lagged a render behind, and
  // Vietnamese input methods, which rewrite the last letters as you type ("bo" + "o" -> "bô"), then doubled them.
  const [draft, setDraft] = useState(inAddress);
  const written = useRef(inAddress);
  useEffect(() => {
    // Only a change made elsewhere (Back, a link, clearing the filters) replaces what is being typed.
    if (inAddress !== written.current) {
      written.current = inAddress;
      setDraft(inAddress);
    }
  }, [inAddress]);
  const setText = useCallback(
    (text: string) => {
      setDraft(text);
      written.current = text;
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          if (text) next.set('text', text);
          else next.delete('text');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );
  return [draft, setText];
}

/** Compared as instants: the API's timestamps and the range's ISO strings need not be written alike. */
const inRange = (value: string | undefined, range: { from?: string; to?: string }) => {
  const at = value ? Date.parse(value) : Number.NaN;
  return (!range.from || at >= Date.parse(range.from)) && (!range.to || at <= Date.parse(range.to));
};

/**
 * The filters of a ticket list. They live in the address (assignee=…&type=…&created=last7), so they survive opening an
 * ticket and coming back, a reload, or a copied link, and other parameters of the page are left alone. Changes replace
 * the history entry instead of adding one, so Back still leaves the page.
 */
export function useTicketFilters(projectId: string | undefined, sprints?: { value: string; label: string }[]) {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const raw = useMemo(
    () => Object.fromEntries(VALUE_FILTERS.map((filter) => [filter, params.getAll(filter)])) as Record<ValueFilter, string[]>,
    [params],
  );
  const created = useMemo(() => readRange(params.get('created')), [params]);
  const updated = useMemo(() => readRange(params.get('updated')), [params]);

  const write = useCallback(
    (change: (next: URLSearchParams) => void) =>
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          change(next);
          return next;
        },
        { replace: true },
      ),
    [setParams],
  );
  const setValues = (update: (current: Record<ValueFilter, string[]>) => Record<ValueFilter, string[]>) =>
    write((next) => {
      const values = update(raw);
      for (const filter of VALUE_FILTERS) {
        next.delete(filter);
        values[filter].forEach((value) => next.append(filter, value));
      }
    });
  const setRange = (key: DateFilter) => (range: DateRange) =>
    write((next) => {
      const value = writeRange(range);
      if (value) next.set(key, value);
      else next.delete(key);
    });
  const setCreated = setRange('created');
  const setUpdated = setRange('updated');

  const members = useMembers(projectId);
  const types = useTicketTypes(projectId);
  const statuses = useStatuses(projectId);
  const priorities = usePriorities();
  const labels = useLabels(projectId);
  const epics = useEpics(projectId);
  const people = useMemo(() => (members.data ?? []).map((member) => member.user!), [members.data]);

  const options: Record<ValueFilter, { value: string; label: string }[]> = {
    assignee: people.map((user) => ({ value: user.id!, label: user.name! })),
    reporter: people.map((user) => ({ value: user.id!, label: user.name! })),
    type: (types.data ?? []).map((type) => ({ value: type.id!, label: t(`ticket.type.${type.icon}`) })),
    status: (statuses.data ?? []).map((status) => ({ value: status.id!, label: status.name! })),
    priority: (priorities.data ?? []).map((priority) => ({ value: priority.id!, label: priority.name! })),
    epic: (epics.data?.items ?? []).map((epic) => ({ value: epic.id!, label: `${epic.key} ${epic.title}` })),
    label: (labels.data ?? []).map((label) => ({ value: label, label })),
    sprint: sprints ?? [],
  };

  // A value the address carries but the space no longer has, such as a deleted label or a member who left, is ignored
  // once the options have loaded, so an old link never empties the list.
  const values = Object.fromEntries(
    VALUE_FILTERS.map((filter) => [filter, options[filter].length === 0 ? raw[filter] : raw[filter].filter((value) => options[filter].some((option) => option.value === value))]),
  ) as Record<ValueFilter, string[]>;

  const filterLabels: Record<ValueFilter | DateFilter, string> = {
    assignee: t('tickets.assignee'),
    reporter: t('tickets.reporter'),
    type: t('tickets.ticketType'),
    status: t('tickets.status'),
    priority: t('tickets.priority'),
    epic: t('tickets.epic'),
    label: t('tickets.labels'),
    sprint: t('agile.sprint'),
    created: t('tickets.createdAt'),
    updated: t('tickets.updatedAt'),
  };

  const toggle = (filter: ValueFilter, value: string) =>
    setValues((current) => ({
      ...current,
      [filter]: current[filter].includes(value) ? current[filter].filter((item) => item !== value) : [...current[filter], value],
    }));

  const chips = [
    ...(Object.keys(values) as ValueFilter[]).flatMap((filter) =>
      values[filter].map((value) => ({
        key: `${filter}:${value}`,
        label: `${filterLabels[filter]}: ${options[filter].find((option) => option.value === value)?.label ?? value}`,
        remove: () => toggle(filter, value),
      })),
    ),
    ...(created.preset !== 'any' ? [{ key: 'created', label: filterLabels.created, remove: () => setCreated(ANY_TIME) }] : []),
    ...(updated.preset !== 'any' ? [{ key: 'updated', label: filterLabels.updated, remove: () => setUpdated(ANY_TIME) }] : []),
  ];

  const clearAll = () =>
    write((next) => {
      [...VALUE_FILTERS, 'created', 'updated'].forEach((key) => next.delete(key));
    });

  const createdRange = rangeToQuery(created);
  const updatedRange = rangeToQuery(updated);

  /** Client-side equivalent of the search API, for lists already in memory. */
  const matches = (card: FilterableCard) => {
    const hit = (filter: ValueFilter, value?: string | null) => values[filter].length === 0 || values[filter].includes(value ?? '');
    return (
      hit('assignee', card.assignee?.id) &&
      hit('reporter', card.reporter?.id) &&
      hit('type', card.type?.id) &&
      hit('status', card.status?.id) &&
      hit('priority', card.priority?.id) &&
      hit('epic', card.epicId) &&
      (values.label.length === 0 || values.label.some((label) => card.labels?.includes(label))) &&
      inRange(card.createdAt, createdRange) &&
      inRange(card.updatedAt, updatedRange)
    );
  };

  return {
    values,
    created,
    setCreated,
    updated,
    setUpdated,
    toggle,
    chips,
    clearAll,
    matches,
    options,
    filterLabels,
    people,
    createdRange,
    updatedRange,
    hasSprintFilter: Boolean(sprints),
  };
}

export type TicketFilters = ReturnType<typeof useTicketFilters>;

export function FilterControls({ filters, onChange }: { filters: TicketFilters; onChange?: () => void }) {
  const { t } = useTranslation();
  const { values, people, options, filterLabels, chips } = filters;
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [moreAnchor, setMoreAnchor] = useState<HTMLElement | null>(null);
  const [active, setActive] = useState<ValueFilter | DateFilter>('assignee');

  const toggle = (filter: ValueFilter, value: string) => {
    filters.toggle(filter, value);
    onChange?.();
  };

  const visible = people.slice(0, MAX_AVATARS);
  const hidden = people.slice(MAX_AVATARS);
  const hiddenSelected = hidden.some((user) => values.assignee.includes(user.id!));
  const dateRange = active === 'created' ? filters.created : filters.updated;
  const setDateRange = active === 'created' ? filters.setCreated : filters.setUpdated;
  const tabs = (Object.keys(filterLabels) as (ValueFilter | DateFilter)[]).filter((filter) => filter !== 'sprint' || filters.hasSprintFilter);

  return (
    <>
      <div className="flex items-center gap-1">
        {visible.map((user) => {
          const selected = values.assignee.includes(user.id!);
          return (
            <ButtonBase
              key={user.id}
              aria-label={`${t('tickets.assignee')}: ${user.name}`}
              aria-pressed={selected}
              onClick={() => toggle('assignee', user.id!)}
              sx={{ borderRadius: '50%', outline: selected ? `2px solid ${sem('border-focus')}` : 'none' }}
            >
              <Avatar name={user.name ?? ''} src={user.avatarUrl} />
            </ButtonBase>
          );
        })}
        {hidden.length > 0 && (
          <ButtonBase
            aria-label={t('tickets.moreAssignees', { count: hidden.length })}
            onClick={(event) => setMoreAnchor(event.currentTarget)}
            sx={{
              width: 24,
              height: 24,
              borderRadius: '50%',
              fontSize: font.size.xs,
              backgroundColor: sem('bg-subtle'),
              outline: hiddenSelected ? `2px solid ${sem('border-focus')}` : 'none',
            }}
          >
            +{hidden.length}
          </ButtonBase>
        )}
        <Popover width={200} open={Boolean(moreAnchor)} anchorEl={moreAnchor} onClose={() => setMoreAnchor(null)}>
          {hidden.map((user) => (
            <Checkbox key={user.id} label={user.name ?? ''} checked={values.assignee.includes(user.id!)} onChange={() => toggle('assignee', user.id!)} />
          ))}
        </Popover>
      </div>
      <Button kind="secondary" startIcon={<FilterIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={(event) => setAnchor(event.currentTarget)}>
        {chips.length ? t('tickets.filterCount', { count: chips.length }) : t('tickets.filter')}
      </Button>
      <Popover width={508} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <div className="flex gap-2">
          <div className="flex shrink-0 flex-col gap-1" style={{ width: 160 }}>
            {tabs.map((filter) => (
              <ButtonBase
                key={filter}
                className="h-8 px-2"
                onClick={() => setActive(filter)}
                sx={{
                  justifyContent: 'flex-start',
                  borderRadius: `${radius.control}px`,
                  fontSize: font.size.md,
                  backgroundColor: active === filter ? sem('bg-selected') : 'transparent',
                  color: sem(active === filter ? 'text-accent' : 'text-primary'),
                }}
              >
                {filterLabels[filter]}
              </ButtonBase>
            ))}
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            {active === 'created' || active === 'updated' ? (
              <>
                {(['any', 'today', 'last7', 'last30', 'custom'] as const).map((preset) => (
                  <Checkbox
                    key={preset}
                    label={t({ any: 'tickets.anyTime', today: 'tickets.today', last7: 'tickets.last7', last30: 'tickets.last30', custom: 'tickets.customRange' }[preset])}
                    checked={dateRange.preset === preset}
                    onChange={() => {
                      setDateRange({ ...dateRange, preset });
                      onChange?.();
                    }}
                  />
                ))}
                {dateRange.preset === 'custom' && (
                  <div className="grid grid-cols-2 gap-2">
                    <DatePicker label={t('tickets.from')} value={dateRange.from} onChange={(from) => setDateRange({ ...dateRange, from })} />
                    <DatePicker label={t('tickets.to')} value={dateRange.to} onChange={(to) => setDateRange({ ...dateRange, to })} />
                  </div>
                )}
              </>
            ) : (
              options[active].map((option) => (
                <Checkbox key={option.value} label={option.label} checked={values[active].includes(option.value)} onChange={() => toggle(active, option.value)} />
              ))
            )}
          </div>
        </div>
      </Popover>
    </>
  );
}

export function FilterChips({ filters, onClear }: { filters: TicketFilters; onClear?: () => void }) {
  const { t } = useTranslation();
  if (filters.chips.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      {filters.chips.map((chip) => (
        <Chip key={chip.key} size="small" label={chip.label} onDelete={chip.remove} />
      ))}
      <Button kind="ghost" onClick={() => { filters.clearAll(); onClear?.(); }}>{t('tickets.clearAll')}</Button>
    </div>
  );
}
