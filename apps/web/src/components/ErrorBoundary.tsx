import Typography from '@mui/material/Typography';
import { TriangleAlert } from 'lucide-react';
import { Component, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ThemedIllustration } from '../theme/ThemedIllustration';
import { radius, sem } from '../theme/tokens';
import { Button } from './Button';

/** Keeps a render failure from blanking the whole app. */
export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override render() {
    return this.state.failed ? <CrashPage /> : this.props.children;
  }
}

/** The app's own error screen, drawn by the theme when it has a picture for it. */
function CrashPage() {
  const { t } = useTranslation();

  return (
    <main className="flex min-h-screen w-full flex-col items-center justify-center gap-2 px-4 py-12 text-center">
      <ThemedIllustration
        kind="error"
        fallback={
          <span
            aria-hidden
            className="mb-2 flex size-12 items-center justify-center"
            style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.card, color: sem('text-secondary') }}
          >
            <TriangleAlert size={24} strokeWidth={1.5} />
          </span>
        }
      />
      <Typography variant="h2" component="h1" role="alert">{t('errors.crashTitle')}</Typography>
      <Typography color="textSecondary" className="max-w-md">{t('errors.crashDescription')}</Typography>
      <div className="mt-2">
        <Button onClick={() => window.location.reload()}>{t('common.retry')}</Button>
      </div>
    </main>
  );
}
