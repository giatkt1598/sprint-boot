import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Toggle } from '../../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { radius, sem } from '../../theme/tokens';

type Provider = components['schemas']['ExternalProviderDto'];
type SaveProvider = components['schemas']['SaveProviderInput'];

const OIDC = 0;
const GOOGLE = 1;
const GITHUB = 2;
const DISPLAY_NAMES: Record<number, string> = { [GOOGLE]: 'Google', [GITHUB]: 'GitHub' };

export const toProviderName = (displayName: string) =>
  displayName.toLowerCase().normalize('NFD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32);

const useProviders = () =>
  useQuery({ queryKey: ['admin', 'sso', 'providers'], queryFn: () => unwrap(apiClient.GET('/api/v1/sso-admin/providers')) });

const toInput = (provider: Provider): SaveProvider => ({
  name: provider.name!,
  displayName: provider.displayName!,
  type: provider.type,
  discoveryUrl: provider.discoveryUrl,
  clientId: provider.clientId!,
  clientSecret: '',
  scopes: provider.scopes,
  emailClaim: provider.emailClaim,
  nameClaim: provider.nameClaim,
  isEnabled: provider.isEnabled,
});

function useSaveProvider(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveProvider) =>
      unwrap(apiClient.PUT('/api/v1/sso-admin/{id}/provider', { params: { path: { id } }, body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] }),
  });
}

function PolicyCard() {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const policy = useQuery({ queryKey: ['admin', 'sso', 'policy'], queryFn: () => unwrap(apiClient.GET('/api/v1/sso-admin/policy')) });
  const [domains, setDomains] = useState('');

  useEffect(() => setDomains(policy.data?.allowedDomains ?? ''), [policy.data?.allowedDomains]);

  const save = useMutation({
    mutationFn: (body: components['schemas']['SaveAuthPolicyInput']) => unwrap(apiClient.PUT('/api/v1/sso-admin/policy', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'sso', 'policy'] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  if (policy.isPending) {
    return <LoadingState />;
  }
  if (policy.isError) {
    return <ErrorState onRetry={() => void policy.refetch()} />;
  }

  const current = {
    localLoginEnabled: policy.data.localLoginEnabled,
    selfRegistrationEnabled: policy.data.selfRegistrationEnabled,
    allowedDomains: policy.data.allowedDomains,
  };

  return (
    <section
      className="flex flex-col gap-4 p-4"
      style={{ maxWidth: 800, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
    >
      <Typography variant="h2">{t('sso.policy')}</Typography>
      <div className="flex items-center gap-4">
        <div className="flex-1">
          <Typography>{t('sso.local')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('sso.localHint')}</Typography>
        </div>
        <Toggle label={t('sso.local')} checked={current.localLoginEnabled ?? true} onChange={(event) => save.mutate({ ...current, localLoginEnabled: event.target.checked })} />
      </div>
      <div className="flex items-center gap-4">
        <div className="flex-1">
          <Typography>{t('sso.selfSignUp')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('sso.selfSignUpHint')}</Typography>
        </div>
        <Toggle label={t('sso.selfSignUp')} checked={current.selfRegistrationEnabled ?? false} onChange={(event) => save.mutate({ ...current, selfRegistrationEnabled: event.target.checked })} />
      </div>
      <TextField
        label={t('sso.domains')}
        value={domains}
        onChange={(event) => setDomains(event.target.value)}
        onBlur={() => domains !== (current.allowedDomains ?? '') && save.mutate({ ...current, allowedDomains: domains })}
      />
      <Typography variant="body2" color="textSecondary">{t('sso.domainsHint')}</Typography>
    </section>
  );
}

function AddProviderDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [type, setType] = useState<SaveProvider['type']>(OIDC);
  const [displayName, setDisplayName] = useState('');
  const [discoveryUrl, setDiscoveryUrl] = useState('');
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const shownName = DISPLAY_NAMES[type ?? OIDC] ?? displayName;

  const add = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/v1/sso-admin/provider', {
          body: {
            name: toProviderName(shownName),
            displayName: shownName,
            type,
            discoveryUrl: type === OIDC ? discoveryUrl : null,
            clientId,
            clientSecret,
            isEnabled: true,
          },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('sso.addTitle')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('sso.add')}
          confirmDisabled={!toProviderName(shownName) || !clientId || !clientSecret || (type === OIDC && !discoveryUrl) || add.isPending}
          onCancel={onClose}
          onConfirm={() => add.mutate()}
        />
      }
    >
      <SelectField
        label={t('sso.type')}
        value={String(type)}
        onChange={(event) => setType(Number(event.target.value) as SaveProvider['type'])}
        options={[
          { value: String(OIDC), label: t('sso.oidc') },
          { value: String(GOOGLE), label: 'Google' },
          { value: String(GITHUB), label: 'GitHub' },
        ]}
      />
      {type === OIDC && (
        <>
          <TextField label={t('sso.displayName')} autoFocus value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
          <TextField label={t('sso.discoveryUrl')} value={discoveryUrl} onChange={(event) => setDiscoveryUrl(event.target.value)} />
        </>
      )}
      <TextField label={t('sso.clientId')} value={clientId} onChange={(event) => setClientId(event.target.value)} />
      <TextField label={t('sso.clientSecret')} type="password" autoComplete="off" placeholder={t('sso.secretPlaceholder')} value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} />
      {add.isError && <Typography role="alert" color="error" variant="body2">{toMessage(add.error)}</Typography>}
    </Dialog>
  );
}

function ProviderToggle({ provider }: { provider: Provider }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const save = useSaveProvider(provider.id!);

  return (
    <Toggle
      label={t('sso.enable', { provider: provider.displayName })}
      checked={provider.isEnabled ?? false}
      onChange={(event) =>
        save.mutate({ ...toInput(provider), isEnabled: event.target.checked }, { onError: (failure) => toast('error', toMessage(failure)) })
      }
    />
  );
}

export function SsoPage() {
  const { t } = useTranslation();
  const providers = useProviders();
  const [adding, setAdding] = useState(false);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('sso.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('sso.subtitle')}</Typography>
        </div>
        <Button onClick={() => setAdding(true)}>{t('sso.addProvider')}</Button>
      </div>
      <PolicyCard />
      {providers.isPending && <LoadingState />}
      {providers.isError && <ErrorState onRetry={() => void providers.refetch()} />}
      {providers.data?.length === 0 && <EmptyState title={t('sso.empty')} />}
      {(providers.data?.length ?? 0) > 0 && (
        <Table<Provider>
          label={t('sso.title')}
          rows={providers.data ?? []}
          rowKey={(item) => item.id!}
          columns={[
            { key: 'name', header: t('sso.provider'), render: (item) => item.displayName },
            { key: 'type', header: t('sso.type'), render: (item) => (item.type === OIDC ? 'OIDC' : 'OAuth') },
            {
              key: 'status',
              header: t('sso.status'),
              render: (item) =>
                item.hasSecret ? <Badge tone="success">{t('sso.connected')}</Badge> : <Badge>{t('sso.notSetUp')}</Badge>,
            },
            { key: 'enabled', header: t('sso.enabled'), render: (item) => <ProviderToggle provider={item} /> },
            {
              key: 'edit',
              header: <span className="sr-only">{t('sso.edit')}</span>,
              render: (item) => (
                <Link component={RouterLink} to={item.id!} variant="body2" aria-label={`${t('sso.edit')} ${item.displayName}`}>{t('sso.edit')}</Link>
              ),
            },
          ]}
        />
      )}
      {adding && <AddProviderDialog onClose={() => setAdding(false)} />}
    </div>
  );
}

const parseMapping = (text: string) =>
  Object.fromEntries(text.split(',').map((pair) => pair.split('=').map((part) => part.trim())));

function ProviderForm({ provider }: { provider: Provider }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(provider.displayName ?? '');
  const [discoveryUrl, setDiscoveryUrl] = useState(provider.discoveryUrl ?? '');
  const [clientId, setClientId] = useState(provider.clientId ?? '');
  const [clientSecret, setClientSecret] = useState('');
  const [scopes, setScopes] = useState(provider.scopes ?? 'openid profile email');
  const [mapping, setMapping] = useState(`email=${provider.emailClaim ?? 'email'}, name=${provider.nameClaim ?? 'name'}`);
  const [deleting, setDeleting] = useState(false);
  const isOidc = provider.type === OIDC;

  const save = useSaveProvider(provider.id!);
  const test = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/sso-admin/test-connection', { body: { discoveryUrl } })),
    onSuccess: (result) => toast(result.succeeded ? 'success' : 'error', t(result.succeeded ? 'sso.testPassed' : 'sso.testFailed')),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/sso-admin/{id}/provider', { params: { path: { id: provider.id! } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] });
      await navigate('..', { relative: 'path' });
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const submit = () => {
    const claims = parseMapping(mapping) as { email?: string; name?: string };
    save.mutate(
      { ...toInput(provider), displayName, discoveryUrl: isOidc ? discoveryUrl : null, clientId, clientSecret, scopes, emailClaim: claims.email, nameClaim: claims.name },
      {
        onSuccess: () => {
          setClientSecret('');
          toast('success', t('account.saved'));
        },
        onError: (failure) => toast('error', toMessage(failure)),
      },
    );
  };

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{provider.displayName}</Typography>
        <Typography variant="body2" color="textSecondary">{isOidc ? t('sso.oidcProvider') : t('sso.oauthProvider')}</Typography>
      </div>
      <section className="flex flex-col gap-4 p-4" style={{ maxWidth: 800, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
        <Typography variant="h2">{t('sso.connection')}</Typography>
        <TextField label={t('sso.displayName')} value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        {isOidc && <TextField label={t('sso.discoveryUrl')} value={discoveryUrl} onChange={(event) => setDiscoveryUrl(event.target.value)} />}
        <TextField label={t('sso.clientId')} value={clientId} onChange={(event) => setClientId(event.target.value)} />
        <TextField label={t('sso.clientSecret')} type="password" autoComplete="off" placeholder={provider.hasSecret ? '••••••••••••' : t('sso.secretPlaceholder')} value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} />
        <Typography variant="body2" color="textSecondary">{t('sso.secretHint')}</Typography>
        {isOidc && (
          <>
            <TextField label={t('sso.scopes')} value={scopes} onChange={(event) => setScopes(event.target.value)} />
            <TextField label={t('sso.claimMapping')} value={mapping} onChange={(event) => setMapping(event.target.value)} />
          </>
        )}
        <div className="flex justify-end gap-2">
          <Button kind="ghost" onClick={() => setDeleting(true)}>{t('sso.delete')}</Button>
          {isOidc && <Button kind="secondary" disabled={test.isPending} onClick={() => test.mutate()}>{t('sso.test')}</Button>}
          <Button disabled={save.isPending} onClick={submit}>{t('sso.save')}</Button>
        </div>
      </section>
      <DialogConfirm
        open={deleting}
        title={t('sso.deleteTitle')}
        description={t('sso.deleteBody', { provider: provider.displayName })}
        confirmLabel={t('sso.delete')}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}

export function SsoProviderPage() {
  const { providerId } = useParams();
  const providers = useProviders();

  if (providers.isPending) {
    return <LoadingState />;
  }
  if (providers.isError) {
    return <ErrorState onRetry={() => void providers.refetch()} />;
  }

  const provider = providers.data.find((item) => item.id === providerId);
  return provider ? <ProviderForm key={provider.id} provider={provider} /> : <NotFoundPage />;
}
