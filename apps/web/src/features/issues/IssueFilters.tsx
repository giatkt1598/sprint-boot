import ButtonBase from '@mui/material/ButtonBase';
import Chip from '@mui/material/Chip';
import { ListFilter } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { DatePicker } from '../../components/DatePicker';
import { Popover } from '../../components/Popover';
import { font, radius, sem } from '../../theme/tokens';
import { useMembers } from '../spaces/api';
import { useEpics, useIssueTypes, useLabels, usePriorities, useStatuses } from './api';

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
    return { from: from || undefined, to: to ? `${to}T23:59:59Z` : undefined };
  }
  const days = { today: 0, last7: 7, last30: 30 }[preset];
  const start = new Date();
  start.setDate(start.getDate() - days);
  start.setHours(0, 0, 0, 0);
  return { from: start.toISOString() };
}

const inRange = (value: string | undefined, range: { from?: string; to?: string }) =>
  (!range.from || (value ?? '') >= range.from) && (!range.to || (value ?? '') <= range.to);

export function useIssueFilters(projectId: string | undefined, sprints?: { value: string; label: string }[]) {
  const { t } = useTranslation();
  const [values, setValues] = useState(NO_VALUES);
  const [created, setCreated] = useState(ANY_TIME);
  const [updated, setUpdated] = useState(ANY_TIME);

  const members = useMembers(projectId);
  const types = useIssueTypes(projectId);
  const statuses = useStatuses(projectId);
  const priorities = usePriorities();
  const labels = useLabels(projectId);
  const epics = useEpics(projectId);
  const people = useMemo(() => (members.data ?? []).map((member) => member.user!), [members.data]);

  const options: Record<ValueFilter, { value: string; label: string }[]> = {
    assignee: people.map((user) => ({ value: user.id!, label: user.name! })),
    reporter: people.map((user) => ({ value: user.id!, label: user.name! })),
    type: (types.data ?? []).map((type) => ({ value: type.id!, label: t(`issue.type.${type.icon}`) })),
    status: (statuses.data ?? []).map((status) => ({ value: status.id!, label: status.name! })),
    priority: (priorities.data ?? []).map((priority) => ({ value: priority.id!, label: priority.name! })),
    epic: (epics.data?.items ?? []).map((epic) => ({ value: epic.id!, label: `${epic.key} ${epic.title}` })),
    label: (labels.data ?? []).map((label) => ({ value: label, label })),
    sprint: sprints ?? [],
  };

  const filterLabels: Record<ValueFilter | DateFilter, string> = {
    assignee: t('issues.assignee'),
    reporter: t('issues.reporter'),
    type: t('issues.issueType'),
    status: t('issues.status'),
    priority: t('issues.priority'),
    epic: t('issues.epic'),
    label: t('issues.labels'),
    sprint: t('agile.sprint'),
    created: t('issues.createdAt'),
    updated: t('issues.updatedAt'),
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

  const clearAll = () => {
    setValues(NO_VALUES);
    setCreated(ANY_TIME);
    setUpdated(ANY_TIME);
  };

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

export type IssueFilters = ReturnType<typeof useIssueFilters>;

export function FilterControls({ filters, onChange }: { filters: IssueFilters; onChange?: () => void }) {
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
              aria-label={`${t('issues.assignee')}: ${user.name}`}
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
            aria-label={t('issues.moreAssignees', { count: hidden.length })}
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
      <Button kind="secondary" startIcon={<ListFilter size={16} strokeWidth={1.5} aria-hidden />} onClick={(event) => setAnchor(event.currentTarget)}>
        {chips.length ? t('issues.filterCount', { count: chips.length }) : t('issues.filter')}
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
                    label={t({ any: 'issues.anyTime', today: 'issues.today', last7: 'issues.last7', last30: 'issues.last30', custom: 'issues.customRange' }[preset])}
                    checked={dateRange.preset === preset}
                    onChange={() => {
                      setDateRange({ ...dateRange, preset });
                      onChange?.();
                    }}
                  />
                ))}
                {dateRange.preset === 'custom' && (
                  <div className="grid grid-cols-2 gap-2">
                    <DatePicker label={t('issues.from')} value={dateRange.from} onChange={(from) => setDateRange({ ...dateRange, from })} />
                    <DatePicker label={t('issues.to')} value={dateRange.to} onChange={(to) => setDateRange({ ...dateRange, to })} />
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

export function FilterChips({ filters, onClear }: { filters: IssueFilters; onClear?: () => void }) {
  const { t } = useTranslation();
  if (filters.chips.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      {filters.chips.map((chip) => (
        <Chip key={chip.key} size="small" label={chip.label} onDelete={chip.remove} />
      ))}
      <Button kind="ghost" onClick={() => { filters.clearAll(); onClear?.(); }}>{t('issues.clearAll')}</Button>
    </div>
  );
}
