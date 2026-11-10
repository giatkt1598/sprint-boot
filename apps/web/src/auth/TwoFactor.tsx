import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { QRCodeSVG } from 'qrcode.react';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, fetchCsrfToken, unwrap } from '../api';
import { Badge } from '../components/Badge';
import { Button } from '../components/Button';
import { Dialog } from '../components/Dialog';
import { LoadingState } from '../components/StateViews';
import { TextField } from '../components/TextField';
import { useToast } from '../components/ToastHost';
import { useErrorMessage } from '../lib/errors';
import { font, radius, sem } from '../theme/tokens';
import { useCurrentUser } from './AuthProvider';
import { AuthCard, continueSignIn, FormError } from './AuthPages';
import { signOut } from './oidc';

type Setup = components['schemas']['TwoFactorSetupDto'];

/** Groups the key in fours, as authenticator apps show it, so it is easier to type. */
const groupKey = (key: string) => key.match(/.{1,4}/g)?.join(' ') ?? key;

/** Six digits, the field the app's code goes in; mobile keyboards open on numbers. */
function CodeField({ value, onChange, recovery = false }: { value: string; onChange: (value: string) => void; recovery?: boolean }) {
  const { t } = useTranslation();
  return (
    <TextField
      label={t(recovery ? 'twoFactor.recoveryCode' : 'twoFactor.code')}
      autoComplete="one-time-code"
      inputProps={{ inputMode: recovery ? 'text' : 'numeric' }}
      autoFocus
      required
      value={value}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

/** Shown once: each code signs in once in place of the app, for a lost phone. */
export function RecoveryCodes({ codes }: { codes: string[] }) {
  const { t } = useTranslation();
  const toast = useToast();
  const text = codes.join('\n');

  const download = () => {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([`${text}\n`], { type: 'text/plain' }));
    link.download = 'sprint-boot-recovery-codes.txt';
    link.click();
    URL.revokeObjectURL(link.href);
  };

  return (
    <div className="flex flex-col gap-3">
      <Typography variant="body2" color="textSecondary">{t('twoFactor.recoveryHint')}</Typography>
      <ul
        aria-label={t('twoFactor.recoveryCodes')}
        className="m-0 grid list-none grid-cols-2 gap-x-4 gap-y-1 p-3"
        style={{ fontFamily: font.mono, fontSize: font.size.md, backgroundColor: sem('bg-subtle'), borderRadius: radius.control }}
      >
        {codes.map((code) => <li key={code}>{code}</li>)}
      </ul>
      <div className="flex flex-wrap gap-2">
        <Button kind="secondary" onClick={() => void navigator.clipboard.writeText(text).then(() => toast('success', t('twoFactor.copied')))}>{t('twoFactor.copy')}</Button>
        <Button kind="secondary" onClick={download}>{t('twoFactor.download')}</Button>
      </div>
    </div>
  );
}

interface SetupStepsProps {
  /** Reads the key: for a sign-in held until setup, or for the signed-in person. */
  load: () => Promise<Setup>;
  /** Sends the first code; returns the recovery codes. */
  complete: (code: string) => Promise<string[]>;
  /** After the recovery codes are shown and the person goes on. */
  onDone: () => void;
  doneLabel: string;
}

/** Scan or type the key, prove it with a code, then keep the recovery codes. */
function SetupSteps({ load, complete, onDone, doneLabel }: SetupStepsProps) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const setup = useQuery({ queryKey: ['two-factor', 'setup'], queryFn: load, staleTime: Infinity, gcTime: 0, retry: false });
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);
  const finish = useMutation({ mutationFn: () => complete(code), onSuccess: setCodes });

  if (codes) {
    return (
      <div className="flex flex-col gap-4">
        <Typography role="status">{t('twoFactor.enabled')}</Typography>
        <RecoveryCodes codes={codes} />
        <div><Button onClick={onDone}>{doneLabel}</Button></div>
      </div>
    );
  }
  if (setup.isPending) {
    return <LoadingState />;
  }
  if (setup.isError) {
    return <FormError message={toMessage(setup.error)} />;
  }

  const submit = (event: FormEvent) => {
    event.preventDefault();
    finish.mutate();
  };

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      <Typography variant="body2" color="textSecondary">{t('twoFactor.scanHint')}</Typography>
      {/* The code's own light margin, whatever the theme: scanners read dark modules on light ground. */}
      <QRCodeSVG value={setup.data.authenticatorUri ?? ''} size={200} marginSize={4} title={t('twoFactor.qrLabel')} className="self-center" style={{ borderRadius: radius.control }} />
      <div className="flex flex-col gap-1">
        <Typography variant="body2" color="textSecondary">{t('twoFactor.keyHint')}</Typography>
        <code className="select-all break-all" style={{ fontFamily: font.mono, fontSize: font.size.md, color: sem('text-primary') }}>{groupKey(setup.data.sharedKey ?? '')}</code>
      </div>
      <CodeField value={code} onChange={setCode} />
      <FormError message={finish.isError ? toMessage(finish.error) : null} />
      <div><Button type="submit" disabled={!code.trim() || finish.isPending}>{t('twoFactor.verify')}</Button></div>
    </form>
  );
}

const heldSetup = async () => unwrap(apiClient.GET('/api/v1/auth/two-factor/held-setup'));
const accountSetup = async () => unwrap(apiClient.GET('/api/v1/auth/two-factor/setup'));

/** After the password: the code from the app, or one of the recovery codes. */
export function TwoFactorCodePage() {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [params] = useSearchParams();
  const [code, setCode] = useState('');
  const [recovery, setRecovery] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await unwrap(
        apiClient.POST('/api/v1/auth/two-factor', {
          body: { code, isRecoveryCode: recovery },
          headers: { RequestVerificationToken: await fetchCsrfToken() },
        }),
      );
      await continueSignIn(params.get('ReturnUrl'));
    } catch (failure) {
      setError(toMessage(failure));
      setBusy(false);
    }
  };

  return (
    <AuthCard title={t('twoFactor.title')}>
      <Typography variant="body2" color="textSecondary">{t(recovery ? 'twoFactor.recoveryPrompt' : 'twoFactor.codePrompt')}</Typography>
      <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
        <CodeField key={String(recovery)} value={code} onChange={setCode} recovery={recovery} />
        <FormError message={error} />
        <Button type="submit" disabled={busy || !code.trim()}>{t('twoFactor.verify')}</Button>
      </form>
      <Link component="button" variant="body2" className="self-start" onClick={() => { setRecovery(!recovery); setCode(''); setError(null); }}>
        {t(recovery ? 'twoFactor.useApp' : 'twoFactor.useRecovery')}
      </Link>
      <Link href="/login" variant="body2" className="self-start">{t('auth.backToSignIn')}</Link>
    </AuthCard>
  );
}

/** After the password, when the workspace requires two-factor and the account has none yet. */
export function TwoFactorSetupPage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();

  return (
    <AuthCard title={t('twoFactor.setupTitle')}>
      <Typography variant="body2" color="textSecondary">{t('twoFactor.requiredHint')}</Typography>
      <SetupSteps
        load={heldSetup}
        complete={async (code) =>
          (await unwrap(
            apiClient.POST('/api/v1/auth/two-factor/held-setup', { body: { code }, headers: { RequestVerificationToken: await fetchCsrfToken() } }),
          )).recoveryCodes ?? []}
        onDone={() => void continueSignIn(params.get('ReturnUrl'))}
        doneLabel={t('twoFactor.continue')}
      />
    </AuthCard>
  );
}

/** Someone signed in before the workspace required two-factor sets it up before using the app. */
export function TwoFactorRequiredScreen() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  return (
    <AuthCard title={t('twoFactor.setupTitle')}>
      <Typography variant="body2" color="textSecondary">{t('twoFactor.requiredHint')}</Typography>
      <SetupSteps
        load={accountSetup}
        complete={async (code) => (await unwrap(apiClient.POST('/api/v1/auth/two-factor/setup', { body: { code } }))).recoveryCodes ?? []}
        onDone={() => void queryClient.invalidateQueries({ queryKey: ['auth', 'me'] })}
        doneLabel={t('twoFactor.continue')}
      />
      <Link component="button" variant="body2" className="self-start" onClick={() => void signOut()}>{t('topBar.signOut')}</Link>
    </AuthCard>
  );
}

/** Asks for a current code, then runs the action with it. */
function CodeDialog({ title, description, confirmLabel, danger, run, onClose }: { title: string; description: string; confirmLabel: string; danger?: boolean; run: (code: string) => Promise<void>; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const [code, setCode] = useState('');
  const action = useMutation({ mutationFn: () => run(code) });

  return (
    <Dialog
      open
      title={title}
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" form="two-factor-code" kind={danger ? 'danger' : 'primary'} disabled={!code.trim() || action.isPending}>{confirmLabel}</Button>
        </div>
      }
    >
      <form id="two-factor-code" className="flex flex-col gap-4" onSubmit={(event) => { event.preventDefault(); action.mutate(); }}>
        <Typography color="textSecondary">{description}</Typography>
        <CodeField value={code} onChange={setCode} />
        <FormError message={action.isError ? toMessage(action.error) : null} />
      </form>
    </Dialog>
  );
}

/** Account > Security: turn the authenticator app on or off, and replace the recovery codes. */
export function TwoFactorSection() {
  const { t } = useTranslation();
  const me = useCurrentUser();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<'setup' | 'codes' | 'disable' | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['auth', 'me'] });
  const close = () => {
    setDialog(null);
    setCodes(null);
  };

  return (
    <section className="flex max-w-md flex-col gap-3">
      <div className="flex items-center gap-2">
        <Typography variant="h2" className="flex-1">{t('twoFactor.sectionTitle')}</Typography>
        <Badge tone={me.twoFactorEnabled ? 'success' : 'subtle'}>{t(me.twoFactorEnabled ? 'twoFactor.on' : 'twoFactor.off')}</Badge>
      </div>
      <Typography variant="body2" color="textSecondary">
        {t(me.twoFactorRequired ? 'twoFactor.sectionRequired' : 'twoFactor.sectionHint')}
      </Typography>
      <div className="flex flex-wrap gap-2">
        {me.twoFactorEnabled ? (
          <>
            <Button kind="secondary" onClick={() => setDialog('codes')}>{t('twoFactor.newCodes')}</Button>
            {!me.twoFactorRequired && <Button kind="ghost" onClick={() => setDialog('disable')}>{t('twoFactor.turnOff')}</Button>}
          </>
        ) : (
          <Button onClick={() => setDialog('setup')}>{t('twoFactor.turnOn')}</Button>
        )}
      </div>

      {dialog === 'setup' && (
        <Dialog open title={t('twoFactor.setupTitle')} onClose={close}>
          <SetupSteps
            load={accountSetup}
            complete={async (code) => (await unwrap(apiClient.POST('/api/v1/auth/two-factor/setup', { body: { code } }))).recoveryCodes ?? []}
            onDone={() => {
              close();
              void refresh();
            }}
            doneLabel={t('twoFactor.done')}
          />
        </Dialog>
      )}
      {dialog === 'codes' && !codes && (
        <CodeDialog
          title={t('twoFactor.newCodes')}
          description={t('twoFactor.newCodesHint')}
          confirmLabel={t('twoFactor.newCodes')}
          run={async (code) => setCodes((await unwrap(apiClient.POST('/api/v1/auth/two-factor/recovery-codes', { body: { code } }))).recoveryCodes ?? [])}
          onClose={close}
        />
      )}
      {dialog === 'codes' && codes && (
        <Dialog open title={t('twoFactor.recoveryCodes')} onClose={close} footer={<div className="flex justify-end"><Button onClick={close}>{t('twoFactor.done')}</Button></div>}>
          <RecoveryCodes codes={codes} />
        </Dialog>
      )}
      {dialog === 'disable' && (
        <CodeDialog
          title={t('twoFactor.turnOffTitle')}
          description={t('twoFactor.turnOffHint')}
          confirmLabel={t('twoFactor.turnOff')}
          danger
          run={async (code) => {
            await unwrap(apiClient.POST('/api/v1/auth/two-factor/disable', { body: { code } }));
            close();
            toast('success', t('twoFactor.turnedOff'));
            await refresh();
          }}
          onClose={close}
        />
      )}
    </section>
  );
}
