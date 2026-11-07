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

export function EmptyState({ title, action }: { title: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-start gap-2">
      <ThemedIllustration kind="empty" />
      <Typography color="textSecondary">{title}</Typography>
      {action}
    </div>
  );
}
