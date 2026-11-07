import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import type { components } from '@sprint-boot/api-client';
import { Check, ChevronLeft, ChevronRight, ListFilter } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { MenuItem } from '../../components/Menu';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { font, radius, sem, shadow, type SemanticToken } from '../../theme/tokens';
import { IssuePanel } from '../agile/IssuePanel';
import { useBacklog } from '../agile/api';
import { useIssueFilters, type IssueFilters, type ValueFilter } from '../issues/IssueFilters';
import type { Space } from '../spaces/api';

type View = 'sprint' | 'week' | 'month' | 'quarter';
type Range = { from: number; to: number };

const DAY_WIDTH: Record<View, number> = { sprint: 60, week: 120, month: 40, quarter: 14 };
const LABEL_WIDTH = 320;
const HEADER_HEIGHT = 40;
const ROW_HEIGHT = 41;
const BAR_HEIGHT = 24;
const MAX_ISSUES = 500;
const DAY_MS = 86_400_000;

const TYPE_BARS: Record<string, SemanticToken> = {
  story: 'type-story',
  task: 'type-task',
  bug: 'type-bug',
  subtask: 'type-subtask',
  epic: 'type-epic',
};

/** Dates are plain calendar days; arithmetic happens on UTC midnights so time zones never shift a bar. */
const toDay = (iso: string) => Date.parse(`${iso}T00:00:00Z`) / DAY_MS;
const fromDay = (day: number) => new Date(day * DAY_MS);
const todayDay = () => {
  const now = new Date();
  return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / DAY_MS;
};

function rangeOf(view: Exclude<View, 'sprint'>, anchor: number): Range {
  const date = fromDay(anchor);
  if (view === 'week') {
    const monday = anchor - ((date.getUTCDay() + 6) % 7);
    return { from: monday, to: monday + 6 };
  }
  if (view === 'month') {
    return { from: Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1) / DAY_MS, to: Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0) / DAY_MS };
  }
  const quarter = Math.floor(date.getUTCMonth() / 3) * 3;
  return { from: Date.UTC(date.getUTCFullYear(), quarter, 1) / DAY_MS, to: Date.UTC(date.getUTCFullYear(), quarter + 3, 0) / DAY_MS };
}

/** Issues with a start and a due date as bars on a calendar; the sprint's issues missing one are listed after them. */
export function TimelineTab({ space }: { space: Space }) {
  const { t, i18n } = useTranslation();
  const backlog = useBacklog(space.id);
  const filters = useIssueFilters(space.id);
  const [openIssue, setOpenIssue] = useState<string | null>(null);
  const active = backlog.data?.sprints?.map((item) => item.sprint!).find((sprint) => sprint.state === 1 && sprint.startDate && sprint.endDate);
  const [chosenView, setView] = useState<View | null>(null);
  const view: View = chosenView ?? (active ? 'sprint' : 'week');
  const [shift, setShift] = useState(0);

  const issues = useQuery({
    queryKey: ['issues', 'timeline', space.id],
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/search', { params: { query: { ProjectId: space.id, MaxResultCount: MAX_ISSUES } } })),
  });

  const sprintFrom = active ? toDay(active.startDate!) : 0;
  const sprintTo = active ? toDay(active.endDate!) : 0;
  const range = useMemo<Range>(() => {
    if (view === 'sprint') {
      const length = sprintTo - sprintFrom + 1;
      return { from: sprintFrom + shift * length, to: sprintTo + shift * length };
    }
    // Other views step by their own length, starting from the one holding today.
    let current = rangeOf(view, todayDay());
    for (let step = 0; step < Math.abs(shift); step++) {
      current = rangeOf(view, shift > 0 ? current.to + 1 : current.from - 1);
    }
    return current;
  }, [view, shift, sprintFrom, sprintTo]);

  const dayWidth = DAY_WIDTH[view];
  const days = range.to - range.from + 1;
  const today = todayDay();

  const rows = useMemo(() => {
    // Subtasks stay off the timeline: their parent's bar already covers the work.
    const visible = (issues.data?.items ?? []).filter((issue) => issue.type?.hierarchy !== 0 && filters.matches({ ...issue, epicId: issue.parentId }));
    const dated = visible
      .filter((issue) => issue.startDate && issue.dueDate && toDay(issue.startDate) <= range.to && toDay(issue.dueDate) >= range.from)
      .sort((a, b) => toDay(a.startDate!) - toDay(b.startDate!) || toDay(a.dueDate!) - toDay(b.dueDate!));
    // Missing a date matters for the work planned now, so the list keeps to the running sprint.
    const undated = active ? visible.filter((issue) => issue.sprintName === active.name && (!issue.startDate || !issue.dueDate)) : [];
    return { dated, undated };
  }, [issues.data, filters, range.from, range.to, active]);

  const short = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(i18n.language, { ...options, timeZone: 'UTC' });
  const dayLabel = (day: number) => {
    const date = fromDay(day);
    if (view === 'quarter') {
      return date.getUTCDay() === 1 ? short({ month: 'short', day: 'numeric' }).format(date) : '';
    }
    if (view === 'month') {
      return short({ day: 'numeric' }).format(date);
    }
    return `${short({ weekday: 'short' }).format(date)} ${date.getUTCDate()}`;
  };
  const span = `${short({ month: 'short', day: 'numeric' }).format(fromDay(range.from))} – ${short({ month: 'short', day: 'numeric', year: 'numeric' }).format(fromDay(range.to))}`;
  const heading = view === 'sprint' && active && shift === 0 ? `${active.name} · ${span}` : span;

  const views: { value: View; label: string }[] = [
    ...(active ? [{ value: 'sprint' as const, label: active.name ?? t('overview.timeline.sprint') }] : []),
    { value: 'week', label: t('overview.timeline.week') },
    { value: 'month', label: t('overview.timeline.month') },
    { value: 'quarter', label: t('overview.timeline.quarter') },
  ];
  const isWeekend = (day: number) => [0, 6].includes(fromDay(day).getUTCDay()) && view !== 'quarter';
  const gridWidth = days * dayWidth;

  const issueLabel = (issue: Issue, faded: boolean) => (
    <ButtonBase
      onClick={() => setOpenIssue(issue.key!)}
      className="sticky left-0 z-10 flex h-full shrink-0 items-center gap-2 px-4"
      sx={{ width: LABEL_WIDTH, boxSizing: 'border-box', justifyContent: 'flex-start', backgroundColor: sem('bg-surface'), opacity: faded ? 0.5 : 1, fontSize: font.size.md, textAlign: 'left' }}
    >
      {/* Its own box, so a long title never squeezes the icon away. */}
      <span className="flex size-4 shrink-0 items-center justify-center">
        {issue.type?.icon && issue.type.icon in TYPE_BARS ? <IssueTypeIcon type={issue.type.icon as IconType} /> : null}
      </span>
      <span className="shrink-0" style={{ color: sem('text-secondary') }}>{issue.key}</span>
      <span className="min-w-0 truncate" style={{ color: sem('text-primary') }}>{issue.title}</span>
    </ButtonBase>
  );

  const bar = (issue: Issue) => {
    const start = toDay(issue.startDate!);
    const end = toDay(issue.dueDate!);
    const from = Math.max(start, range.from);
    const to = Math.min(end, range.to);
    // A bar that runs past the view is cut square at that edge.
    const left = start < range.from ? 0 : radius.control;
    const right = end > range.to ? 0 : radius.control;
    return (
      <ButtonBase
        onClick={() => setOpenIssue(issue.key!)}
        aria-label={`${issue.key} ${issue.title}`}
        className="absolute flex items-center overflow-hidden px-2"
        sx={{
          justifyContent: 'flex-start',
          color: sem('text-on-accent'),
          fontSize: font.size.xs,
          fontWeight: font.weight.medium,
          left: (from - range.from) * dayWidth + (start < range.from ? 0 : 4),
          width: Math.max((to - from + 1) * dayWidth - (start < range.from ? 0 : 4) - (end > range.to ? 0 : 4), 6),
          top: (ROW_HEIGHT - BAR_HEIGHT) / 2,
          height: BAR_HEIGHT,
          backgroundColor: sem(TYPE_BARS[issue.type?.icon ?? ''] ?? 'accent-default'),
          borderRadius: `${left}px ${right}px ${right}px ${left}px`,
        }}
      >
        <span className="min-w-0 truncate">{issue.title}</span>
      </ButtonBase>
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <Button kind="secondary" onClick={() => setShift(0)}>{t('overview.timeline.today')}</Button>
        <IconButton size="small" aria-label={t('overview.timeline.previous')} onClick={() => setShift(shift - 1)}><ChevronLeft size={16} strokeWidth={1.5} aria-hidden /></IconButton>
        <IconButton size="small" aria-label={t('overview.timeline.next')} onClick={() => setShift(shift + 1)}><ChevronRight size={16} strokeWidth={1.5} aria-hidden /></IconButton>
        <Typography variant="h3" component="h2" className="flex-1">{heading}</Typography>
        <FilterButton filters={filters} filter="type" label={t('overview.timeline.type')} />
        <FilterButton filters={filters} filter="assignee" label={t('overview.timeline.assignee')} />
        <FilterButton filters={filters} filter="epic" label={t('overview.timeline.epic')} />
        <div role="radiogroup" aria-label={t('overview.timeline.views')} className="flex p-1" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.control }}>
          {views.map((item) => (
            <ButtonBase
              key={item.value}
              role="radio"
              aria-checked={view === item.value}
              onClick={() => { setView(item.value); setShift(0); }}
              className="h-7 px-3"
              sx={{
                fontSize: font.size.md,
                borderRadius: `${radius.control - 2}px`,
                color: sem(view === item.value ? 'text-primary' : 'text-secondary'),
                fontWeight: view === item.value ? font.weight.medium : font.weight.regular,
                backgroundColor: view === item.value ? sem('bg-surface') : 'transparent',
                boxShadow: view === item.value ? shadow.card : 'none',
              }}
            >
              {item.label}
            </ButtonBase>
          ))}
        </div>
      </div>

      {issues.isPending && <LoadingState />}
      {issues.isError && <ErrorState onRetry={() => void issues.refetch()} />}
      {issues.data && (
        <div className="overflow-x-auto" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
          <div style={{ width: LABEL_WIDTH + gridWidth, minWidth: '100%' }}>
            <div className="relative flex" style={{ height: HEADER_HEIGHT, backgroundColor: sem('bg-page'), borderBottom: `1px solid ${sem('border-default')}` }}>
              <span className="sticky left-0 z-10 flex shrink-0 items-center px-4" style={{ width: LABEL_WIDTH, backgroundColor: sem('bg-page'), fontSize: font.size.xs, color: sem('text-secondary') }}>
                {t('overview.timeline.issue')}
              </span>
              {Array.from({ length: days }, (_, index) => (
                <span
                  key={index}
                  className="flex shrink-0 items-center overflow-hidden whitespace-nowrap px-2"
                  style={{ width: dayWidth, fontSize: font.size.xs, color: sem('text-secondary'), backgroundColor: isWeekend(range.from + index) ? sem('bg-subtle') : undefined }}
                >
                  {dayLabel(range.from + index)}
                </span>
              ))}
            </div>

            <div className="relative">
              <DayGrid days={days} dayWidth={dayWidth} from={range.from} today={today} weekend={isWeekend} />
              {rows.dated.length === 0 && rows.undated.length === 0 && (
                <div className="sticky left-0 p-4" style={{ width: LABEL_WIDTH + 320 }}><EmptyState title={t('overview.timeline.empty')} /></div>
              )}
              {rows.dated.map((issue) => (
                <div key={issue.id} className="relative flex" style={{ height: ROW_HEIGHT, borderBottom: `1px solid ${sem('border-default')}` }}>
                  {issueLabel(issue, false)}
                  <div className="relative shrink-0" style={{ width: gridWidth }}>{bar(issue)}</div>
                </div>
              ))}
            </div>

            {rows.undated.length > 0 && (
              <>
                <div className="sticky left-0 flex items-center gap-2 px-4" style={{ height: 33, backgroundColor: sem('bg-page'), borderBottom: `1px solid ${sem('border-default')}`, fontSize: font.size.xs, color: sem('text-secondary') }}>
                  {t('overview.timeline.noDatesGroup')}
                  <span>{rows.undated.length}</span>
                </div>
                {rows.undated.map((issue, index) => (
                  <div key={issue.id} className="flex" style={{ height: ROW_HEIGHT, borderBottom: index < rows.undated.length - 1 ? `1px solid ${sem('border-default')}` : undefined }}>
                    {issueLabel(issue, true)}
                  </div>
                ))}
              </>
            )}
          </div>
        </div>
      )}
      <IssuePanel issueKey={openIssue} onClose={() => setOpenIssue(null)} />
    </div>
  );
}

type Issue = components['schemas']['IssueSummaryDto'];

/** Day lines, weekend shading and the today line, drawn behind the dated rows. */
function DayGrid({ days, dayWidth, from, today, weekend }: { days: number; dayWidth: number; from: number; today: number; weekend: (day: number) => boolean }) {
  return (
    <div aria-hidden className="pointer-events-none absolute inset-y-0" style={{ left: LABEL_WIDTH, width: days * dayWidth }}>
      {Array.from({ length: days }, (_, index) => (
        <span
          key={index}
          className="absolute inset-y-0"
          style={{ left: index * dayWidth, width: dayWidth, borderLeft: `1px solid ${sem('border-default')}`, backgroundColor: weekend(from + index) ? sem('bg-page') : undefined }}
        />
      ))}
      {today >= from && today < from + days && (
        <span className="absolute inset-y-0" style={{ left: (today - from + 0.5) * dayWidth - 1, width: 2, backgroundColor: sem('accent-default') }}>
          <span className="absolute -left-0.75 -top-1 size-2" style={{ backgroundColor: sem('accent-default'), borderRadius: radius.full }} />
        </span>
      )}
    </div>
  );
}

/** One filter as its own button, as the timeline toolbar shows them. */
function FilterButton({ filters, filter, label }: { filters: IssueFilters; filter: ValueFilter; label: string }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const chosen = filters.values[filter];

  return (
    <>
      <Button kind="secondary" aria-haspopup="menu" startIcon={<ListFilter size={16} strokeWidth={1.5} aria-hidden />} onClick={(event) => setAnchor(event.currentTarget)}>
        {chosen.length > 0 ? `${label} · ${chosen.length}` : label}
      </Button>
      <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)} slotProps={{ paper: { sx: { minWidth: 220, maxHeight: 360 } } }}>
        {filters.options[filter].map((option) => (
          <MenuItem key={option.value} onClick={() => filters.toggle(filter, option.value)}>
            <span className="flex w-4 shrink-0" aria-hidden>{chosen.includes(option.value) && <Check size={16} strokeWidth={1.5} />}</span>
            <span className="truncate">{option.label}</span>
          </MenuItem>
        ))}
        {chosen.length > 0 && (
          <MenuItem onClick={() => { chosen.forEach((value) => filters.toggle(filter, value)); setAnchor(null); }}>
            <span className="w-4 shrink-0" />
            {t('overview.timeline.clear')}
          </MenuItem>
        )}
      </Menu>
    </>
  );
}
