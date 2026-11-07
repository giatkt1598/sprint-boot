import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { SelectField } from '../../components/TextField';
import { font, radius, sem, type SemanticToken } from '../../theme/tokens';
import { IssuePanel } from '../agile/IssuePanel';
import { useBacklog } from '../agile/api';
import { FilterChips, FilterControls, useIssueFilters } from '../issues/IssueFilters';
import type { Space } from '../spaces/api';

type View = 'sprint' | 'week' | 'month' | 'quarter';

const DAY_WIDTH: Record<View, number> = { sprint: 60, week: 120, month: 36, quarter: 12 };
const LABEL_WIDTH = 280;
const ROW_HEIGHT = 40;
const MAX_ISSUES = 500;
const MAX_UNDATED = 50;
const CATEGORY_BARS: SemanticToken[] = ['border-strong', 'accent-default', 'status-done-text'];

/** Dates are plain calendar days; arithmetic happens on UTC midnights so time zones never shift a bar. */
const toDay = (iso: string) => Date.parse(`${iso}T00:00:00Z`) / 86_400_000;
const fromDay = (day: number) => new Date(day * 86_400_000);
const todayDay = () => {
  const now = new Date();
  return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / 86_400_000;
};

function rangeOf(view: View, anchor: number, sprint?: { from: number; to: number }): { from: number; to: number } {
  const date = fromDay(anchor);
  switch (view) {
    case 'sprint':
      return sprint ?? rangeOf('week', anchor);
    case 'week': {
      const monday = anchor - ((date.getUTCDay() + 6) % 7);
      return { from: monday, to: monday + 6 };
    }
    case 'month': {
      const first = Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1) / 86_400_000;
      return { from: first, to: Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0) / 86_400_000 };
    }
    default: {
      const quarter = Math.floor(date.getUTCMonth() / 3) * 3;
      return { from: Date.UTC(date.getUTCFullYear(), quarter, 1) / 86_400_000, to: Date.UTC(date.getUTCFullYear(), quarter + 3, 0) / 86_400_000 };
    }
  }
}

/** Issues with dates as bars on a calendar. Those missing a date show faded at the end. */
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
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/search', { params: { query: { ProjectId: space.id, MaxResultCount: MAX_ISSUES, Sorting: 'created' } } })),
  });

  const sprintRange = active ? { from: toDay(active.startDate!), to: toDay(active.endDate!) } : undefined;
  const sprintLength = sprintRange ? sprintRange.to - sprintRange.from + 1 : 7;
  const range = useMemo(() => {
    if (view === 'sprint' && sprintRange) {
      return { from: sprintRange.from + shift * sprintLength, to: sprintRange.to + shift * sprintLength };
    }
    // Other views step by their own length: from today, a week, month or quarter at a time.
    let anchor = todayDay();
    let range = rangeOf(view, anchor);
    for (let step = 0; step < Math.abs(shift); step++) {
      anchor = shift > 0 ? range.to + 1 : range.from - 1;
      range = rangeOf(view, anchor);
    }
    return range;
  }, [view, shift, sprintRange?.from, sprintRange?.to, sprintLength]);

  const dayWidth = DAY_WIDTH[view];
  const days = range.to - range.from + 1;
  const today = todayDay();

  const rows = useMemo(() => {
    const visible = (issues.data?.items ?? []).filter((issue) => filters.matches({ ...issue, epicId: issue.parentId }));
    const dated = visible
      .filter((issue) => issue.startDate && issue.dueDate)
      .filter((issue) => toDay(issue.startDate!) <= range.to && toDay(issue.dueDate!) >= range.from)
      .sort((a, b) => toDay(a.startDate!) - toDay(b.startDate!) || toDay(a.dueDate!) - toDay(b.dueDate!));
    const partial = visible
      .filter((issue) => Boolean(issue.startDate) !== Boolean(issue.dueDate))
      .filter((issue) => {
        const day = toDay((issue.startDate ?? issue.dueDate)!);
        return day >= range.from && day <= range.to;
      });
    const undated = visible.filter((issue) => !issue.startDate && !issue.dueDate);
    return { dated, partial, undated: undated.slice(0, MAX_UNDATED), hiddenUndated: Math.max(0, undated.length - MAX_UNDATED) };
  }, [issues.data, filters, range.from, range.to]);

  const dayLabel = (day: number) => {
    const date = fromDay(day);
    if (view === 'quarter') {
      return date.getUTCDate() === 1 || day === range.from
        ? new Intl.DateTimeFormat(i18n.language, { month: 'short', timeZone: 'UTC' }).format(date)
        : '';
    }
    return new Intl.DateTimeFormat(i18n.language, view === 'month' ? { day: 'numeric', timeZone: 'UTC' } : { weekday: 'short', day: 'numeric', timeZone: 'UTC' }).format(date);
  };
  const title = new Intl.DateTimeFormat(i18n.language, { month: 'short', day: 'numeric', timeZone: 'UTC' });
  const heading = view === 'sprint' && active && shift === 0
    ? active.name
    : `${title.format(fromDay(range.from))} – ${new Intl.DateTimeFormat(i18n.language, { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' }).format(fromDay(range.to))}`;

  const viewOptions = [
    ...(active ? [{ value: 'sprint', label: active.name ?? t('overview.timeline.sprint') }] : []),
    { value: 'week', label: t('overview.timeline.week') },
    { value: 'month', label: t('overview.timeline.month') },
    { value: 'quarter', label: t('overview.timeline.quarter') },
  ];

  const bar = (issue: (typeof rows.dated)[number], faded: boolean) => {
    const start = toDay((issue.startDate ?? issue.dueDate)!);
    const end = toDay((issue.dueDate ?? issue.startDate)!);
    const from = Math.max(start, range.from);
    const to = Math.min(end, range.to);
    const clippedStart = start < range.from;
    const clippedEnd = end > range.to;
    const corner = `${radius.control}px`;
    return (
      <ButtonBase
        onClick={() => setOpenIssue(issue.key!)}
        aria-label={`${issue.key} ${issue.title}`}
        className="absolute flex items-center overflow-hidden px-2"
        sx={{
          left: (from - range.from) * dayWidth + 2,
          width: Math.max((to - from + 1) * dayWidth - 4, 8),
          top: 8,
          height: ROW_HEIGHT - 16,
          justifyContent: 'flex-start',
          backgroundColor: sem(CATEGORY_BARS[issue.status?.category ?? 0]),
          color: sem(issue.status?.category === 0 ? 'text-primary' : 'text-on-accent'),
          opacity: faded ? 0.5 : 1,
          // A bar that runs past the view is cut square at that edge.
          borderRadius: `${clippedStart ? 0 : corner} ${clippedEnd ? 0 : corner} ${clippedEnd ? 0 : corner} ${clippedStart ? 0 : corner}`,
          fontSize: font.size.xs,
          fontWeight: font.weight.medium,
        }}
      >
        <span className="truncate">{issue.title}</span>
      </ButtonBase>
    );
  };

  const label = (issue: (typeof rows.dated)[number], faded: boolean) => (
    <ButtonBase
      onClick={() => setOpenIssue(issue.key!)}
      className="sticky left-0 z-10 flex h-full shrink-0 items-center gap-2 px-3"
      sx={{ width: LABEL_WIDTH, justifyContent: 'flex-start', backgroundColor: sem('bg-surface'), borderRight: `1px solid ${sem('border-default')}`, opacity: faded ? 0.5 : 1, fontSize: font.size.md, textAlign: 'left' }}
    >
      <IssueTypeIcon type={issue.type?.icon as IconType} />
      <span className="shrink-0" style={{ fontSize: font.size.xs, color: sem('text-accent') }}>{issue.key}</span>
      <span className="min-w-0 truncate">{issue.title}</span>
    </ButtonBase>
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <div style={{ width: 180 }}>
          <SelectField label={t('overview.timeline.view')} quiet value={view} options={viewOptions} onChange={(event) => { setView(event.target.value as View); setShift(0); }} />
        </div>
        <IconButton size="small" aria-label={t('overview.timeline.previous')} onClick={() => setShift(shift - 1)}><ChevronLeft size={16} strokeWidth={1.5} aria-hidden /></IconButton>
        <IconButton size="small" aria-label={t('overview.timeline.next')} onClick={() => setShift(shift + 1)}><ChevronRight size={16} strokeWidth={1.5} aria-hidden /></IconButton>
        <Button kind="secondary" onClick={() => setShift(0)}>{t('overview.timeline.today')}</Button>
        <Typography variant="h3" component="h2" className="flex-1">{heading}</Typography>
        <FilterControls filters={filters} />
      </div>
      <FilterChips filters={filters} />

      {issues.isPending && <LoadingState />}
      {issues.isError && <ErrorState onRetry={() => void issues.refetch()} />}
      {issues.data && (
        <div className="overflow-x-auto" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
          <div className="relative" style={{ width: LABEL_WIDTH + days * dayWidth }}>
            {/* Weekend shading and the today line sit behind the rows. */}
            <div aria-hidden className="pointer-events-none absolute inset-y-0" style={{ left: LABEL_WIDTH, width: days * dayWidth }}>
              {Array.from({ length: days }, (_, index) => {
                const weekday = fromDay(range.from + index).getUTCDay();
                return (weekday === 0 || weekday === 6) && view !== 'quarter' ? (
                  <span key={index} className="absolute inset-y-0" style={{ left: index * dayWidth, width: dayWidth, backgroundColor: sem('bg-page') }} />
                ) : null;
              })}
              {today >= range.from && today <= range.to && (
                <span className="absolute inset-y-0" style={{ left: (today - range.from + 0.5) * dayWidth, width: 2, backgroundColor: sem('feedback-danger-text') }} />
              )}
            </div>

            <div className="relative flex" style={{ height: 36, borderBottom: `1px solid ${sem('border-default')}` }}>
              <span className="sticky left-0 z-10 flex shrink-0 items-center px-3" style={{ width: LABEL_WIDTH, backgroundColor: sem('bg-surface'), borderRight: `1px solid ${sem('border-default')}`, fontSize: font.size.xs, color: sem('text-secondary') }}>
                {t('overview.timeline.issue')}
              </span>
              {Array.from({ length: days }, (_, index) => (
                <span key={index} className="flex shrink-0 items-center justify-center overflow-hidden whitespace-nowrap" style={{ width: dayWidth, fontSize: font.size.xs, color: range.from + index === today ? sem('feedback-danger-text') : sem('text-secondary') }}>
                  {dayLabel(range.from + index)}
                </span>
              ))}
            </div>

            {rows.dated.length + rows.partial.length + rows.undated.length === 0 && (
              <div className="sticky left-0 p-4" style={{ width: LABEL_WIDTH + 320 }}><EmptyState title={t('overview.timeline.empty')} /></div>
            )}
            {[...rows.dated.map((issue) => [issue, false] as const), ...rows.partial.map((issue) => [issue, true] as const)].map(([issue, faded]) => (
              <div key={issue.id} className="relative flex" style={{ height: ROW_HEIGHT, borderBottom: `1px solid ${sem('border-default')}` }}>
                {label(issue, faded)}
                <div className="relative flex-1">{bar(issue, faded)}</div>
              </div>
            ))}
            {rows.undated.map((issue) => (
              <div key={issue.id} className="relative flex" style={{ height: ROW_HEIGHT, borderBottom: `1px solid ${sem('border-default')}` }}>
                {label(issue, true)}
                <span className="flex items-center px-3" style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{t('overview.timeline.noDates')}</span>
              </div>
            ))}
            {rows.hiddenUndated > 0 && (
              <p className="sticky left-0 px-3 py-2" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{t('overview.timeline.moreUndated', { count: rows.hiddenUndated })}</p>
            )}
          </div>
        </div>
      )}
      <IssuePanel issueKey={openIssue} onClose={() => setOpenIssue(null)} />
    </div>
  );
}
