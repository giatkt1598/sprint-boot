import Typography from '@mui/material/Typography';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '../components/Button';

export function NotFoundPage() {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col items-start gap-4">
      <Typography variant="h1">{t('notFound.title')}</Typography>
      <Typography color="textSecondary">{t('notFound.description')}</Typography>
      <Button component={Link} to="/" kind="secondary">
        {t('notFound.back')}
      </Button>
    </div>
  );
}
