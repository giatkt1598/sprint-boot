import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { isNotFound } from '../../lib/errors';
import { formatDate } from '../../lib/format';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { font, sem } from '../../theme/tokens';
import { useSpace } from '../spaces/api';
import { SectionCard, StaticIssueList } from './BacklogPage';
import { IssuePanel } from './IssuePanel';
import { useBacklog, type Card } from './api';

/** A closed sprint on its own address, so it can be shared, reloaded and left with the browser's Back. */
export function ClosedSprintPage() {
  const { t, i18n } = useTranslation();
  const { spaceKey, sprintId } = useParams();
  const space = useSpace(spaceKey);
  const backlog = useBacklog(space.data?.id);
  const [openIssue, setOpenIssue] = useState<string | null>(null);
  // Only the sprints this space lists as closed open here: a running sprint or one of another space does not.
  const listed = backlog.data?.closedSprints?.some((item) => item.id === sprintId) ?? false;
  const closed = useQuery({
    queryKey: ['sprints', sprintId, 'closed'],
    enabled: listed,
    queryFn: () => unwrap(apiClient.GET('/api/v1/sprint/{id}/closed', { params: { path: { id: sprintId! } } })),
  });

  const missing = <NotFoundPage title={t('notFound.sprintTitle')} description={t('notFound.sprintDescription')} />;
  if (space.isPending || backlog.isPending || (listed && closed.isPending)) {
    return <LoadingState />;
  }
  if (backlog.isError) {
    return <ErrorState onRetry={() => void backlog.refetch()} />;
  }
  if (!listed || (closed.isError && isNotFound(closed.error))) {
    return missing;
  }
  if (closed.isError) {
    return <ErrorState onRetry={() => void closed.refetch()} />;
  }

  const sprint = closed.data!.sprint!;
  const day = (value?: string | null) =>
    value ? new Intl.DateTimeFormat(i18n.language, { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(`${value}T00:00:00`)) : '';
  const meta = [
    sprint.startDate && sprint.endDate ? `${day(sprint.startDate)} – ${day(sprint.endDate)}` : '',
    sprint.completedAt ? t('agile.closedOn', { date: formatDate(sprint.completedAt, i18n.language) }) : '',
    t('agile.pointsProgress', { done: sprint.donePoints, total: sprint.totalPoints }),
  ];
  const backlogPath = `/spaces/${space.data?.key}/backlog`;

  return (
    <div className="flex flex-col gap-4">
      <nav aria-label="breadcrumb" className="flex items-center gap-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
        <Link component={RouterLink} to={backlogPath} color="inherit">{space.data?.name}</Link>
        <span aria-hidden>/</span>
        <Link component={RouterLink} to={backlogPath} color="inherit">{t('agile.backlog')}</Link>
        <span aria-hidden>/</span>
        <span style={{ color: sem('text-primary') }}>{sprint.name}</span>
      </nav>
      <div className="flex flex-col gap-1">
        <div className="flex items-center gap-2">
          <Typography variant="h1">{sprint.name}</Typography>
          <Badge tone="subtle">{t('agile.sprintClosed')}</Badge>
        </div>
        <Typography variant="body2" color="textSecondary">{meta.filter(Boolean).join(' · ')}</Typography>
      </div>
      {sprint.goal && (
        <div className="flex flex-col gap-1">
          <Typography variant="h3" component="h2">{t('agile.goal')}</Typography>
          <Typography>{sprint.goal}</Typography>
        </div>
      )}

      <Group title={t('agile.completed')} cards={closed.data!.completed ?? []} empty={t('agile.noneCompleted')} onOpen={setOpenIssue} />
      <Group title={t('agile.notCompleted')} cards={closed.data!.notCompleted ?? []} empty={t('agile.noneNotCompleted')} onOpen={setOpenIssue} />
      <IssuePanel issueKey={openIssue} onClose={() => setOpenIssue(null)} />
    </div>
  );
}

function Group({ title, cards, empty, onOpen }: { title: string; cards: Card[]; empty: string; onOpen: (key: string) => void }) {
  const { t } = useTranslation();

  return (
    <SectionCard>
      <div className="flex items-center gap-2 px-2">
        <Typography variant="h3" component="h2">{title}</Typography>
        <Typography variant="body2" color="textSecondary">{t('agile.issueCount', { count: cards.length })}</Typography>
      </div>
      {cards.length > 0 ? <StaticIssueList cards={cards} onOpen={onOpen} /> : <div className="px-2"><EmptyState inline title={empty} /></div>}
    </SectionCard>
  );
}
