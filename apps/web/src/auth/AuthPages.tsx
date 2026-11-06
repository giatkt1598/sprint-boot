import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import logo from '../../../../assets/brand/sprint-boot-logo.png';
import { ApiError, apiClient, fetchCsrfToken, unwrap } from '../api';
import { BrandSignInButton } from '../components/BrandSignInButton';
import { CustomSignInButton } from '../components/CustomSignInButton';
import { Button } from '../components/Button';
import { Dialog } from '../components/Dialog';
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

type SignInProvider = components['schemas']['SignInProviderDto'];

const LDAP = 4;

/** After any sign-in on our page: continue our own authorize request, or start one. */
async function continueSignIn(returnUrl: string | null) {
  if (returnUrl?.startsWith('/connect/authorize')) {
    window.location.assign(returnUrl);
  } else {
    await userManager.signinRedirect();
  }
}

/** LDAP has no page of its own to send people to, so they type their directory password here. */
function LdapSignInDialog({ provider, query, onClose }: { provider: SignInProvider; query: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const params = new URLSearchParams(query);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const csrf = await fetchCsrfToken();
      await unwrap(
        apiClient.POST('/api/v1/auth/ldap-login', {
          body: { provider: provider.name!, userName, password, invitation: params.get('invitation') },
          headers: { RequestVerificationToken: csrf },
        }),
      );
      await continueSignIn(params.get('returnUrl'));
    } catch (failure) {
      setError(toMessage(failure));
      setBusy(false);
    }
  };

  return (
    <Dialog open title={t('auth.ldapTitle', { provider: provider.displayName })} onClose={onClose}>
      <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
        <FormError message={error} />
        <TextField label={t('auth.userName')} autoComplete="username" autoFocus required value={userName} onChange={(event) => setUserName(event.target.value)} />
        <TextField label={t('auth.password')} type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />
        <div className="flex justify-end gap-2">
          <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={busy || !userName.trim() || !password}>{t('auth.ldapSignIn')}</Button>
        </div>
      </form>
    </Dialog>
  );
}

/** Full page navigation, because the server redirects to the identity provider. */
function ProviderButtons({ providers, query }: { providers: SignInProvider[]; query: string }) {
  const { t } = useTranslation();
  const [directory, setDirectory] = useState<SignInProvider | null>(null);

  const buttons = providers.map((provider) => {
    const href = `/connect/external/${provider.name}/challenge?${query}`;
    const label = t('auth.continueWith', { provider: provider.displayName });
    if (provider.type === LDAP) {
      return <CustomSignInButton key={provider.name} onClick={() => setDirectory(provider)} label={label} logoUrl={provider.logoUrl} color={provider.buttonColor} />;
    }
    // Google and GitHub ask for their own look; any other provider uses ours.
    const kind = provider.type === 1 ? 'google' : provider.type === 2 ? 'github' : null;
    return kind ? (
      <BrandSignInButton key={provider.name} kind={kind} href={href} label={label} />
    ) : (
      <CustomSignInButton key={provider.name} href={href} label={label} logoUrl={provider.logoUrl} color={provider.buttonColor} />
    );
  });

  return (
    <>
      {buttons}
      {directory && <LdapSignInDialog provider={directory} query={query} onClose={() => setDirectory(null)} />}
    </>
  );
}

function OrDivider() {
  const { t } = useTranslation();

  return (
    <div className="flex items-center gap-2" aria-hidden>
      <span className="flex-1" style={{ borderTop: `1px solid ${sem('border-default')}` }} />
      <Typography variant="body2" color="textSecondary">{t('auth.or')}</Typography>
      <span className="flex-1" style={{ borderTop: `1px solid ${sem('border-default')}` }} />
    </div>
  );
}

export function LoginPage() {
  const { t, i18n } = useTranslation();
  const toMessage = useErrorMessage();
  const [params] = useSearchParams();
  const [showPassword, setShowPassword] = useState(false);
  const methods = useQuery({
    queryKey: ['auth', 'methods'],
    queryFn: () => unwrap(apiClient.GET('/api/v1/auth/methods')),
  });
  const providers = methods.data?.providers ?? [];
  const localEnabled = methods.data?.localLoginEnabled ?? true;
  const returnUrl = params.get('ReturnUrl');
  const challengeQuery = returnUrl ? `returnUrl=${encodeURIComponent(returnUrl)}` : '';
  const externalError = params.get('error');
  const externalMessage = externalError && i18n.exists(`errors.${externalError}`) ? t(`errors.${externalError}`) : null;
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
      <FormError message={externalMessage} />
      <ProviderButtons providers={providers} query={challengeQuery} />
      {providers.length > 0 && localEnabled && <OrDivider />}
      {localEnabled || showPassword ? (
        <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
          <TextField label={t('auth.email')} type="email" required autoFocus autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} />
          <TextField label={t('auth.password')} type="password" required autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} />
          <FormError message={error} />
          <div className="flex justify-end">
            <Link component={RouterLink} to="/forgot-password" variant="body2">{t('auth.forgotLink')}</Link>
          </div>
          <Button type="submit" disabled={busy}>{t('auth.signIn')}</Button>
        </form>
      ) : (
        <Link component="button" variant="body2" onClick={() => setShowPassword(true)}>{t('auth.adminPassword')}</Link>
      )}
    </AuthCard>
  );
}

// The code can be redeemed once, and StrictMode runs effects twice.
let pendingCallback: ReturnType<typeof userManager.signinCallback> | null = null;

export function AuthCallbackPage() {
  const navigate = useNavigate();

  useEffect(() => {
    pendingCallback ??= userManager.signinCallback();
    pendingCallback
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

function invitationSummary(t: ReturnType<typeof useTranslation>['t'], preview: components['schemas']['InvitationPreviewDto']) {
  const inviter = preview.invitedBy ?? t('app.name');
  const project = preview.projects?.[0];
  return project
    ? t('invitations.joinSpace', { inviter, space: project.projectName, role: t(`invitations.roles.${project.role}`) })
    : t('invitations.joinApp', { inviter });
}

export function AcceptInvitationPage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [gone, setGone] = useState(false);

  const preview = useQuery({
    queryKey: ['auth', 'invitation', token],
    retry: false,
    queryFn: async () => {
      const csrf = await fetchCsrfToken();
      return unwrap(
        apiClient.POST('/api/v1/auth/invitation', { body: { token }, headers: { RequestVerificationToken: csrf } }),
      );
    },
  });

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const email = preview.data?.email ?? '';
      const csrf = await fetchCsrfToken();
      await unwrap(
        apiClient.POST('/api/v1/auth/accept-invitation', {
          body: { token, name, password },
          headers: { RequestVerificationToken: csrf },
        }),
      );
      await unwrap(
        apiClient.POST('/api/v1/auth/login', {
          body: { email, password },
          headers: { RequestVerificationToken: await fetchCsrfToken() },
        }),
      );
      await userManager.signinRedirect();
    } catch (failure) {
      if (failure instanceof ApiError && failure.code?.endsWith('InvitationInvalid')) {
        setGone(true);
      } else {
        setError(toMessage(failure));
      }
    }
  };

  if (preview.isPending) {
    return null;
  }

  if (preview.isError || gone) {
    return (
      <AuthCard title={t('invitations.invalidTitle')}>
        <Typography color="textSecondary">{t('invitations.invalidBody')}</Typography>
        <Button kind="secondary" component={RouterLink} to="/login">{t('auth.backToSignIn')}</Button>
      </AuthCard>
    );
  }

  const providers = preview.data.providers ?? [];
  const localEnabled = preview.data.localLoginEnabled ?? true;

  return (
    <AuthCard title={t('invitations.joinTitle')}>
      <Typography color="textSecondary">{invitationSummary(t, preview.data)}</Typography>
      <ProviderButtons providers={providers} query={`invitation=${encodeURIComponent(token)}`} />
      {providers.length > 0 && localEnabled && <OrDivider />}
      {localEnabled && (
        <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
          <TextField label={t('auth.email')} type="email" value={preview.data.email ?? ''} disabled />
          <TextField label={t('invitations.fullName')} required autoFocus autoComplete="name" value={name} onChange={(event) => setName(event.target.value)} />
          <TextField label={t('auth.password')} type="password" required autoComplete="new-password" placeholder={t('invitations.passwordHint')} value={password} onChange={(event) => setPassword(event.target.value)} />
          <FormError message={error} />
          <Button type="submit">{t('invitations.createAccount')}</Button>
        </form>
      )}
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
