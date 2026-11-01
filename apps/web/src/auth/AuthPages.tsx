import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import logo from '../../../../assets/brand/sprint-boot-logo.png';
import { ApiError, apiClient, fetchCsrfToken, unwrap } from '../api';
import { Button } from '../components/Button';
import { TextField } from '../components/TextField';
import { useErrorMessage } from '../lib/errors';
import { font, radius, sem } from '../theme/tokens';
import { userManager } from './oidc';

function AuthCard({ title, children }: { title: string; children: ReactNode }) {
  const { t } = useTranslation();

  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <div
        className="flex w-full flex-col gap-4 p-8"
        style={{
          maxWidth: 400,
          backgroundColor: sem('bg-surface'),
          border: `1px solid ${sem('border-default')}`,
          borderRadius: radius.card,
        }}
      >
        <div
          className="flex items-center gap-2"
          style={{ color: sem('text-accent'), fontSize: font.size.md, fontWeight: font.weight.semibold }}
        >
          <img src={logo} alt="" width={24} height={24} />
          {t('app.name')}
        </div>
        <Typography variant="h1">{title}</Typography>
        {children}
      </div>
    </main>
  );
}

function FormError({ message }: { message: string | null }) {
  return message ? (
    <Typography role="alert" variant="body2" sx={{ color: sem('feedback-danger-text') }}>
      {message}
    </Typography>
  ) : null;
}

export function LoginPage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [params] = useSearchParams();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const csrf = await fetchCsrfToken();
      await unwrap(
        apiClient.POST('/api/v1/auth/login', {
          body: { email, password },
          headers: { RequestVerificationToken: csrf },
        }),
      );
      // Only continue an authorize request of our own, never an arbitrary URL.
      const returnUrl = params.get('ReturnUrl');
      if (returnUrl?.startsWith('/connect/authorize')) {
        window.location.assign(returnUrl);
      } else {
        await userManager.signinRedirect();
      }
    } catch (failure) {
      setError(toMessage(failure));
      setBusy(false);
    }
  };

  return (
    <AuthCard title={t('auth.signIn')}>
      <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
        <TextField label={t('auth.email')} type="email" required autoFocus autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} />
        <TextField label={t('auth.password')} type="password" required autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} />
        <FormError message={error} />
        <div className="flex justify-end">
          <Link component={RouterLink} to="/forgot-password" variant="body2">{t('auth.forgotLink')}</Link>
        </div>
        <Button type="submit" disabled={busy}>{t('auth.signIn')}</Button>
      </form>
    </AuthCard>
  );
}

export function AuthCallbackPage() {
  const navigate = useNavigate();

  useEffect(() => {
    userManager
      .signinCallback()
      .then((user) => {
        const state = user?.state as { returnTo?: string } | undefined;
        void navigate(state?.returnTo ?? '/', { replace: true });
      })
      .catch(() => void navigate('/login', { replace: true }));
  }, [navigate]);

  return null;
}

export function ForgotPasswordPage() {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const csrf = await fetchCsrfToken();
      await unwrap(
        apiClient.POST('/api/v1/auth/forgot-password', {
          body: { email, language: i18n.language },
          headers: { RequestVerificationToken: csrf },
        }),
      );
      setSent(true);
    } catch (failure) {
      setError(toMessage(failure));
    }
  };

  return (
    <AuthCard title={t('auth.forgotTitle')}>
      {sent ? (
        <Typography role="status" color="textSecondary">{t('auth.forgotSent')}</Typography>
      ) : (
        <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
          <Typography color="textSecondary">{t('auth.forgotHint')}</Typography>
          <TextField label={t('auth.email')} type="email" required autoFocus value={email} onChange={(event) => setEmail(event.target.value)} />
          <FormError message={error} />
          <Button type="submit">{t('auth.forgotAction')}</Button>
        </form>
      )}
      <Link component={RouterLink} to="/login" variant="body2">{t('auth.backToSignIn')}</Link>
    </AuthCard>
  );
}

export function ResetPasswordPage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? '';
  const token = params.get('token') ?? '';
  const [valid, setValid] = useState<boolean | null>(null);
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const check = async () => {
      const csrf = await fetchCsrfToken();
      const status = await unwrap(
        apiClient.POST('/api/v1/auth/validate-reset-token', {
          body: { userId, token },
          headers: { RequestVerificationToken: csrf },
        }),
      );
      setValid(status.isValid ?? false);
    };
    check().catch(() => setValid(false));
  }, [userId, token]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (password !== confirm) {
      setError(t('auth.passwordMismatch'));
      return;
    }
    try {
      const csrf = await fetchCsrfToken();
      await unwrap(
        apiClient.POST('/api/v1/auth/reset-password', {
          body: { userId, token, newPassword: password },
          headers: { RequestVerificationToken: csrf },
        }),
      );
      await navigate('/login');
    } catch (failure) {
      if (failure instanceof ApiError && failure.code?.endsWith('ResetLinkInvalid')) {
        setValid(false);
      } else {
        setError(toMessage(failure));
      }
    }
  };

  if (valid === null) {
    return null;
  }

  if (!valid) {
    return (
      <AuthCard title={t('auth.resetInvalidTitle')}>
        <Typography color="textSecondary">{t('auth.resetInvalidBody')}</Typography>
        <Button component={RouterLink} to="/forgot-password">{t('auth.requestNewLink')}</Button>
      </AuthCard>
    );
  }

  return (
    <AuthCard title={t('auth.setPasswordTitle')}>
      <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
        <TextField label={t('auth.newPassword')} type="password" required autoFocus autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} />
        <TextField label={t('auth.confirmPassword')} type="password" required autoComplete="new-password" value={confirm} onChange={(event) => setConfirm(event.target.value)} />
        <FormError message={error} />
        <Button type="submit">{t('auth.setPasswordAction')}</Button>
      </form>
    </AuthCard>
  );
}

export function SessionExpiredPage() {
  const { t } = useTranslation();

  return (
    <AuthCard title={t('auth.sessionExpiredTitle')}>
      <Typography color="textSecondary">{t('auth.sessionExpiredBody')}</Typography>
      <Button onClick={() => void userManager.removeUser().then(() => userManager.signinRedirect())}>
        {t('auth.signInAgain')}
      </Button>
    </AuthCard>
  );
}
