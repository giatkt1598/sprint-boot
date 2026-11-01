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

/** Home is the backlog of the last space opened on this browser, or of the first one. */
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
