import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Clock } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { Popover } from '../../components/Popover';
import { font, radius, sem } from '../../theme/tokens';

export const RECENT_KEY = ['issues', 'recent'];

/** Notes that the issue on screen was opened, so it joins the person's recents. Once per issue, after it loaded. */
export function useRecordVisit(issueId: string | undefined) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!issueId) {
      return;
    }
    void apiClient
      .POST('/api/v1/recent-issue/visit/{issueId}', { params: { path: { issueId } } })
      .then(() => queryClient.invalidateQueries({ queryKey: RECENT_KEY }));
  }, [issueId, queryClient]);
}

/** The issues and epics opened lately, newest first, from a button in the top bar. */
export function RecentsMenu() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const recents = useQuery({
    queryKey: RECENT_KEY,
    enabled: Boolean(anchor),
    queryFn: () => unwrap(apiClient.GET('/api/v1/recent-issue')),
  });

  return (
    <>
      <IconButton aria-label={t('recents.title')} aria-haspopup="dialog" size="small" sx={{ color: sem('text-secondary') }} onClick={(event) => setAnchor(event.currentTarget)}>
        <Clock size={20} strokeWidth={1.5} aria-hidden />
      </IconButton>
      <Popover width={360} open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)}>
        <Typography variant="h3" component="h2" className="px-2 pb-2">{t('recents.title')}</Typography>
        <div className="flex max-h-96 flex-col gap-1 overflow-y-auto">
          {recents.isPending && <Typography color="textSecondary" className="p-2">{t('common.loading')}</Typography>}
          {recents.isError && <Typography role="alert" className="p-2">{t('errors.loadFailed')}</Typography>}
          {recents.data?.length === 0 && <Typography color="textSecondary" className="p-2">{t('recents.empty')}</Typography>}
          {recents.data?.map((issue) => (
            <ButtonBase
              key={issue.id}
              onClick={() => {
                setAnchor(null);
                void navigate(`/issues/${issue.key}`);
              }}
              className="flex w-full items-center gap-2 p-2 text-left"
              sx={{ justifyContent: 'flex-start', borderRadius: `${radius.card}px`, '&:hover': { backgroundColor: sem('bg-hover') } }}
            >
              <span className="flex shrink-0"><IssueTypeIcon type={issue.type?.icon as IconType} /></span>
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate" style={{ fontSize: font.size.md }}>{issue.title}</span>
                <span style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{issue.key}</span>
              </span>
            </ButtonBase>
          ))}
        </div>
      </Popover>
    </>
  );
}
