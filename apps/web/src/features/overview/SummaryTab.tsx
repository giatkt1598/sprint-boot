import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { Calendar, Check, ChevronDown, GitPullRequest, Inbox, ListTodo, Users, type LucideIcon } from 'lucide-react';
import { Fragment, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { MenuItem } from '../../components/Menu';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { formatMinutes, formatRelative } from '../../lib/format';
import { font, radius, sem, type SemanticToken } from '../../theme/tokens';
import { useIssueTypes, useStatuses } from '../issues/api';
import type { Space } from '../spaces/api';
import { useOverviewSummary, type OverviewPeriod, type OverviewSummary } from './api';

const TYPE_COLORS: Record<string, SemanticToken> = {
  story: 'type-story',
  task: 'type-task',
  bug: 'type-bug',
  subtask: 'type-subtask',
  epic: 'type-epic',
};

/** The order the type bars keep, whatever their counts. */
const TYPE_ORDER = ['story', 'task', 'bug', 'subtask', 'epic'];

const PERIOD_GROUPS: { label: string; periods: OverviewPeriod[] }[] = [
  { label: 'overview.periodGroup.sprints', periods: ['currentSprint', 'lastThreeSprints'] },
  { label: 'overview.periodGroup.calendar', periods: ['thisMonth', 'thisQuarter', 'thisYear'] },
];

const CATEGORY_COLORS: SemanticToken[] = ['border-strong', 'accent-default', 'status-done-text'];
const CATEGORY_LABELS = ['overview.toDo', 'overview.inProgress', 'overview.done'];

export function SummaryTab({ space }: { space: Space }) {
  const { t, i18n } = useTranslation();
  const [period, setPeriod] = useState<OverviewPeriod>('currentSprint');
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const summary = useOverviewSummary(space.id, period);
  const statuses = useStatuses(space.id);
  const types = useIssueTypes(space.id);

  const issuesPath = `/spaces/${space.key}/issues`;
  const withStatuses = (categories: number[]) => {
    const ids = (statuses.data ?? []).filter((status) => categories.includes(status.category ?? 0) && (!status.projectId || status.projectId === space.id)).map((status) => status.id!);
    return `${issuesPath}?${ids.map((id) => `status=${id}`).join('&')}`;
  };
  const withType = (icon: string) => {
    const id = types.data?.find((type) => type.icon === icon)?.id;
    return id ? `${issuesPath}?type=${id}` : issuesPath;
  };

  const periodName = (value: OverviewPeriod, data?: OverviewSummary) =>
    value === 'currentSprint' && data?.range.sprintName
      ? t('overview.period.currentSprintNamed', { name: data.range.sprintName })
      : t(`overview.period.${value}`);
  const sprintName = period === 'currentSprint' ? summary.data?.range.sprintName : undefined;
  const inPeriod = sprintName ? t('overview.inSprint', { name: sprintName }) : t(`overview.inPeriod.${period}`);
  // Card headers name the period briefly: the sprint itself when one is running.
  const periodTag = sprintName ?? t(`overview.period.${period}`);

  // Every type of the space shows, in a fixed order, so a zero reads as zero rather than as missing.
  const counted = summary.data?.byType ?? [];
  const doneStatuses = new Set((statuses.data ?? []).filter((status) => status.category === 2).map((status) => status.name));
  const byType = [...(types.data ?? [])]
    .sort((a, b) => TYPE_ORDER.indexOf(a.icon ?? '') - TYPE_ORDER.indexOf(b.icon ?? ''))
    .map((type) => ({ icon: type.icon ?? '', name: type.name ?? '', count: counted.find((item) => item.icon === type.icon)?.count ?? 0 }));
  const velocity = summary.data?.velocity ?? [];
  const recent = summary.data?.recent ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <Button kind="secondary" startIcon={<Calendar size={16} strokeWidth={1.5} aria-hidden />} endIcon={<ChevronDown size={16} strokeWidth={1.5} aria-hidden />} aria-haspopup="menu" onClick={(event) => setAnchor(event.currentTarget)}>
          {periodName(period, summary.data)}
        </Button>
        <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)} slotProps={{ paper: { sx: { minWidth: 260 } } }}>
          {PERIOD_GROUPS.map((group, index) => (
            <Fragment key={group.label}>
              {index > 0 && <li role="separator" className="my-1" style={{ borderTop: `1px solid ${sem('border-default')}` }} />}
              <li role="presentation" className="px-3 pb-1 pt-2" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{t(group.label)}</li>
              {group.periods.map((value) => (
                <MenuItem
                  key={value}
                  aria-current={value === period ? 'true' : undefined}
                  onClick={() => { setPeriod(value); setAnchor(null); }}
                  sx={value === period ? { color: sem('text-accent'), backgroundColor: sem('bg-selected') } : undefined}
                >
                  <span className="flex w-4 shrink-0" aria-hidden>{value === period && <Check size={16} strokeWidth={1.5} />}</span>
                  {periodName(value, value === 'currentSprint' ? summary.data : undefined)}
                </MenuItem>
              ))}
            </Fragment>
          ))}
        </Menu>
        {summary.data && <Typography variant="body2" color="textSecondary">{rangeText(summary.data, i18n.language, t)}</Typography>}
      </div>

      {summary.isPending && <LoadingState />}
      {summary.isError && <ErrorState onRetry={() => void summary.refetch()} />}
      {summary.data && (
        <>
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-5">
            <Kpi icon={Users} label={t('overview.members')} value={summary.data.members} note={t('overview.membersAdded', { count: summary.data.membersAddedInPeriod, period: inPeriod })} />
            <Kpi icon={ListTodo} label={t('overview.issues')} value={summary.data.issues.total} to={issuesPath} note={t('overview.createdIn', { count: summary.data.issues.createdInPeriod, period: inPeriod })} />
            <Kpi icon={Inbox} label={t('overview.open')} value={summary.data.issues.toDo + summary.data.issues.inProgress} to={withStatuses([0, 1])} note={inProgressNote(summary.data, t)} />
            <Kpi icon={Check} label={t('overview.done')} value={summary.data.issues.done} to={withStatuses([2])} note={t('overview.ofAll', { percent: percent(summary.data.issues.done, summary.data.issues.total) })} />
            <Kpi icon={GitPullRequest} label={t('overview.pullRequests')} value={summary.data.pullRequests.total} note={t('overview.pullRequestsNote', { open: summary.data.pullRequests.open, merged: summary.data.pullRequests.merged })} />
          </div>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
            <Card title={t('overview.byStatus')} meta={periodTag}>
              <StatusDonut summary={summary.data} link={withStatuses} />
            </Card>
            <Card title={t('overview.byType')} meta={t('overview.issueCount', { count: summary.data.issues.total })}>
              {byType.length === 0 ? <EmptyState title={t('overview.noIssues')} /> : (
                <div className="flex flex-col gap-3">
                  {byType.map((item) => (
                    <BarRow
                      key={`${item.icon}-${item.name}`}
                      label={<Link component={RouterLink} to={withType(item.icon ?? '')} color="inherit" underline="hover">{item.name}</Link>}
                      value={item.count}
                      max={Math.max(...byType.map((row) => row.count), 1)}
                      color={TYPE_COLORS[item.icon ?? ''] ?? 'accent-default'}
                    />
                  ))}
                </div>
              )}
            </Card>
            <Card title={t('overview.issueActivity')} meta={periodTag}>
              <div className="flex flex-col">
                <ActivityRow label={t('overview.created')} note={summary.data.activity.lastCreatedAt && t('overview.lastCreated', { when: formatRelative(summary.data.activity.lastCreatedAt, i18n.language) })} value={summary.data.activity.created} />
                <ActivityRow label={t('overview.updated')} note={summary.data.activity.lastUpdatedAt && t('overview.lastUpdated', { when: formatRelative(summary.data.activity.lastUpdatedAt, i18n.language) })} value={summary.data.activity.updated} />
                <ActivityRow label={t('overview.completed')} note={summary.data.activity.lastCompletedAt && t('overview.lastCompleted', { when: formatRelative(summary.data.activity.lastCompletedAt, i18n.language) })} value={summary.data.activity.completed} last />
              </div>
            </Card>
          </div>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <Card title={t('overview.velocity')} meta={t('overview.velocityMeta', { count: summary.data.velocitySprints })}>
              {velocity.length === 0 ? <EmptyState title={t('overview.noVelocity')} /> : (
                <div className="flex flex-col gap-3">
                  {velocity.map((item) => (
                    <BarRow
                      key={item.user.id}
                      label={<span className="flex min-w-0 items-center gap-2"><Avatar name={item.user.name ?? ''} src={item.user.avatarUrl} /><span className="truncate">{item.user.name}</span></span>}
                      value={item.points}
                      display={t('overview.points', { count: item.points })}
                      max={Math.max(...velocity.map((row) => row.points), 1)}
                      color="accent-default"
                      wide
                    />
                  ))}
                </div>
              )}
            </Card>
            <Card title={t('overview.recent')} action={<Link component={RouterLink} to={issuesPath} underline="hover" sx={{ fontSize: font.size.xs }}>{t('overview.viewAll')}</Link>}>
              {recent.length === 0 ? <EmptyState title={t('overview.noActivity')} /> : (
                <ul className="m-0 flex list-none flex-col gap-3 p-0">
                  {recent.map((item, index) => (
                    <li key={`${item.at}-${index}`} className="flex items-center gap-2">
                      <Avatar name={item.actor?.name ?? ''} src={item.actor?.avatarUrl} />
                      <span className="min-w-0 flex-1 truncate" style={{ fontSize: font.size.md }}>
                        <span style={{ fontWeight: font.weight.medium }}>{item.actor?.name ?? t('common.deactivatedUser')}</span>{' '}
                        <EventText item={item} doneStatuses={doneStatuses} />
                      </span>
                      <span className="shrink-0" style={{ fontSize: font.size.xs, color: sem('text-tertiary') }}>{shortAgo(item.at, i18n.language, t)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>
        </>
      )}
    </div>
  );
}

const percent = (part: number, total: number) => (total === 0 ? 0 : Math.round((part / total) * 100));

function rangeText(summary: OverviewSummary, language: string, t: (key: string, options?: Record<string, unknown>) => string) {
  const day = (value: string, year: boolean) =>
    new Intl.DateTimeFormat(language, { month: 'short', day: 'numeric', ...(year ? { year: 'numeric' } : {}) }).format(new Date(`${value}T00:00:00`));
  const range = `${day(summary.range.from, false)} – ${day(summary.range.to, true)}`;
  return summary.range.daysLeft != null ? `${range} · ${t('overview.daysLeft', { count: summary.range.daysLeft })}` : range;
}

type Event = NonNullable<OverviewSummary['recent']>[number];

/** "moved SB-81 to In Progress", "completed SB-12", "logged 2h on SB-27". */
function EventText({ item, doneStatuses }: { item: Event; doneStatuses: Set<string | null | undefined> }) {
  const { t } = useTranslation();
  const key = <Link component={RouterLink} to={`/issues/${item.issueKey}`} underline="hover" title={item.issueTitle ?? undefined}>{item.issueKey}</Link>;
  if (item.kind === 0 || item.kind === 2) {
    return <>{t(item.kind === 0 ? 'overview.event.created' : 'overview.event.commented')} {key}</>;
  }
  if (item.field === 'status' && item.newValue) {
    return doneStatuses.has(item.newValue)
      ? <>{t('overview.event.completed')} {key}</>
      : <>{t('overview.event.status')} {key} {t('overview.event.toStatus', { status: item.newValue })}</>;
  }
  if (item.field === 'workLog' && item.newValue) {
    return <>{t('overview.event.logged', { time: formatMinutes(Number(item.newValue)) })} {key}</>;
  }
  const known = ['assignee', 'description', 'title', 'priority', 'workLog', 'sprint'];
  return <>{t(`overview.event.${known.includes(item.field ?? '') ? item.field : 'changed'}`)} {key}</>;
}

/** "12 in progress, 5 in review", from the statuses the open work sits in. */
function inProgressNote(summary: OverviewSummary, t: (key: string, options?: Record<string, unknown>) => string) {
  const parts = (summary.issues.inProgressByStatus ?? []).map((item) => t('overview.inStatus', { count: item.count, status: (item.name ?? '').toLowerCase() }));
  return parts.length > 0 ? parts.join(', ') : t('overview.inProgressCount', { count: 0 });
}

const HOUR = 60;
const DAY = 24 * HOUR;

/** "5m ago", "2h ago", "Yesterday", then the date. */
function shortAgo(value: string, language: string, t: (key: string, options?: Record<string, unknown>) => string) {
  const minutes = Math.max(0, Math.round((Date.now() - Date.parse(value)) / 60_000));
  if (minutes < 1) {
    return t('overview.ago.now');
  }
  if (minutes < HOUR) {
    return t('overview.ago.minutes', { count: minutes });
  }
  if (minutes < DAY) {
    return t('overview.ago.hours', { count: Math.floor(minutes / HOUR) });
  }
  if (minutes < 2 * DAY) {
    return t('overview.ago.yesterday');
  }
  return new Intl.DateTimeFormat(language, { month: 'short', day: 'numeric' }).format(new Date(value));
}

function Card({ title, meta, action, children }: { title: string; meta?: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4 p-4" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card, backgroundColor: sem('bg-surface') }}>
      <div className="flex items-center gap-2">
        <Typography variant="h3" component="h2" className="flex-1">{title}</Typography>
        {meta && <Typography variant="body2" color="textSecondary" sx={{ fontSize: font.size.xs }}>{meta}</Typography>}
        {action}
      </div>
      {children}
    </section>
  );
}

function Kpi({ icon: Icon, label, value, note, to }: { icon: LucideIcon; label: string; value: number; note: string; to?: string }) {
  const number = <span style={{ fontSize: font.size.xl, fontWeight: font.weight.semibold, color: sem('text-primary') }}>{value}</span>;
  return (
    <section className="flex flex-col gap-1 p-4" style={{ border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
      <span className="flex items-center gap-2" style={{ fontSize: font.size.md, color: sem('text-secondary') }}>
        <Icon size={16} strokeWidth={1.5} aria-hidden />
        {label}
      </span>
      {to ? <Link component={RouterLink} to={to} underline="hover" aria-label={`${label}: ${value}`}>{number}</Link> : number}
      <span className="truncate" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{note}</span>
    </section>
  );
}

function BarRow({ label, value, display, max, color, wide }: { label: ReactNode; value: number; display?: string; max: number; color: SemanticToken; wide?: boolean }) {
  return (
    <div className="flex items-center gap-3" style={{ fontSize: font.size.md }}>
      <span className="min-w-0 shrink-0 truncate" style={{ width: wide ? 160 : 72 }}>{label}</span>
      <span className="h-2 flex-1 overflow-hidden" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.full }}>
        <span className="block h-full" style={{ width: `${max === 0 ? 0 : (value / max) * 100}%`, backgroundColor: sem(color), borderRadius: radius.full }} />
      </span>
      <span className="shrink-0 text-right" style={{ minWidth: wide ? 56 : 32 }}>{display ?? value}</span>
    </div>
  );
}

function ActivityRow({ label, note, value, last }: { label: string; note?: string | null | false; value: number; last?: boolean }) {
  return (
    <div className="flex items-center gap-2 py-2" style={{ borderBottom: last ? undefined : `1px solid ${sem('border-default')}` }}>
      <span className="flex min-w-0 flex-1 flex-col">
        <span style={{ fontSize: font.size.md }}>{label}</span>
        {note && <span className="truncate" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{note}</span>}
      </span>
      <span style={{ fontSize: font.size.lg, fontWeight: font.weight.semibold }}>{value}</span>
    </div>
  );
}

const DONUT_RADIUS = 40;
const DONUT_STROKE = 14;

/** A ring per status category; each slice links to the issues it counts. */
function StatusDonut({ summary, link }: { summary: OverviewSummary; link: (categories: number[]) => string }) {
  const { t } = useTranslation();
  const counts = [summary.issues.toDo, summary.issues.inProgress, summary.issues.done];
  const total = counts.reduce((sum, count) => sum + count, 0);
  if (total === 0) {
    return <EmptyState title={t('overview.noIssues')} />;
  }

  const circumference = 2 * Math.PI * DONUT_RADIUS;
  let offset = 0;
  const order = [2, 1, 0];

  return (
    <div className="flex items-center gap-6">
      <div className="relative shrink-0" style={{ width: 112, height: 112 }}>
        <svg viewBox="0 0 112 112" width={112} height={112} role="img" aria-label={t('overview.byStatus')}>
          <g transform="rotate(-90 56 56)">
            {order.map((category) => {
              const length = (counts[category] / total) * circumference;
              const slice = (
                <circle
                  key={category}
                  cx={56}
                  cy={56}
                  r={DONUT_RADIUS}
                  fill="none"
                  stroke={sem(CATEGORY_COLORS[category])}
                  strokeWidth={DONUT_STROKE}
                  strokeDasharray={`${length} ${circumference - length}`}
                  strokeDashoffset={-offset}
                />
              );
              offset += length;
              return slice;
            })}
          </g>
        </svg>
        <span className="absolute inset-0 flex flex-col items-center justify-center">
          <span style={{ fontSize: font.size.xl, fontWeight: font.weight.semibold }}>{total}</span>
          <span style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{t('overview.issuesUnit', { count: total })}</span>
        </span>
      </div>
      <ul className="m-0 flex list-none flex-col gap-2 p-0" style={{ fontSize: font.size.md }}>
        {order.map((category) => (
          <li key={category} className="flex items-center gap-2">
            <span aria-hidden className="size-2 shrink-0" style={{ backgroundColor: sem(CATEGORY_COLORS[category]), borderRadius: radius.full }} />
            <Link component={RouterLink} to={link([category])} color="inherit" underline="hover">{t(CATEGORY_LABELS[category])}</Link>
            <span style={{ fontWeight: font.weight.medium }}>{percent(counts[category], total)}%</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
