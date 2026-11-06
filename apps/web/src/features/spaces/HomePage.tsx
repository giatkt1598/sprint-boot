import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate } from 'react-router';
import { useCurrentUser } from '../../auth/AuthProvider';
import { Button } from '../../components/Button';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { CreateSpaceDialog } from './SpacesPage';
import { useSpaces } from './api';

const storageKey = (userId: string | undefined) => `sb.lastSpace.${userId}`;

export const rememberSpace = (userId: string | undefined, spaceKey: string) => localStorage.setItem(storageKey(userId), spaceKey);

/** Matches the server's SpaceView values, in order. */
export const SPACE_VIEWS = ['backlog', 'board', 'issues', 'epics'] as const;

export const HomePageKind = { lastSpace: 0, space: 1, search: 2, notifications: 3 } as const;

/**
 * Home is the page the person chose in their profile. A chosen space they can no longer open, or none
 * chosen, means the backlog of the last space opened on this browser, or of the first one.
 */
export function HomePage() {
  const { t } = useTranslation();
  const me = useCurrentUser();
  const spaces = useSpaces();
  const [creating, setCreating] = useState(false);

  if (spaces.isPending) {
    return <LoadingState />;
  }
  if (spaces.isError) {
    return <ErrorState onRetry={() => void spaces.refetch()} />;
  }

  const home = me.homePage;
  if (home?.kind === HomePageKind.search) {
    return <Navigate to="/search" replace />;
  }
  if (home?.kind === HomePageKind.notifications) {
    return <Navigate to="/notifications" replace />;
  }
  const chosen = home?.kind === HomePageKind.space ? spaces.data.find((space) => space.id === home.projectId) : undefined;
  if (chosen) {
    return <Navigate to={`/spaces/${chosen.key}/${SPACE_VIEWS[home?.view ?? 0] ?? 'backlog'}`} replace />;
  }

  const last = localStorage.getItem(storageKey(me.id));
  const target = spaces.data.find((space) => space.key === last) ?? spaces.data[0];
  if (target) {
    return <Navigate to={`/spaces/${target.key}/backlog`} replace />;
  }

  return (
    <div className="flex flex-col items-start gap-4">
      <Typography variant="h1">{t('spaces.empty')}</Typography>
      <Button onClick={() => setCreating(true)}>{t('spaces.create')}</Button>
      {creating && <CreateSpaceDialog open onClose={() => setCreating(false)} />}
    </div>
  );
}
