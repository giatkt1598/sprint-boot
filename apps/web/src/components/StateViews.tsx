import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ThemedIllustration } from '../theme/ThemedIllustration';
import { Button } from './Button';

export function LoadingState() {
  const { t } = useTranslation();
  return <Typography role="status" color="textSecondary">{t('common.loading')}</Typography>;
}

export function ErrorState({ onRetry }: { onRetry?: () => void }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col items-start gap-2">
      <Typography role="alert">{t('errors.loadFailed')}</Typography>
      {onRetry && <Button kind="secondary" onClick={onRetry}>{t('common.retry')}</Button>}
    </div>
  );
}

/**
 * Nothing to show. A page that is empty gets the theme's picture; an empty part of a page, such as a comment list or
 * a card on the overview, passes `inline` and shows the text only, so a picture never crowds the content around it.
 */
export function EmptyState({ title, action, inline }: { title: string; action?: ReactNode; inline?: boolean }) {
  return (
    <div className="flex flex-col items-start gap-2">
      {!inline && <ThemedIllustration kind="empty" />}
      <Typography color="textSecondary">{title}</Typography>
      {action}
    </div>
  );
}
