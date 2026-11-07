import Typography from '@mui/material/Typography';
import { FileQuestion } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { Button } from '../components/Button';
import { ThemedIllustration } from '../theme/ThemedIllustration';
import { radius, sem } from '../theme/tokens';

/** React Router numbers its history entries; the first page of a visit is 0, so there is nowhere to go back to. */
const hasHistory = () => ((window.history.state as { idx?: number } | null)?.idx ?? 0) > 0;

/**
 * One screen for everything that cannot be found, from a mistyped address to an issue the person may not see.
 * Missing and forbidden read the same, so the page never tells whether something exists.
 */
export function NotFoundPage({ title, description }: { title?: string; description?: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <section className="flex min-h-[60vh] w-full flex-col items-center justify-center gap-2 px-4 py-12 text-center">
      <ThemedIllustration
        kind="notFound"
        fallback={
          <span
            aria-hidden
            className="mb-2 flex size-12 items-center justify-center"
            style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.card, color: sem('text-secondary') }}
          >
            <FileQuestion size={24} strokeWidth={1.5} />
          </span>
        }
      />
      <Typography variant="body2" color="textSecondary">{t('notFound.code')}</Typography>
      <Typography variant="h2" component="h1">{title ?? t('notFound.title')}</Typography>
      <Typography color="textSecondary" className="max-w-md">{description ?? t('notFound.description')}</Typography>
      <div className="mt-2 flex flex-wrap justify-center gap-2">
        <Button component={Link} to="/">{t('notFound.back')}</Button>
        {hasHistory() && <Button kind="secondary" onClick={() => void navigate(-1)}>{t('notFound.goBack')}</Button>}
      </div>
    </section>
  );
}
